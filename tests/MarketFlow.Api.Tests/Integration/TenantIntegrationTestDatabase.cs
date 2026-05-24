using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using BCrypt.Net;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using NpgsqlTypes;

namespace MarketFlow.Api.Tests.Integration;

public sealed class TenantIntegrationTestDatabase : IAsyncDisposable
{
    private const int MaxPostgresIdentifierLength = 63;
    private const int SchemaSuffixLength = 16;
    private const int SchemaSeparatorLength = 1;

    private static readonly SemaphoreSlim RequiredRolesLock = new(1, 1);
    private static readonly ConcurrentDictionary<string, byte> RequiredRolesEnsuredConnectionStrings =
        new(StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, (string Description, string Permissions)> RequiredRoles =
        new Dictionary<string, (string Description, string Permissions)>
    {
        ["RootAdmin"] = ("Platform administrator", "{\"company\": true, \"companies:read\": true, \"companies:create\": true, \"companies:update\": true, \"companies:delete\": true, \"users:read\": true, \"users:create\": true, \"users:update\": true, \"users:delete\": true}"),
        ["CompanyAdmin"] = ("Company-level administrator", "{\"users:read\": true, \"users:create\": true, \"users:update\": true, \"users:delete\": true, \"markets:read\": true, \"markets:create\": true, \"markets:update\": true, \"markets:delete\": true, \"departments:read\": true, \"departments:create\": true, \"departments:update\": true, \"departments:delete\": true, \"products:read\": true, \"products:create\": true, \"products:update\": true, \"products:delete\": true, \"sales:read\": true, \"sales:create\": true, \"sales:update\": true, \"sales:delete\": true, \"inventory:create\": true, \"inventory:read\": true, \"stock:update\": true, \"stock:adjust\": true, \"inventory:delete\": true, \"inventory-movements:read\": true, \"stock:transfer\": true, \"purchases:read\": true, \"purchases:create\": true, \"purchases:update\": true, \"purchases:delete\": true}"),
        ["MainOperator"] = ("Market-level manager", "{\"markets:read\": true, \"departments:read\": true, \"products:read\": true, \"products:create\": true, \"products:update\": true, \"products:delete\": true, \"sales:read\": true, \"sales:create\": true, \"sales:update\": true, \"sales:delete\": true, \"inventory:create\": true, \"inventory:read\": true, \"stock:update\": true, \"stock:adjust\": true, \"inventory:delete\": true, \"inventory-movements:read\": true, \"stock:transfer\": true, \"purchases:read\": true, \"purchases:create\": true, \"purchases:update\": true, \"purchases:delete\": true}"),
        ["DepartmentManager"] = ("Department-level manager", "{\"markets:read\": true, \"departments:read\": true, \"products:read\": true, \"sales:read\": true, \"inventory:read\": true, \"stock:update\": true, \"stock:adjust\": true, \"inventory-movements:read\": true, \"stock:transfer\": true}"),
        ["InventoryEmployee"] = ("Stock and inventory employee", "{\"markets:read\": true, \"departments:read\": true, \"products:read\": true, \"inventory:read\": true, \"stock:update\": true, \"stock:adjust\": true, \"inventory-movements:read\": true}"),
        ["Seller"] = ("Creates sales and handles POS operations", "{\"markets:read\": true, \"departments:read\": true, \"products:read\": true, \"sales:create\": true, \"inventory:read\": true}")
    };

    private readonly TenantIntegrationTestOptions _options;
    private readonly List<int> _createdUserIds = [];
    private readonly List<int> _createdCompanyIds = [];
    private readonly HashSet<string> _createdSchemaNames = new(StringComparer.Ordinal);

    public TenantIntegrationTestDatabase(TenantIntegrationTestOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new ArgumentException("A PostgreSQL connection string is required.", nameof(options));
        }

        _options = options;
    }

    public static bool HasConfiguredConnectionString =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(
            TenantIntegrationTestOptions.DefaultConnectionStringEnvironmentVariable));

    public static string MissingConnectionStringSkipReason =>
        $"{TenantIntegrationTestOptions.DefaultConnectionStringEnvironmentVariable} is not configured.";

    public string CreateUniqueSchemaName(string prefix = "mf_test")
    {
        var sanitizedPrefix = new string(prefix
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) || character == '_' ? character : '_')
            .ToArray())
            .Trim('_');

        if (string.IsNullOrWhiteSpace(sanitizedPrefix) || !char.IsLetter(sanitizedPrefix[0]))
        {
            sanitizedPrefix = "mf_test";
        }

        var maxPrefixLength = MaxPostgresIdentifierLength - SchemaSeparatorLength - SchemaSuffixLength;

        if (sanitizedPrefix.Length > maxPrefixLength)
        {
            sanitizedPrefix = sanitizedPrefix[..maxPrefixLength].TrimEnd('_');
        }

        if (string.IsNullOrWhiteSpace(sanitizedPrefix))
        {
            sanitizedPrefix = "mf_test";
        }

        var suffix = Guid.NewGuid().ToString("N")[..SchemaSuffixLength];

        return $"{sanitizedPrefix}_{suffix}";
    }

    public async Task EnsureRequiredRolesAsync(CancellationToken cancellationToken = default)
    {
        if (RequiredRolesEnsuredConnectionStrings.ContainsKey(_options.ConnectionString))
        {
            return;
        }

        await RequiredRolesLock.WaitAsync(cancellationToken);

        try
        {
            if (RequiredRolesEnsuredConnectionStrings.ContainsKey(_options.ConnectionString))
            {
                return;
            }

            await using var connection = await OpenConnectionAsync(cancellationToken);

            foreach (var role in RequiredRoles)
            {
                await ExecuteAsync(
                    connection,
                    """
                    INSERT INTO public.roles (name, description, permissions)
                    VALUES (@name, @description, @permissions::jsonb)
                    ON CONFLICT (name) DO UPDATE
                    SET description = EXCLUDED.description,
                        permissions = EXCLUDED.permissions;
                    """,
                    cancellationToken,
                    new NpgsqlParameter("name", role.Key),
                    new NpgsqlParameter("description", role.Value.Description),
                    new NpgsqlParameter("permissions", role.Value.Permissions));
            }

            RequiredRolesEnsuredConnectionStrings.TryAdd(_options.ConnectionString, 0);
        }
        finally
        {
            RequiredRolesLock.Release();
        }
    }

    public async Task<TenantTestCompany> CreateCompanyAsync(
        string? schemaName = null,
        string? name = null,
        bool isActive = true,
        bool dropSchemaOnDispose = false,
        CancellationToken cancellationToken = default)
    {
        var generatedSchemaName = string.IsNullOrWhiteSpace(schemaName);
        schemaName ??= CreateUniqueSchemaName();
        name ??= $"Tenant Test {schemaName}";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        var companyId = await ExecuteScalarAsync<int>(
            connection,
            """
            INSERT INTO public.companies (
                name,
                schema_name,
                company_type,
                subscription_plan,
                max_markets,
                max_users,
                is_active
            )
            VALUES (
                @name,
                @schema_name,
                'SMALL',
                'BASIC',
                5,
                50,
                @is_active
            )
            RETURNING id;
            """,
            cancellationToken,
            new NpgsqlParameter("name", name),
            new NpgsqlParameter("schema_name", schemaName),
            new NpgsqlParameter("is_active", isActive));

        _createdCompanyIds.Add(companyId);

        await EnsureInventoryMovementsTableAsync(connection, schemaName, cancellationToken);
        await EnsureLowStockAlertsTableAsync(connection, schemaName, cancellationToken);
        await EnsureSaleReferenceNumbersAsync(connection, schemaName, cancellationToken);
        await EnsureSalesStatusAsync(connection, schemaName, cancellationToken);

        if (generatedSchemaName || dropSchemaOnDispose)
        {
            _createdSchemaNames.Add(schemaName);
        }

        return new TenantTestCompany(companyId, name, schemaName);
    }

    public void TrackCompanyForCleanup(
        int companyId,
        string schemaName,
        bool dropSchemaOnDispose = true)
    {
        _createdCompanyIds.Add(companyId);

        if (dropSchemaOnDispose)
        {
            _createdSchemaNames.Add(schemaName);
        }
    }

    public async Task<TenantTestUser> CreateUserAsync(
        TenantTestCompany company,
        string roleName = "CompanyAdmin",
        bool isActive = true,
        string? email = null,
        string? fullName = null,
        string password = "User12345",
        CancellationToken cancellationToken = default)
    {
        await EnsureRequiredRolesAsync(cancellationToken);

        email ??= $"user-{Guid.NewGuid():N}@marketflow.test";
        fullName ??= $"{roleName} Test User";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        var roleId = await ExecuteScalarAsync<int>(
            connection,
            "SELECT id FROM public.roles WHERE name = @role_name;",
            cancellationToken,
            new NpgsqlParameter("role_name", roleName));

        var userId = await ExecuteScalarAsync<int>(
            connection,
            """
            INSERT INTO public.users (
                full_name,
                email,
                password_hash,
                company_id,
                role_id,
                is_active
            )
            VALUES (
                @full_name,
                @email,
                @password_hash,
                @company_id,
                @role_id,
                @is_active
            )
            RETURNING id;
            """,
            cancellationToken,
            new NpgsqlParameter("full_name", fullName),
            new NpgsqlParameter("email", email.Trim().ToLowerInvariant()),
            new NpgsqlParameter("password_hash", BCrypt.Net.BCrypt.HashPassword(password)),
            new NpgsqlParameter("company_id", company.Id),
            new NpgsqlParameter("role_id", roleId),
            new NpgsqlParameter("is_active", isActive));

        _createdUserIds.Add(userId);

        return new TenantTestUser(
            userId,
            company.Id,
            company.SchemaName,
            fullName,
            email.Trim().ToLowerInvariant(),
            roleName,
            isActive);
    }

    public async Task<TenantTestCategory> InsertCategoryAsync(
        string schemaName,
        string? name = null,
        CancellationToken cancellationToken = default)
    {
        name ??= $"Category {Guid.NewGuid():N}"[..24];

        await using var connection = await OpenConnectionAsync(cancellationToken);
        var categoryId = await ExecuteScalarAsync<int>(
            connection,
            $"""
            INSERT INTO {QuoteIdentifier(schemaName)}.categories (name, description, is_active)
            VALUES (@name, @description, TRUE)
            RETURNING id;
            """,
            cancellationToken,
            new NpgsqlParameter("name", name),
            new NpgsqlParameter("description", $"{name} integration test category"));

        return new TenantTestCategory(categoryId, name);
    }

    public async Task<TenantTestMarket> InsertMarketAsync(
        string schemaName,
        string? name = null,
        string? city = "Test City",
        string? address = null,
        bool isActive = true,
        CancellationToken cancellationToken = default)
    {
        name ??= $"Market {Guid.NewGuid():N}"[..22];

        await using var connection = await OpenConnectionAsync(cancellationToken);
        var marketId = await ExecuteScalarAsync<int>(
            connection,
            $"""
            INSERT INTO {QuoteIdentifier(schemaName)}.markets (name, city, address, is_active)
            VALUES (@name, @city, @address, @is_active)
            RETURNING id;
            """,
            cancellationToken,
            new NpgsqlParameter("name", name),
            new NpgsqlParameter("city", NpgsqlDbType.Varchar)
            {
                Value = city is null ? DBNull.Value : city
            },
            new NpgsqlParameter("address", NpgsqlDbType.Text)
            {
                Value = address is null ? DBNull.Value : address
            },
            new NpgsqlParameter("is_active", isActive));

        return new TenantTestMarket(marketId, name);
    }

    public async Task<int> CountMarketsByNameAsync(
        string schemaName,
        string name,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

        return await ExecuteScalarAsync<int>(
            connection,
            $"""
            SELECT COUNT(*)::int
            FROM {QuoteIdentifier(schemaName)}.markets
            WHERE lower(name) = lower(@name);
            """,
            cancellationToken,
            new NpgsqlParameter("name", name));
    }

    public async Task<TenantTestMarketDetails?> GetMarketDetailsAsync(
        string schemaName,
        int marketId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            $"""
            SELECT id,
                   name,
                   city,
                   address,
                   is_active
            FROM {QuoteIdentifier(schemaName)}.markets
            WHERE id = @market_id;
            """,
            connection);
        command.Parameters.Add(new NpgsqlParameter("market_id", NpgsqlDbType.Integer)
        {
            Value = marketId
        });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken)
            ? new TenantTestMarketDetails(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetBoolean(4))
            : null;
    }

    public async Task<TenantTestDepartment> InsertDepartmentAsync(
        string schemaName,
        int marketId,
        string? name = null,
        string? description = null,
        bool isActive = true,
        CancellationToken cancellationToken = default)
    {
        name ??= $"Department {Guid.NewGuid():N}"[..26];

        await using var connection = await OpenConnectionAsync(cancellationToken);
        var departmentId = await ExecuteScalarAsync<int>(
            connection,
            $"""
            INSERT INTO {QuoteIdentifier(schemaName)}.departments (market_id, name, description, is_active)
            VALUES (@market_id, @name, @description, @is_active)
            RETURNING id;
            """,
            cancellationToken,
            new NpgsqlParameter("market_id", marketId),
            new NpgsqlParameter("name", name),
            new NpgsqlParameter("description", NpgsqlDbType.Text)
            {
                Value = description is null ? DBNull.Value : description
            },
            new NpgsqlParameter("is_active", isActive));

        return new TenantTestDepartment(departmentId, marketId, name, description, isActive);
    }

    public async Task<int> CountDepartmentsByNameAsync(
        string schemaName,
        int marketId,
        string name,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

        return await ExecuteScalarAsync<int>(
            connection,
            $"""
            SELECT COUNT(*)::int
            FROM {QuoteIdentifier(schemaName)}.departments
            WHERE market_id = @market_id
              AND lower(name) = lower(@name);
            """,
            cancellationToken,
            new NpgsqlParameter("market_id", marketId),
            new NpgsqlParameter("name", name));
    }

    public async Task<TenantTestDepartment?> GetDepartmentDetailsAsync(
        string schemaName,
        int departmentId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            $"""
            SELECT id,
                   market_id,
                   name,
                   description,
                   is_active
            FROM {QuoteIdentifier(schemaName)}.departments
            WHERE id = @department_id;
            """,
            connection);
        command.Parameters.Add(new NpgsqlParameter("department_id", NpgsqlDbType.Integer)
        {
            Value = departmentId
        });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken)
            ? new TenantTestDepartment(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetBoolean(4))
            : null;
    }

    public async Task<TenantTestSupplier> InsertSupplierAsync(
        string schemaName,
        string? name = null,
        CancellationToken cancellationToken = default)
    {
        name ??= $"Supplier {Guid.NewGuid():N}"[..24];

        await using var connection = await OpenConnectionAsync(cancellationToken);
        var supplierId = await ExecuteScalarAsync<int>(
            connection,
            $"""
            INSERT INTO {QuoteIdentifier(schemaName)}.suppliers (name, is_active)
            VALUES (@name, TRUE)
            RETURNING id;
            """,
            cancellationToken,
            new NpgsqlParameter("name", name));

        return new TenantTestSupplier(supplierId, name);
    }

    public async Task<int> InsertSaleWithReferenceNumberAsync(
        string schemaName,
        int marketId,
        int createdByUserId,
        string referenceNumber,
        int? departmentId = null,
        DateOnly? saleDate = null,
        string status = "Paid",
        string paymentMethod = "Cash",
        decimal totalAmount = 0,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

        return await ExecuteScalarAsync<int>(
            connection,
            $"""
            INSERT INTO {QuoteIdentifier(schemaName)}.sales (
                market_id,
                department_id,
                created_by_user_id,
                sale_date,
                status,
                payment_method,
                total_amount,
                reference_number)
            VALUES (
                @market_id,
                @department_id,
                @created_by_user_id,
                @sale_date,
                @status,
                @payment_method,
                @total_amount,
                @reference_number)
            RETURNING id;
            """,
            cancellationToken,
            new NpgsqlParameter("market_id", marketId),
            new NpgsqlParameter("department_id", departmentId is null ? DBNull.Value : departmentId),
            new NpgsqlParameter("created_by_user_id", createdByUserId),
            new NpgsqlParameter("sale_date", saleDate ?? DateOnly.FromDateTime(DateTime.UtcNow)),
            new NpgsqlParameter("status", status),
            new NpgsqlParameter("payment_method", paymentMethod),
            new NpgsqlParameter("total_amount", totalAmount),
            new NpgsqlParameter("reference_number", referenceNumber));
    }

    public async Task RemoveSaleReferenceNumbersAsync(
        string schemaName,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

        await ExecuteAsync(
            connection,
            $"""
            DROP TRIGGER IF EXISTS trg_sales_assign_reference_number ON {QuoteIdentifier(schemaName)}.sales;
            DROP INDEX IF EXISTS {QuoteIdentifier(schemaName)}.ux_sales_reference_number;
            ALTER TABLE {QuoteIdentifier(schemaName)}.sales DROP COLUMN IF EXISTS reference_number;
            """,
            cancellationToken);
    }

    public async Task RemoveSaleReferenceTriggerAsync(
        string schemaName,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

        await ExecuteAsync(
            connection,
            $"""
            DROP TRIGGER IF EXISTS trg_sales_assign_reference_number ON {QuoteIdentifier(schemaName)}.sales;
            DROP INDEX IF EXISTS {QuoteIdentifier(schemaName)}.ux_sales_reference_number;
            """,
            cancellationToken);
    }

    public async Task MakeSaleReferenceNumberNullableAndNullAsync(
        string schemaName,
        int saleId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

        await ExecuteAsync(
            connection,
            $"""
            ALTER TABLE {QuoteIdentifier(schemaName)}.sales
                ALTER COLUMN reference_number DROP NOT NULL;
            UPDATE {QuoteIdentifier(schemaName)}.sales
            SET reference_number = NULL
            WHERE id = @sale_id;
            """,
            cancellationToken,
            new NpgsqlParameter("sale_id", saleId));
    }

    public async Task<TenantSaleReferenceSchemaState> GetSaleReferenceSchemaStateAsync(
        string schemaName,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
                       SELECT 1
                       FROM information_schema.columns
                       WHERE table_schema = @schema_name
                         AND table_name = 'sales'
                         AND column_name = 'reference_number'
                   ) AS column_exists,
                   EXISTS (
                       SELECT 1
                       FROM information_schema.columns
                       WHERE table_schema = @schema_name
                         AND table_name = 'sales'
                         AND column_name = 'reference_number'
                         AND is_nullable = 'YES'
                   ) AS column_is_nullable,
                   EXISTS (
                       SELECT 1
                       FROM pg_catalog.pg_trigger trigger
                       INNER JOIN pg_catalog.pg_class relation ON relation.oid = trigger.tgrelid
                       INNER JOIN pg_catalog.pg_namespace namespace ON namespace.oid = relation.relnamespace
                       WHERE namespace.nspname = @schema_name
                         AND relation.relname = 'sales'
                         AND trigger.tgname = 'trg_sales_assign_reference_number'
                   ) AS trigger_exists,
                   to_regclass(format('%I.ux_sales_reference_number', @schema_name)) IS NOT NULL AS unique_index_exists,
                   to_regprocedure('public.assign_sale_reference_number()') IS NOT NULL AS trigger_function_exists;
            """,
            connection);
        command.Parameters.AddWithValue("schema_name", schemaName);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken)
            ? new TenantSaleReferenceSchemaState(
                reader.GetBoolean(0),
                reader.GetBoolean(1),
                reader.GetBoolean(2),
                reader.GetBoolean(3),
                reader.GetBoolean(4),
                await HasSaleReferenceNumbersNeedingBackfillAsync(schemaName, reader.GetBoolean(0), cancellationToken))
            : throw new InvalidOperationException("Sale reference schema state was not returned.");
    }

    private async Task<bool> HasSaleReferenceNumbersNeedingBackfillAsync(
        string schemaName,
        bool referenceNumberColumnExists,
        CancellationToken cancellationToken)
    {
        if (!referenceNumberColumnExists)
        {
            return false;
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        return await ExecuteScalarAsync<bool>(
            connection,
            $"""
            SELECT EXISTS (
                SELECT 1
                FROM {QuoteIdentifier(schemaName)}.sales
                WHERE reference_number IS NULL OR btrim(reference_number) = ''
            );
            """,
            cancellationToken);
    }

    public async Task<TenantTestStaffAssignment> InsertStaffAssignmentAsync(
        string schemaName,
        int userId,
        int marketId,
        int? departmentId = null,
        bool isActive = true,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var assignmentId = await ExecuteScalarAsync<int>(
            connection,
            $"""
            INSERT INTO {QuoteIdentifier(schemaName)}.staff_assignments (
                user_id,
                market_id,
                department_id,
                is_active)
            VALUES (
                @user_id,
                @market_id,
                @department_id,
                @is_active)
            RETURNING id;
            """,
            cancellationToken,
            new NpgsqlParameter("user_id", userId),
            new NpgsqlParameter("market_id", marketId),
            new NpgsqlParameter("department_id", departmentId is null ? DBNull.Value : departmentId),
            new NpgsqlParameter("is_active", isActive));

        return new TenantTestStaffAssignment(assignmentId, userId, marketId, departmentId, isActive);
    }

    public async Task<TenantTestProduct> InsertProductAsync(
        string schemaName,
        int? categoryId = null,
        string? name = null,
        string? barcode = null,
        decimal unitPrice = 1.25m,
        int minStockAlert = 0,
        bool isActive = true,
        CancellationToken cancellationToken = default)
    {
        if (!categoryId.HasValue)
        {
            categoryId = (await InsertCategoryAsync(schemaName, cancellationToken: cancellationToken)).Id;
        }

        name ??= $"Product {Guid.NewGuid():N}"[..23];
        barcode ??= Guid.NewGuid().ToString("N")[..18];

        await using var connection = await OpenConnectionAsync(cancellationToken);
        var productId = await ExecuteScalarAsync<int>(
            connection,
            $"""
            INSERT INTO {QuoteIdentifier(schemaName)}.products (
                name,
                barcode,
                category_id,
                unit_price,
                cost_price,
                tax_rate,
                min_stock_alert,
                is_active
            )
            VALUES (
                @name,
                @barcode,
                @category_id,
                @unit_price,
                0,
                0,
                @min_stock_alert,
                @is_active
            )
            RETURNING id;
            """,
            cancellationToken,
            new NpgsqlParameter("name", name),
            new NpgsqlParameter("barcode", barcode),
            new NpgsqlParameter("category_id", categoryId.Value),
            new NpgsqlParameter("unit_price", unitPrice),
            new NpgsqlParameter("min_stock_alert", minStockAlert),
            new NpgsqlParameter("is_active", isActive));

        return new TenantTestProduct(productId, name, barcode, categoryId.Value);
    }

    public async Task<TenantTestInventoryItem> InsertInventoryAsync(
        string schemaName,
        int productId,
        int marketId,
        int? departmentId = null,
        int quantity = 10,
        int reservedQuantity = 0,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var inventoryId = await ExecuteScalarAsync<int>(
            connection,
            $"""
            INSERT INTO {QuoteIdentifier(schemaName)}.inventory (
                product_id,
                market_id,
                department_id,
                quantity,
                reserved_quantity)
            VALUES (
                @product_id,
                @market_id,
                @department_id,
                @quantity,
                @reserved_quantity)
            RETURNING id;
            """,
            cancellationToken,
            new NpgsqlParameter("product_id", productId),
            new NpgsqlParameter("market_id", marketId),
            new NpgsqlParameter("department_id", departmentId is null ? DBNull.Value : departmentId),
            new NpgsqlParameter("quantity", quantity),
            new NpgsqlParameter("reserved_quantity", reservedQuantity));

        return new TenantTestInventoryItem(
            inventoryId,
            productId,
            marketId,
            departmentId,
            quantity,
            reservedQuantity);
    }

    public async Task<TenantTestInventoryItem?> GetInventoryDetailsAsync(
        string schemaName,
        int inventoryId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            $"""
            SELECT id,
                   product_id,
                   market_id,
                   department_id,
                   quantity,
                   reserved_quantity
            FROM {QuoteIdentifier(schemaName)}.inventory
            WHERE id = @id;
            """,
            connection);
        command.Parameters.AddWithValue("id", inventoryId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken)
            ? new TenantTestInventoryItem(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetInt32(5))
            : null;
    }

    public async Task<bool> DeleteInventoryAsync(
        string schemaName,
        int inventoryId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

        var affected = await ExecuteScalarAsync<int>(
            connection,
            $"""
            WITH deleted AS (
                DELETE FROM {QuoteIdentifier(schemaName)}.inventory
                WHERE id = @inventory_id
                RETURNING id
            )
            SELECT COUNT(*)::int
            FROM deleted;
            """,
            cancellationToken,
            new NpgsqlParameter("inventory_id", inventoryId));

        return affected > 0;
    }

    public async Task<TenantTestInventoryMovement> InsertInventoryMovementAsync(
        string schemaName,
        int inventoryId,
        string movementType = "Adjustment",
        int quantityChanged = 1,
        string? referenceNumber = null,
        int? createdByUserId = null,
        CancellationToken cancellationToken = default)
    {
        referenceNumber ??= $"movement-{Guid.NewGuid():N}"[..30];

        await using var connection = await OpenConnectionAsync(cancellationToken);
        var movementId = await ExecuteScalarAsync<int>(
            connection,
            $"""
            INSERT INTO {QuoteIdentifier(schemaName)}.inventory_movements (
                inventory_id,
                movement_type,
                quantity_changed,
                reference_number,
                created_by_user_id)
            VALUES (
                @inventory_id,
                @movement_type,
                @quantity_changed,
                @reference_number,
                @created_by_user_id)
            RETURNING id;
            """,
            cancellationToken,
            new NpgsqlParameter("inventory_id", inventoryId),
            new NpgsqlParameter("movement_type", movementType),
            new NpgsqlParameter("quantity_changed", quantityChanged),
            new NpgsqlParameter("reference_number", referenceNumber),
            new NpgsqlParameter("created_by_user_id", createdByUserId is null ? DBNull.Value : createdByUserId));

        return new TenantTestInventoryMovement(
            movementId,
            inventoryId,
            movementType,
            quantityChanged,
            referenceNumber,
            createdByUserId);
    }

    public async Task<TenantTestPurchase> InsertPurchaseAsync(
        string schemaName,
        int supplierId,
        int marketId,
        int createdByUserId,
        string status = "Pending",
        decimal totalAmount = 0,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var purchaseId = await ExecuteScalarAsync<int>(
            connection,
            $"""
            INSERT INTO {QuoteIdentifier(schemaName)}.purchases (
                supplier_id,
                market_id,
                created_by_user_id,
                status,
                total_amount)
            VALUES (
                @supplier_id,
                @market_id,
                @created_by_user_id,
                @status,
                @total_amount)
            RETURNING id;
            """,
            cancellationToken,
            new NpgsqlParameter("supplier_id", supplierId),
            new NpgsqlParameter("market_id", marketId),
            new NpgsqlParameter("created_by_user_id", createdByUserId),
            new NpgsqlParameter("status", status),
            new NpgsqlParameter("total_amount", totalAmount));

        return new TenantTestPurchase(purchaseId, supplierId, marketId, status);
    }

    public async Task InsertPurchaseItemAsync(
        string schemaName,
        int purchaseId,
        int productId,
        int quantity,
        decimal unitCost,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await ExecuteAsync(
            connection,
            $"""
            INSERT INTO {QuoteIdentifier(schemaName)}.purchase_items (
                purchase_id,
                product_id,
                quantity,
                unit_cost)
            VALUES (
                @purchase_id,
                @product_id,
                @quantity,
                @unit_cost);
            """,
            cancellationToken,
            new NpgsqlParameter("purchase_id", purchaseId),
            new NpgsqlParameter("product_id", productId),
            new NpgsqlParameter("quantity", quantity),
            new NpgsqlParameter("unit_cost", unitCost));
    }

    public async Task<bool> SchemaExistsAsync(
        string schemaName,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

        return await ExecuteScalarAsync<bool>(
            connection,
            """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.schemata
                WHERE schema_name = @schema_name
            );
            """,
            cancellationToken,
            new NpgsqlParameter("schema_name", schemaName));
    }

    public async Task<bool> TableExistsAsync(
        string schemaName,
        string tableName,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

        return await ExecuteScalarAsync<bool>(
            connection,
            """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.tables
                WHERE table_schema = @schema_name
                  AND table_name = @table_name
            );
            """,
            cancellationToken,
            new NpgsqlParameter("schema_name", schemaName),
            new NpgsqlParameter("table_name", tableName));
    }

    public async Task<int> CountRowsAsync(
        string schemaName,
        string tableName,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

        return await ExecuteScalarAsync<int>(
            connection,
            $"SELECT COUNT(*)::int FROM {QuoteIdentifier(schemaName)}.{QuoteIdentifier(tableName)};",
            cancellationToken);
    }

    public async Task<int> CountProductsByBarcodeAsync(
        string schemaName,
        string barcode,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

        return await ExecuteScalarAsync<int>(
            connection,
            $"""
            SELECT COUNT(*)::int
            FROM {QuoteIdentifier(schemaName)}.products
            WHERE barcode = @barcode;
            """,
            cancellationToken,
            new NpgsqlParameter("barcode", barcode));
    }

    public async Task<TenantTestProductDetails?> GetProductDetailsAsync(
        string schemaName,
        int productId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            $"""
            SELECT id,
                   name,
                   barcode,
                   category_id,
                   unit_price,
                   cost_price,
                   tax_rate,
                   min_stock_alert,
                   is_active
            FROM {QuoteIdentifier(schemaName)}.products
            WHERE id = @product_id;
            """,
            connection);
        command.Parameters.Add(new NpgsqlParameter("product_id", NpgsqlDbType.Integer)
        {
            Value = productId
        });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken)
            ? new TenantTestProductDetails(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetDecimal(4),
                reader.GetDecimal(5),
                reader.GetDecimal(6),
                reader.GetInt32(7),
                reader.GetBoolean(8))
            : null;
    }

    public async Task<int> CountCompaniesBySchemaNameAsync(
        string schemaName,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

        return await ExecuteScalarAsync<int>(
            connection,
            """
            SELECT COUNT(*)::int
            FROM public.companies
            WHERE schema_name = @schema_name;
            """,
            cancellationToken,
            new NpgsqlParameter("schema_name", schemaName));
    }

    public async Task<int> CountUsersByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

        return await ExecuteScalarAsync<int>(
            connection,
            """
            SELECT COUNT(*)::int
            FROM public.users
            WHERE email = @email;
            """,
            cancellationToken,
            new NpgsqlParameter("email", email.Trim().ToLowerInvariant()));
    }

    public string GenerateAccessToken(TenantTestUser user)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Role, user.RoleName),
            new("company_id", user.CompanyId.ToString()),
            new("schema_name", user.SchemaName)
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.JwtSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.JwtIssuer,
            audience: _options.JwtAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_options.AccessTokenMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public async ValueTask DisposeAsync()
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            return;
        }

        await using var connection = await OpenConnectionAsync(CancellationToken.None);

        foreach (var schemaName in _createdSchemaNames)
        {
            await ExecuteAsync(
                connection,
                $"DROP SCHEMA IF EXISTS {QuoteIdentifier(schemaName)} CASCADE;",
                CancellationToken.None);
        }

        if (_createdUserIds.Count > 0)
        {
            await ExecuteAsync(
                connection,
                "DELETE FROM public.users WHERE id = ANY (@user_ids);",
                CancellationToken.None,
                new NpgsqlParameter<int[]>("user_ids", _createdUserIds.Distinct().ToArray()));
        }

        if (_createdCompanyIds.Count > 0)
        {
            await ExecuteAsync(
                connection,
                "DELETE FROM public.companies WHERE id = ANY (@company_ids);",
                CancellationToken.None,
                new NpgsqlParameter<int[]>("company_ids", _createdCompanyIds.Distinct().ToArray()));
        }
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        return connection;
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection connection,
        string commandText,
        CancellationToken cancellationToken,
        params NpgsqlParameter[] parameters)
    {
        await using var command = new NpgsqlCommand(commandText, connection);
        command.Parameters.AddRange(parameters);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureInventoryMovementsTableAsync(
        NpgsqlConnection connection,
        string schemaName,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(
            connection,
            $"""
            CREATE TABLE IF NOT EXISTS {QuoteIdentifier(schemaName)}.inventory_movements (
                id                  SERIAL PRIMARY KEY,
                inventory_id        INT         NOT NULL REFERENCES {QuoteIdentifier(schemaName)}.inventory(id) ON DELETE CASCADE,
                movement_type       VARCHAR(30) NOT NULL,
                quantity_changed    INT         NOT NULL,
                reference_number    VARCHAR(100),
                reason              VARCHAR(100),
                note                TEXT,
                created_by_user_id  INT,
                created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            CREATE INDEX IF NOT EXISTS idx_inventory_movements_inventory ON {QuoteIdentifier(schemaName)}.inventory_movements(inventory_id);
            CREATE INDEX IF NOT EXISTS idx_inventory_movements_created ON {QuoteIdentifier(schemaName)}.inventory_movements(created_at);
            """,
            cancellationToken);
    }

    private static async Task EnsureSalesStatusAsync(
        NpgsqlConnection connection,
        string schemaName,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(
            connection,
            $"""
            ALTER TABLE {QuoteIdentifier(schemaName)}.sales
                ADD COLUMN IF NOT EXISTS status VARCHAR(20) NOT NULL DEFAULT 'Paid';

            ALTER TABLE {QuoteIdentifier(schemaName)}.sales
                DROP CONSTRAINT IF EXISTS sales_status_check;

            ALTER TABLE {QuoteIdentifier(schemaName)}.sales
                ADD CONSTRAINT sales_status_check
                CHECK (status IN ('Draft', 'Pending', 'Paid', 'Cancelled'));

            CREATE INDEX IF NOT EXISTS idx_sales_status
                ON {QuoteIdentifier(schemaName)}.sales(status);
            """,
            cancellationToken);
    }

    private static async Task EnsureLowStockAlertsTableAsync(
        NpgsqlConnection connection,
        string schemaName,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(
            connection,
            $"""
            CREATE TABLE IF NOT EXISTS {QuoteIdentifier(schemaName)}.low_stock_alerts (
                id                BIGSERIAL PRIMARY KEY,
                inventory_id      INT         NOT NULL REFERENCES {QuoteIdentifier(schemaName)}.inventory(id) ON DELETE CASCADE,
                product_id        INT         NOT NULL REFERENCES {QuoteIdentifier(schemaName)}.products(id) ON DELETE CASCADE,
                market_id         INT         NOT NULL REFERENCES {QuoteIdentifier(schemaName)}.markets(id) ON DELETE CASCADE,
                department_id     INT         REFERENCES {QuoteIdentifier(schemaName)}.departments(id) ON DELETE SET NULL,
                quantity          INT         NOT NULL,
                min_stock_alert   INT         NOT NULL,
                status            VARCHAR(20) NOT NULL DEFAULT 'Active'
                    CHECK (status IN ('Active', 'Resolved')),
                first_detected_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                last_detected_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                resolved_at       TIMESTAMPTZ
            );
            CREATE INDEX IF NOT EXISTS idx_low_stock_alerts_inventory ON {QuoteIdentifier(schemaName)}.low_stock_alerts(inventory_id);
            CREATE INDEX IF NOT EXISTS idx_low_stock_alerts_status ON {QuoteIdentifier(schemaName)}.low_stock_alerts(status);
            CREATE UNIQUE INDEX IF NOT EXISTS ux_low_stock_alerts_active_product_location
                ON {QuoteIdentifier(schemaName)}.low_stock_alerts(product_id, market_id, (COALESCE(department_id, -1)))
                WHERE status = 'Active';
            """,
            cancellationToken);
    }

    private static async Task EnsureSaleReferenceNumbersAsync(
        NpgsqlConnection connection,
        string schemaName,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(
            connection,
            $"""
            CREATE OR REPLACE FUNCTION public.assign_sale_reference_number()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $$
            BEGIN
                IF NEW.reference_number IS NULL OR btrim(NEW.reference_number) = '' THEN
                    NEW.reference_number := 'SALE-' || lpad(NEW.id::text, 6, '0');
                END IF;

                RETURN NEW;
            END;
            $$;

            ALTER TABLE {QuoteIdentifier(schemaName)}.sales
                ADD COLUMN IF NOT EXISTS department_id INT;
            CREATE INDEX IF NOT EXISTS idx_sales_department
                ON {QuoteIdentifier(schemaName)}.sales(department_id);
            ALTER TABLE {QuoteIdentifier(schemaName)}.sales
                ADD COLUMN IF NOT EXISTS reference_number VARCHAR(50);
            UPDATE {QuoteIdentifier(schemaName)}.sales
            SET reference_number = 'SALE-' || lpad(id::text, 6, '0')
            WHERE reference_number IS NULL OR btrim(reference_number) = '';
            ALTER TABLE {QuoteIdentifier(schemaName)}.sales
                ALTER COLUMN reference_number SET NOT NULL;
            CREATE UNIQUE INDEX IF NOT EXISTS ux_sales_reference_number
                ON {QuoteIdentifier(schemaName)}.sales(reference_number);
            DROP TRIGGER IF EXISTS trg_sales_assign_reference_number ON {QuoteIdentifier(schemaName)}.sales;
            CREATE TRIGGER trg_sales_assign_reference_number
                BEFORE INSERT ON {QuoteIdentifier(schemaName)}.sales
                FOR EACH ROW
                EXECUTE FUNCTION public.assign_sale_reference_number();
            """,
            cancellationToken);
    }

    private static async Task<T> ExecuteScalarAsync<T>(
        NpgsqlConnection connection,
        string commandText,
        CancellationToken cancellationToken,
        params NpgsqlParameter[] parameters)
    {
        await using var command = new NpgsqlCommand(commandText, connection);
        command.Parameters.AddRange(parameters);

        var result = await command.ExecuteScalarAsync(cancellationToken);

        return result is T typedResult
            ? typedResult
            : throw new InvalidOperationException(
                $"Expected scalar result of type {typeof(T).Name}, but got {result?.GetType().Name ?? "null"}.");
    }

    private static string QuoteIdentifier(string identifier)
    {
        return "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
