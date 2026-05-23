using MarketFlow.Application.Common.Interfaces;
using MarketFlow.Application.Features.Products.DTOs;
using MarketFlow.Application.Features.Products.Services;
using MarketFlow.Infrastructure.MultiTenancy;
using MarketFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MarketFlow.Api.Tests.Products;

public sealed class ProductLiveSmokeTests
{
    private const string TestConnectionStringEnvironmentVariable = "MARKETFLOW_TEST_DB_CONNECTION_STRING";

    [Fact]
    public async Task CreateProductAsync_WithUniqueBarcodeAndNoExcludedProductId_Succeeds()
    {
        var connectionString = Environment.GetEnvironmentVariable(TestConnectionStringEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var suffix = Guid.NewGuid().ToString("N")[..12];
        var schemaName = $"mf_test_product_create_{suffix}";

        await using var setupConnection = new NpgsqlConnection(connectionString);
        await setupConnection.OpenAsync();

        try
        {
            await ExecuteAsync(
                setupConnection,
                "SELECT public.create_tenant_schema(@schema_name);",
                new NpgsqlParameter("schema_name", schemaName));

            var categoryId = await ExecuteScalarAsync<int>(
                setupConnection,
                $"INSERT INTO {QuoteIdentifier(schemaName)}.categories (name) VALUES ('Smoke Category') RETURNING id;");

            var productService = CreateProductService(connectionString, schemaName);
            var request = new CreateProductRequest
            {
                Name = "Smoke Product",
                Barcode = $"CREATE-{suffix}",
                CategoryId = categoryId,
                UnitPrice = 2.25m,
                CostPrice = 1.15m
            };

            var result = await productService.CreateProductAsync(request);

            Assert.True(result.Succeeded, result.Message);
            Assert.Equal($"CREATE-{suffix}", result.Data?.Barcode);
        }
        finally
        {
            await ExecuteAsync(
                setupConnection,
                $"DROP SCHEMA IF EXISTS {QuoteIdentifier(schemaName)} CASCADE;");
        }
    }

    [Fact]
    public async Task CreateProductAsync_WithDuplicateBarcode_ReturnsConflict()
    {
        var connectionString = Environment.GetEnvironmentVariable(TestConnectionStringEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var suffix = Guid.NewGuid().ToString("N")[..12];
        var schemaName = $"mf_test_product_dup_{suffix}";
        var barcode = $"DUP-{suffix}";

        await using var setupConnection = new NpgsqlConnection(connectionString);
        await setupConnection.OpenAsync();

        try
        {
            await ExecuteAsync(
                setupConnection,
                "SELECT public.create_tenant_schema(@schema_name);",
                new NpgsqlParameter("schema_name", schemaName));

            var categoryId = await ExecuteScalarAsync<int>(
                setupConnection,
                $"INSERT INTO {QuoteIdentifier(schemaName)}.categories (name) VALUES ('Smoke Category') RETURNING id;");

            await ExecuteAsync(
                setupConnection,
                $"""
                INSERT INTO {QuoteIdentifier(schemaName)}.products (
                    name,
                    barcode,
                    category_id,
                    unit_price,
                    cost_price
                )
                VALUES (
                    'Existing Product',
                    @barcode,
                    @category_id,
                    1.25,
                    0.75
                );
                """,
                new NpgsqlParameter("barcode", barcode),
                new NpgsqlParameter("category_id", categoryId));

            var productService = CreateProductService(connectionString, schemaName);
            var request = new CreateProductRequest
            {
                Name = "Duplicate Product",
                Barcode = barcode,
                CategoryId = categoryId,
                UnitPrice = 2.25m,
                CostPrice = 1.15m
            };

            var result = await productService.CreateProductAsync(request);

            Assert.False(result.Succeeded);
            Assert.Equal("Barcode is already used by another product.", result.Message);
        }
        finally
        {
            await ExecuteAsync(
                setupConnection,
                $"DROP SCHEMA IF EXISTS {QuoteIdentifier(schemaName)} CASCADE;");
        }
    }

    [Fact]
    public async Task DeactivateProductAsync_PreservesSalesPurchasesAndInventoryReferences()
    {
        var connectionString = Environment.GetEnvironmentVariable(TestConnectionStringEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var suffix = Guid.NewGuid().ToString("N")[..12];
        var schemaName = $"mf_test_product_{suffix}";

        await using var setupConnection = new NpgsqlConnection(connectionString);
        await setupConnection.OpenAsync();

        try
        {
            await ExecuteAsync(
                setupConnection,
                "SELECT public.create_tenant_schema(@schema_name);",
                new NpgsqlParameter("schema_name", schemaName));

            var marketId = await ExecuteScalarAsync<int>(
                setupConnection,
                $"INSERT INTO {QuoteIdentifier(schemaName)}.markets (name) VALUES ('Smoke Market') RETURNING id;");

            var categoryId = await ExecuteScalarAsync<int>(
                setupConnection,
                $"INSERT INTO {QuoteIdentifier(schemaName)}.categories (name) VALUES ('Smoke Category') RETURNING id;");

            var supplierId = await ExecuteScalarAsync<int>(
                setupConnection,
                $"INSERT INTO {QuoteIdentifier(schemaName)}.suppliers (name) VALUES ('Smoke Supplier') RETURNING id;");

            var productId = await ExecuteScalarAsync<int>(
                setupConnection,
                $"""
                INSERT INTO {QuoteIdentifier(schemaName)}.products (
                    name,
                    barcode,
                    category_id,
                    unit_price,
                    cost_price
                )
                VALUES (
                    'Smoke Milk',
                    @barcode,
                    @category_id,
                    1.25,
                    0.75
                )
                RETURNING id;
                """,
                new NpgsqlParameter("barcode", $"SMOKE-{suffix}"),
                new NpgsqlParameter("category_id", categoryId));

            await ExecuteAsync(
                setupConnection,
                $"""
                INSERT INTO {QuoteIdentifier(schemaName)}.inventory (
                    product_id,
                    market_id,
                    quantity
                )
                VALUES (
                    @product_id,
                    @market_id,
                    10
                );
                """,
                new NpgsqlParameter("product_id", productId),
                new NpgsqlParameter("market_id", marketId));

            var purchaseId = await ExecuteScalarAsync<int>(
                setupConnection,
                $"""
                INSERT INTO {QuoteIdentifier(schemaName)}.purchases (
                    supplier_id,
                    market_id,
                    created_by_user_id,
                    total_amount
                )
                VALUES (
                    @supplier_id,
                    @market_id,
                    1,
                    7.50
                )
                RETURNING id;
                """,
                new NpgsqlParameter("supplier_id", supplierId),
                new NpgsqlParameter("market_id", marketId));

            await ExecuteAsync(
                setupConnection,
                $"""
                INSERT INTO {QuoteIdentifier(schemaName)}.purchase_items (
                    purchase_id,
                    product_id,
                    quantity,
                    unit_cost
                )
                VALUES (
                    @purchase_id,
                    @product_id,
                    10,
                    0.75
                );
                """,
                new NpgsqlParameter("purchase_id", purchaseId),
                new NpgsqlParameter("product_id", productId));

            var saleId = await ExecuteScalarAsync<int>(
                setupConnection,
                $"""
                INSERT INTO {QuoteIdentifier(schemaName)}.sales (
                    market_id,
                    created_by_user_id,
                    payment_method,
                    total_amount
                )
                VALUES (
                    @market_id,
                    1,
                    'Cash',
                    2.50
                )
                RETURNING id;
                """,
                new NpgsqlParameter("market_id", marketId));

            await ExecuteAsync(
                setupConnection,
                $"""
                INSERT INTO {QuoteIdentifier(schemaName)}.sale_items (
                    sale_id,
                    product_id,
                    quantity,
                    unit_price
                )
                VALUES (
                    @sale_id,
                    @product_id,
                    2,
                    1.25
                );
                """,
                new NpgsqlParameter("sale_id", saleId),
                new NpgsqlParameter("product_id", productId));

            var productService = CreateProductService(connectionString, schemaName);

            var result = await productService.DeactivateProductAsync(productId);

            Assert.True(result.Succeeded);
            Assert.False(result.Data?.IsActive);

            var referenceCount = await ExecuteScalarAsync<long>(
                setupConnection,
                $"""
                SELECT COUNT(*)
                FROM {QuoteIdentifier(schemaName)}.products p
                INNER JOIN {QuoteIdentifier(schemaName)}.inventory i ON i.product_id = p.id
                INNER JOIN {QuoteIdentifier(schemaName)}.purchase_items pi ON pi.product_id = p.id
                INNER JOIN {QuoteIdentifier(schemaName)}.sale_items si ON si.product_id = p.id
                WHERE p.id = @product_id
                  AND p.is_active = FALSE;
                """,
                new NpgsqlParameter("product_id", productId));

            Assert.Equal(1, referenceCount);
        }
        finally
        {
            await ExecuteAsync(
                setupConnection,
                $"DROP SCHEMA IF EXISTS {QuoteIdentifier(schemaName)} CASCADE;");
        }
    }

    private static ProductService CreateProductService(string connectionString, string schemaName)
    {
        var dbContextOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        var dbContext = new ApplicationDbContext(dbContextOptions);
        var currentUser = new TestCurrentUserService(schemaName);
        var tenantContextStore = new TestTenantContextStore(schemaName);
        var tenantQueryService = new TenantQueryService(
            dbContext,
            new TenantProvider(currentUser, tenantContextStore),
            currentUser);

        return new ProductService(tenantQueryService);
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection connection,
        string commandText,
        params NpgsqlParameter[] parameters)
    {
        await using var command = new NpgsqlCommand(commandText, connection);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<T> ExecuteScalarAsync<T>(
        NpgsqlConnection connection,
        string commandText,
        params NpgsqlParameter[] parameters)
    {
        await using var command = new NpgsqlCommand(commandText, connection);
        command.Parameters.AddRange(parameters);
        var result = await command.ExecuteScalarAsync();
        return Assert.IsType<T>(result);
    }

    private static string QuoteIdentifier(string identifier)
    {
        return "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private sealed class TestCurrentUserService(string schemaName) : ICurrentUserService
    {
        public int? UserId => 1;

        public int? CompanyId => 1;

        public string? Email => "product-smoke@marketflow.test";

        public string? Role => "CompanyAdmin";

        public string? SchemaName => schemaName;
    }

    private sealed class TestTenantContextStore(string schemaName) : ITenantContextStore
    {
        public Task<TenantContext?> GetTenantContextAsync(
            int userId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<TenantContext?>(new TenantContext(
                schemaName,
                UserIsActive: true,
                CompanyIsActive: true));
        }
    }
}
