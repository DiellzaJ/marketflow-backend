using MarketFlow.Api.Tests.Integration;
using MarketFlow.Infrastructure.BackgroundJobs;
using MarketFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace MarketFlow.Api.Tests.BackgroundJobs;

public sealed class StockAlertJobTests
{
    [PostgresIntegrationFact]
    public async Task ExecuteAsync_CreatesAlertAndNotificationOnceForLowStockInventory()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();
        await using var database = new TenantIntegrationTestDatabase(options);
        var company = await database.CreateCompanyAsync();
        var user = await database.CreateUserAsync(company, roleName: "CompanyAdmin");
        var market = await database.InsertMarketAsync(company.SchemaName);
        var lowProduct = await database.InsertProductAsync(
            company.SchemaName,
            minStockAlert: 5);
        var stockedProduct = await database.InsertProductAsync(
            company.SchemaName,
            minStockAlert: 5);
        await database.InsertInventoryAsync(
            company.SchemaName,
            lowProduct.Id,
            market.Id,
            quantity: 5);
        await database.InsertInventoryAsync(
            company.SchemaName,
            stockedProduct.Id,
            market.Id,
            quantity: 6);
        await using var dbContext = CreateDbContext(options.ConnectionString);
        var job = new StockAlertJob(dbContext, NullLogger<StockAlertJob>.Instance);

        await job.ExecuteAsync();
        await job.ExecuteAsync();

        Assert.Equal(1, await CountActiveLowStockAlertsAsync(options.ConnectionString, company.SchemaName));
        Assert.Equal(1, await CountNotificationsAsync(options.ConnectionString, company.SchemaName, user.Id));
    }

    [PostgresIntegrationFact]
    public async Task ExecuteAsync_ResolvesActiveAlertWhenStockRecovers()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();
        await using var database = new TenantIntegrationTestDatabase(options);
        var company = await database.CreateCompanyAsync();
        await database.CreateUserAsync(company, roleName: "CompanyAdmin");
        var market = await database.InsertMarketAsync(company.SchemaName);
        var product = await database.InsertProductAsync(
            company.SchemaName,
            minStockAlert: 5);
        var inventory = await database.InsertInventoryAsync(
            company.SchemaName,
            product.Id,
            market.Id,
            quantity: 2);
        await using var dbContext = CreateDbContext(options.ConnectionString);
        var job = new StockAlertJob(dbContext, NullLogger<StockAlertJob>.Instance);

        await job.ExecuteAsync();
        await UpdateInventoryQuantityAsync(
            options.ConnectionString,
            company.SchemaName,
            inventory.Id,
            quantity: 8);
        await job.ExecuteAsync();

        Assert.Equal(0, await CountActiveLowStockAlertsAsync(options.ConnectionString, company.SchemaName));
        Assert.Equal(1, await CountResolvedLowStockAlertsAsync(options.ConnectionString, company.SchemaName));
    }

    [PostgresIntegrationFact]
    public async Task ExecuteAsync_WhenLowStockAlertsTableIsMissing_RecreatesTableBeforeScanning()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();
        await using var database = new TenantIntegrationTestDatabase(options);
        var company = await database.CreateCompanyAsync();
        await database.CreateUserAsync(company, roleName: "CompanyAdmin");
        var market = await database.InsertMarketAsync(company.SchemaName);
        var product = await database.InsertProductAsync(
            company.SchemaName,
            minStockAlert: 5);
        await database.InsertInventoryAsync(
            company.SchemaName,
            product.Id,
            market.Id,
            quantity: 2);
        await DropLowStockAlertsTableAsync(options.ConnectionString, company.SchemaName);
        await using var dbContext = CreateDbContext(options.ConnectionString);
        var job = new StockAlertJob(dbContext, NullLogger<StockAlertJob>.Instance);

        await job.ExecuteAsync();

        Assert.True(await database.TableExistsAsync(company.SchemaName, "low_stock_alerts"));
        Assert.Equal(1, await CountActiveLowStockAlertsAsync(options.ConnectionString, company.SchemaName));
    }

    private static ApplicationDbContext CreateDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new ApplicationDbContext(options);
    }

    private static async Task<int> CountActiveLowStockAlertsAsync(
        string connectionString,
        string schemaName)
    {
        return await CountLowStockAlertsByStatusAsync(connectionString, schemaName, "Active");
    }

    private static async Task<int> CountResolvedLowStockAlertsAsync(
        string connectionString,
        string schemaName)
    {
        return await CountLowStockAlertsByStatusAsync(connectionString, schemaName, "Resolved");
    }

    private static async Task<int> CountLowStockAlertsByStatusAsync(
        string connectionString,
        string schemaName,
        string status)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            $"""
            SELECT COUNT(*)::int
            FROM {QuoteIdentifier(schemaName)}.low_stock_alerts
            WHERE status = @status;
            """,
            connection);
        command.Parameters.AddWithValue("status", status);

        return (int)(await command.ExecuteScalarAsync() ?? 0);
    }

    private static async Task<int> CountNotificationsAsync(
        string connectionString,
        string schemaName,
        int userId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            $"""
            SELECT COUNT(*)::int
            FROM {QuoteIdentifier(schemaName)}.notifications
            WHERE user_id = @user_id
              AND type = 'LowStock';
            """,
            connection);
        command.Parameters.AddWithValue("user_id", userId);

        return (int)(await command.ExecuteScalarAsync() ?? 0);
    }

    private static async Task UpdateInventoryQuantityAsync(
        string connectionString,
        string schemaName,
        int inventoryId,
        int quantity)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            $"""
            UPDATE {QuoteIdentifier(schemaName)}.inventory
            SET quantity = @quantity,
                updated_at = NOW()
            WHERE id = @inventory_id;
            """,
            connection);
        command.Parameters.AddWithValue("inventory_id", inventoryId);
        command.Parameters.AddWithValue("quantity", quantity);

        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropLowStockAlertsTableAsync(
        string connectionString,
        string schemaName)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            $"DROP TABLE IF EXISTS {QuoteIdentifier(schemaName)}.low_stock_alerts;",
            connection);

        await command.ExecuteNonQueryAsync();
    }

    private static string QuoteIdentifier(string identifier)
    {
        return "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
