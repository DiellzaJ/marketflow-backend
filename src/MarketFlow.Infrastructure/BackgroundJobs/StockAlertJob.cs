using System.Data;
using System.Text.RegularExpressions;
using MarketFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace MarketFlow.Infrastructure.BackgroundJobs;

public sealed partial class StockAlertJob
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<StockAlertJob> _logger;

    public StockAlertJob(
        ApplicationDbContext dbContext,
        ILogger<StockAlertJob> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var tenants = await GetActiveTenantsAsync(cancellationToken);

        foreach (var tenant in tenants)
        {
            try
            {
                await EnsureLowStockAlertsTableAsync(tenant, cancellationToken);

                var createdAlertCount = await CreateLowStockAlertsAsync(tenant, cancellationToken);

                if (createdAlertCount > 0)
                {
                    _logger.LogInformation(
                        "Created {AlertCount} low-stock alerts for tenant {SchemaName}.",
                        createdAlertCount,
                        tenant.SchemaName);
                }
            }
            catch (PostgresException exception) when (IsMissingTenantObject(exception))
            {
                _logger.LogWarning(
                    exception,
                    "Low-stock alert scan skipped tenant {SchemaName} because its schema is not fully provisioned.",
                    tenant.SchemaName);
            }
        }
    }

    private async Task<IReadOnlyCollection<TenantScanTarget>> GetActiveTenantsAsync(
        CancellationToken cancellationToken)
    {
        var tenants = new List<TenantScanTarget>();

        await using var command = await CreateCommandAsync("""
            SELECT id, schema_name
            FROM public.companies
            WHERE is_active = TRUE
            ORDER BY id;
            """, cancellationToken);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            tenants.Add(new TenantScanTarget(reader.GetInt32(0), reader.GetString(1)));
        }

        return tenants;
    }

    private async Task EnsureLowStockAlertsTableAsync(
        TenantScanTarget tenant,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var command = await CreateCommandAsync(
                "SELECT public.ensure_tenant_low_stock_alerts_table(@schema_name);",
                cancellationToken);

            command.Parameters.AddWithValue("schema_name", tenant.SchemaName);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException exception) when (IsUndefinedFunction(exception))
        {
            await EnsureLowStockAlertsTableInlineAsync(tenant, cancellationToken);
        }
    }

    private async Task EnsureLowStockAlertsTableInlineAsync(
        TenantScanTarget tenant,
        CancellationToken cancellationToken)
    {
        var schemaName = QuoteTenantSchemaName(tenant.SchemaName);

        await using var transaction = await BeginTransactionAsync(cancellationToken);

        await using (var lockCommand = await CreateCommandAsync(
            "SELECT pg_advisory_xact_lock(hashtext(@lock_key));",
            cancellationToken,
            transaction))
        {
            lockCommand.Parameters.AddWithValue(
                "lock_key",
                $"tenant:{tenant.SchemaName}:low_stock_alerts");

            await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var command = await CreateCommandAsync($"""
            CREATE TABLE IF NOT EXISTS {schemaName}.low_stock_alerts (
                id                BIGSERIAL PRIMARY KEY,
                inventory_id      INT         NOT NULL REFERENCES {schemaName}.inventory(id) ON DELETE CASCADE,
                product_id        INT         NOT NULL REFERENCES {schemaName}.products(id) ON DELETE CASCADE,
                market_id         INT         NOT NULL REFERENCES {schemaName}.markets(id) ON DELETE CASCADE,
                department_id     INT         REFERENCES {schemaName}.departments(id) ON DELETE SET NULL,
                quantity          INT         NOT NULL,
                min_stock_alert   INT         NOT NULL,
                status            VARCHAR(20) NOT NULL DEFAULT 'Active'
                    CHECK (status IN ('Active', 'Resolved')),
                first_detected_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                last_detected_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                resolved_at       TIMESTAMPTZ
            );
            CREATE INDEX IF NOT EXISTS idx_low_stock_alerts_inventory
                ON {schemaName}.low_stock_alerts(inventory_id);
            CREATE INDEX IF NOT EXISTS idx_low_stock_alerts_status
                ON {schemaName}.low_stock_alerts(status);
            CREATE UNIQUE INDEX IF NOT EXISTS ux_low_stock_alerts_active_product_location
                ON {schemaName}.low_stock_alerts(product_id, market_id, (COALESCE(department_id, -1)))
                WHERE status = 'Active';
            """, cancellationToken, transaction);

        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<int> CreateLowStockAlertsAsync(
        TenantScanTarget tenant,
        CancellationToken cancellationToken)
    {
        var schemaName = QuoteTenantSchemaName(tenant.SchemaName);

        await using var transaction = await BeginTransactionAsync(cancellationToken);

        await using (var refreshCommand = await CreateCommandAsync($"""
            UPDATE {schemaName}.low_stock_alerts alert
            SET quantity = i.quantity,
                min_stock_alert = p.min_stock_alert,
                last_detected_at = NOW()
            FROM {schemaName}.inventory i
            INNER JOIN {schemaName}.products p ON p.id = i.product_id
            WHERE alert.status = 'Active'
              AND alert.product_id = i.product_id
              AND alert.market_id = i.market_id
              AND COALESCE(alert.department_id, -1) = COALESCE(i.department_id, -1)
              AND p.is_active = TRUE
              AND i.quantity <= p.min_stock_alert;
            """, cancellationToken, transaction))
        {
            await refreshCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var resolveCommand = await CreateCommandAsync($"""
            UPDATE {schemaName}.low_stock_alerts alert
            SET status = 'Resolved',
                resolved_at = NOW(),
                last_detected_at = NOW()
            WHERE alert.status = 'Active'
              AND NOT EXISTS (
                  SELECT 1
                  FROM {schemaName}.inventory i
                  INNER JOIN {schemaName}.products p ON p.id = i.product_id
                  WHERE i.product_id = alert.product_id
                    AND i.market_id = alert.market_id
                    AND COALESCE(i.department_id, -1) = COALESCE(alert.department_id, -1)
                    AND p.is_active = TRUE
                    AND i.quantity <= p.min_stock_alert
              );
            """, cancellationToken, transaction))
        {
            await resolveCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var createCommand = await CreateCommandAsync($"""
            WITH inserted_alerts AS (
                INSERT INTO {schemaName}.low_stock_alerts (
                    inventory_id,
                    product_id,
                    market_id,
                    department_id,
                    quantity,
                    min_stock_alert)
                SELECT i.id,
                       i.product_id,
                       i.market_id,
                       i.department_id,
                       i.quantity,
                       p.min_stock_alert
                FROM {schemaName}.inventory i
                INNER JOIN {schemaName}.products p ON p.id = i.product_id
                WHERE p.is_active = TRUE
                  AND i.quantity <= p.min_stock_alert
                  AND NOT EXISTS (
                      SELECT 1
                      FROM {schemaName}.low_stock_alerts existing
                      WHERE existing.status = 'Active'
                        AND existing.product_id = i.product_id
                        AND existing.market_id = i.market_id
                        AND COALESCE(existing.department_id, -1) = COALESCE(i.department_id, -1)
                  )
                RETURNING id, product_id, market_id, department_id, quantity, min_stock_alert
            ),
            recipient_users AS (
                SELECT u.id AS user_id
                FROM public.users u
                INNER JOIN public.roles r ON r.id = u.role_id
                WHERE u.company_id = @company_id
                  AND u.is_active = TRUE
                  AND (
                      r.permissions ? 'inventory:read'
                      OR r.permissions ? 'stock:update'
                      OR r.permissions ? 'stock:adjust'
                  )
            ),
            inserted_notifications AS (
                INSERT INTO {schemaName}.notifications (user_id, type, title, body)
                SELECT ru.user_id,
                       'LowStock',
                       'Low stock alert',
                       CONCAT('Product ', ia.product_id, ' is at ', ia.quantity, ' units; minimum is ', ia.min_stock_alert, '.')
                FROM inserted_alerts ia
                CROSS JOIN recipient_users ru
                RETURNING id
            )
            SELECT COUNT(*)::int, (SELECT COUNT(*)::int FROM inserted_notifications)
            FROM inserted_alerts;
            """, cancellationToken, transaction);

        createCommand.Parameters.AddWithValue("company_id", tenant.CompanyId);

        var createdAlertCount = (int)(await createCommand.ExecuteScalarAsync(cancellationToken) ?? 0);

        await transaction.CommitAsync(cancellationToken);

        return createdAlertCount;
    }

    private async Task<NpgsqlCommand> CreateCommandAsync(
        string commandText,
        CancellationToken cancellationToken,
        NpgsqlTransaction? transaction = null)
    {
        var connection = (NpgsqlConnection)_dbContext.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        return new NpgsqlCommand(commandText, connection, transaction);
    }

    private async Task<NpgsqlTransaction> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)_dbContext.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        return await connection.BeginTransactionAsync(cancellationToken);
    }

    private static string QuoteTenantSchemaName(string schemaName)
    {
        if (!TenantSchemaNamePattern().IsMatch(schemaName))
        {
            throw new InvalidOperationException($"Tenant schema name '{schemaName}' is invalid.");
        }

        return "\"" + schemaName.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static bool IsMissingTenantObject(PostgresException exception)
    {
        return exception.SqlState is
            "3F000" or // undefined_schema
            "42P01" or // undefined_table
            "42703";   // undefined_column
    }

    private static bool IsUndefinedFunction(PostgresException exception)
    {
        return exception.SqlState == "42883";
    }

    [GeneratedRegex("^[a-zA-Z_][a-zA-Z0-9_]{0,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex TenantSchemaNamePattern();

    private sealed record TenantScanTarget(int CompanyId, string SchemaName);
}

public sealed class StockAlertJobOptions
{
    public bool Enabled { get; set; } = true;

    public bool RunOnStartup { get; set; } = true;

    public TimeSpan Interval { get; set; } = TimeSpan.FromDays(1);
}
