using System.Net;
using System.Net.Http.Json;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Inventory.DTOs;
using MarketFlow.Application.Features.Sales.DTOs;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace MarketFlow.Api.Tests.Integration;

public sealed class SaleCompletionIntegrationTests
{
    [PostgresIntegrationFact]
    public async Task CreateSale_DecreasesStockAndCreatesMovementHistory()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);
        using var apiFactory = new TenantApiFactory(options);

        await database.EnsureRequiredRolesAsync();

        var company = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName("sale_completion"),
            name: "sale_completion",
            dropSchemaOnDispose: true);
        var market = await database.InsertMarketAsync(company.SchemaName, "Market A");
        var product = await database.InsertProductAsync(company.SchemaName, name: "Sale Product");
        var inventory = await database.InsertInventoryAsync(
            company.SchemaName,
            product.Id,
            market.Id,
            quantity: 8);
        var user = await database.CreateUserAsync(company, roleName: "CompanyAdmin");

        using var client = apiFactory.CreateAuthenticatedClient(database, user);

        var response = await client.PostAsJsonAsync("/api/sales", new CreateSaleRequest
        {
            MarketId = market.Id,
            PaymentMethod = "Cash",
            TotalAmount = 15,
            Items =
            [
                new CreateSaleItemRequest
                {
                    ProductId = product.Id,
                    Quantity = 3,
                    UnitPrice = 5
                }
            ]
        });
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ServiceResult<SaleDto>>();
        Assert.NotNull(result?.Data);
        Assert.NotEmpty(result.Data.ReferenceNumber);
        Assert.StartsWith("SALE-", result.Data.ReferenceNumber, StringComparison.Ordinal);

        var updatedInventory = await database.GetInventoryDetailsAsync(company.SchemaName, inventory.Id);
        Assert.Equal(5, updatedInventory?.Quantity);

        var movements = await GetInventoryMovementsAsync(client, inventory.Id);
        Assert.Single(movements, movement =>
            movement.MovementType == "SaleCompleted" &&
            movement.QuantityChanged == -3 &&
            movement.ReferenceNumber == $"sale:{result.Data.Id}");
    }

    [PostgresIntegrationFact]
    public async Task CreateSale_GeneratesUniqueReferenceNumbersAndPreventsDuplicates()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);
        using var apiFactory = new TenantApiFactory(options);

        await database.EnsureRequiredRolesAsync();

        var company = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName("sale_reference"),
            name: "sale_reference",
            dropSchemaOnDispose: true);
        var market = await database.InsertMarketAsync(company.SchemaName, "Market A");
        var product = await database.InsertProductAsync(company.SchemaName, name: "Receipt Product");
        await database.InsertInventoryAsync(
            company.SchemaName,
            product.Id,
            market.Id,
            quantity: 10);
        var user = await database.CreateUserAsync(company, roleName: "CompanyAdmin");

        using var client = apiFactory.CreateAuthenticatedClient(database, user);

        var firstSale = await CreateSaleAsync(client, market.Id, product.Id);
        var secondSale = await CreateSaleAsync(client, market.Id, product.Id);

        Assert.NotEmpty(firstSale.ReferenceNumber);
        Assert.NotEmpty(secondSale.ReferenceNumber);
        Assert.NotEqual(firstSale.ReferenceNumber, secondSale.ReferenceNumber);

        var duplicateException = await Assert.ThrowsAsync<PostgresException>(() =>
            database.InsertSaleWithReferenceNumberAsync(
                company.SchemaName,
                market.Id,
                user.Id,
                firstSale.ReferenceNumber));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicateException.SqlState);
    }

    [PostgresIntegrationFact]
    public async Task CreateSale_WhenTenantReferenceSchemaIsHealthy_DoesNotExecuteRepairDdl()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();
        using var loggerProvider = new CollectingLoggerProvider();

        await using var database = new TenantIntegrationTestDatabase(options);
        using var apiFactory = new TenantApiFactory(options, loggerProvider);

        await database.EnsureRequiredRolesAsync();

        var company = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName("sale_reference_healthy"),
            name: "sale_reference_healthy",
            dropSchemaOnDispose: true);
        var market = await database.InsertMarketAsync(company.SchemaName, "Market A");
        var product = await database.InsertProductAsync(company.SchemaName, name: "Healthy Receipt Product");
        await database.InsertInventoryAsync(
            company.SchemaName,
            product.Id,
            market.Id,
            quantity: 5);
        var user = await database.CreateUserAsync(company, roleName: "CompanyAdmin");

        var initialState = await database.GetSaleReferenceSchemaStateAsync(company.SchemaName);
        Assert.True(initialState.ColumnExists);
        Assert.True(initialState.TriggerExists);
        Assert.True(initialState.UniqueIndexExists);
        Assert.True(initialState.TriggerFunctionExists);

        using var client = apiFactory.CreateAuthenticatedClient(database, user);

        var sale = await CreateSaleAsync(client, market.Id, product.Id);

        Assert.NotEmpty(sale.ReferenceNumber);

        var salesResponse = await client.GetAsync("/api/sales");
        salesResponse.EnsureSuccessStatusCode();

        var salesResult = await salesResponse.Content
            .ReadFromJsonAsync<ServiceResult<IReadOnlyCollection<SaleDto>>>();
        Assert.NotNull(salesResult?.Data);
        Assert.Equal(sale.ReferenceNumber, Assert.Single(salesResult.Data).ReferenceNumber);

        var detailsResponse = await client.GetAsync($"/api/sales/{sale.Id}");
        detailsResponse.EnsureSuccessStatusCode();

        var detailsResult = await detailsResponse.Content
            .ReadFromJsonAsync<ServiceResult<SaleDetailsResponse>>();
        Assert.NotNull(detailsResult?.Data);
        Assert.Equal(sale.ReferenceNumber, detailsResult.Data.ReferenceNumber);

        Assert.DoesNotContain(
            loggerProvider.Messages,
            message => message.Contains(
                "Executing sale reference number repair step",
                StringComparison.Ordinal));
    }

    [PostgresIntegrationFact]
    public async Task CreateSale_WhenTenantReferenceSchemaIsMissing_RepairsSchemaAndReturnsReferenceNumber()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);
        using var apiFactory = new TenantApiFactory(options);

        await database.EnsureRequiredRolesAsync();

        var company = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName("sale_reference_repair"),
            name: "sale_reference_repair",
            dropSchemaOnDispose: true);
        var market = await database.InsertMarketAsync(company.SchemaName, "Market A");
        var product = await database.InsertProductAsync(company.SchemaName, name: "Repair Receipt Product");
        await database.InsertInventoryAsync(
            company.SchemaName,
            product.Id,
            market.Id,
            quantity: 5);
        var user = await database.CreateUserAsync(company, roleName: "CompanyAdmin");
        await database.RemoveSaleReferenceNumbersAsync(company.SchemaName);

        var initialState = await database.GetSaleReferenceSchemaStateAsync(company.SchemaName);
        Assert.False(initialState.ColumnExists);
        Assert.False(initialState.TriggerExists);
        Assert.False(initialState.UniqueIndexExists);

        using var client = apiFactory.CreateAuthenticatedClient(database, user);

        var sale = await CreateSaleAsync(client, market.Id, product.Id);

        Assert.NotEmpty(sale.ReferenceNumber);
        Assert.StartsWith("SALE-", sale.ReferenceNumber, StringComparison.Ordinal);

        var repairedState = await database.GetSaleReferenceSchemaStateAsync(company.SchemaName);
        Assert.True(repairedState.ColumnExists);
        Assert.True(repairedState.TriggerExists);
        Assert.True(repairedState.UniqueIndexExists);
        Assert.True(repairedState.TriggerFunctionExists);
    }

    [PostgresIntegrationFact]
    public async Task CreateSale_WhenTenantReferenceTriggerIsMissing_RepairsSchemaAndReturnsReferenceNumber()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);
        using var apiFactory = new TenantApiFactory(options);

        await database.EnsureRequiredRolesAsync();

        var company = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName("sale_reference_trigger"),
            name: "sale_reference_trigger",
            dropSchemaOnDispose: true);
        var market = await database.InsertMarketAsync(company.SchemaName, "Market A");
        var product = await database.InsertProductAsync(company.SchemaName, name: "Trigger Repair Product");
        await database.InsertInventoryAsync(
            company.SchemaName,
            product.Id,
            market.Id,
            quantity: 5);
        var user = await database.CreateUserAsync(company, roleName: "CompanyAdmin");

        await database.RemoveSaleReferenceTriggerAsync(company.SchemaName);

        var initialState = await database.GetSaleReferenceSchemaStateAsync(company.SchemaName);
        Assert.True(initialState.ColumnExists);
        Assert.False(initialState.TriggerExists);

        using var client = apiFactory.CreateAuthenticatedClient(database, user);

        var sale = await CreateSaleAsync(client, market.Id, product.Id);

        Assert.NotEmpty(sale.ReferenceNumber);
        Assert.StartsWith("SALE-", sale.ReferenceNumber, StringComparison.Ordinal);

        var repairedState = await database.GetSaleReferenceSchemaStateAsync(company.SchemaName);
        Assert.True(repairedState.ColumnExists);
        Assert.True(repairedState.TriggerExists);
        Assert.True(repairedState.UniqueIndexExists);
        Assert.True(repairedState.TriggerFunctionExists);
    }

    [PostgresIntegrationFact]
    public async Task CreateSale_FailsWhenStockIsInsufficient()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);
        using var apiFactory = new TenantApiFactory(options);

        await database.EnsureRequiredRolesAsync();

        var company = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName("sale_insufficient"),
            name: "sale_insufficient",
            dropSchemaOnDispose: true);
        var market = await database.InsertMarketAsync(company.SchemaName, "Market A");
        var product = await database.InsertProductAsync(company.SchemaName, name: "Limited Sale Product");
        var inventory = await database.InsertInventoryAsync(
            company.SchemaName,
            product.Id,
            market.Id,
            quantity: 2);
        var user = await database.CreateUserAsync(company, roleName: "CompanyAdmin");

        using var client = apiFactory.CreateAuthenticatedClient(database, user);

        var response = await client.PostAsJsonAsync("/api/sales", new CreateSaleRequest
        {
            MarketId = market.Id,
            PaymentMethod = "Cash",
            TotalAmount = 15,
            Items =
            [
                new CreateSaleItemRequest
                {
                    ProductId = product.Id,
                    Quantity = 3,
                    UnitPrice = 5
                }
            ]
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var updatedInventory = await database.GetInventoryDetailsAsync(company.SchemaName, inventory.Id);
        Assert.Equal(2, updatedInventory?.Quantity);
        Assert.Equal(0, await database.CountRowsAsync(company.SchemaName, "sales"));

        var movements = await GetInventoryMovementsAsync(client, inventory.Id);
        Assert.DoesNotContain(movements, movement => movement.MovementType == "SaleCompleted");
    }

    [PostgresIntegrationFact]
    public async Task CreateSale_SucceedsWhenAvailableStockIsEnoughAndKeepsReservedQuantity()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);
        using var apiFactory = new TenantApiFactory(options);

        await database.EnsureRequiredRolesAsync();

        var company = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName("sale_available_stock"),
            name: "sale_available_stock",
            dropSchemaOnDispose: true);
        var market = await database.InsertMarketAsync(company.SchemaName, "Market A");
        var product = await database.InsertProductAsync(
            company.SchemaName,
            name: "Available Sale Product",
            barcode: "SALE-AVAILABLE");
        var inventory = await database.InsertInventoryAsync(
            company.SchemaName,
            product.Id,
            market.Id,
            quantity: 8,
            reservedQuantity: 3);
        var user = await database.CreateUserAsync(company, roleName: "CompanyAdmin");

        using var client = apiFactory.CreateAuthenticatedClient(database, user);

        var beforeLookup = await GetPosProductsAsync(client, $"marketId={market.Id}&barcode=SALE-AVAILABLE");
        Assert.Equal(5, Assert.Single(beforeLookup).AvailableQuantity);

        var response = await client.PostAsJsonAsync("/api/sales", new CreateSaleRequest
        {
            MarketId = market.Id,
            PaymentMethod = "Cash",
            TotalAmount = 25,
            Items =
            [
                new CreateSaleItemRequest
                {
                    ProductId = product.Id,
                    Quantity = 5,
                    UnitPrice = 5
                }
            ]
        });
        response.EnsureSuccessStatusCode();

        var updatedInventory = await database.GetInventoryDetailsAsync(company.SchemaName, inventory.Id);
        Assert.Equal(3, updatedInventory?.Quantity);
        Assert.Equal(3, updatedInventory?.ReservedQuantity);

        var afterLookup = await GetPosProductsAsync(client, $"marketId={market.Id}&barcode=SALE-AVAILABLE");
        Assert.Equal(0, Assert.Single(afterLookup).AvailableQuantity);
    }

    [PostgresIntegrationFact]
    public async Task CreateSale_FailsWhenOnlyReservedStockWouldCoverRequestedQuantity()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);
        using var apiFactory = new TenantApiFactory(options);

        await database.EnsureRequiredRolesAsync();

        var company = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName("sale_reserved_stock"),
            name: "sale_reserved_stock",
            dropSchemaOnDispose: true);
        var market = await database.InsertMarketAsync(company.SchemaName, "Market A");
        var product = await database.InsertProductAsync(
            company.SchemaName,
            name: "Reserved Sale Product",
            barcode: "SALE-RESERVED");
        var inventory = await database.InsertInventoryAsync(
            company.SchemaName,
            product.Id,
            market.Id,
            quantity: 8,
            reservedQuantity: 6);
        var user = await database.CreateUserAsync(company, roleName: "CompanyAdmin");

        using var client = apiFactory.CreateAuthenticatedClient(database, user);

        var lookup = await GetPosProductsAsync(client, $"marketId={market.Id}&barcode=SALE-RESERVED");
        Assert.Equal(2, Assert.Single(lookup).AvailableQuantity);

        var response = await client.PostAsJsonAsync("/api/sales", new CreateSaleRequest
        {
            MarketId = market.Id,
            PaymentMethod = "Cash",
            TotalAmount = 15,
            Items =
            [
                new CreateSaleItemRequest
                {
                    ProductId = product.Id,
                    Quantity = 3,
                    UnitPrice = 5
                }
            ]
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var updatedInventory = await database.GetInventoryDetailsAsync(company.SchemaName, inventory.Id);
        Assert.Equal(8, updatedInventory?.Quantity);
        Assert.Equal(6, updatedInventory?.ReservedQuantity);
        Assert.Equal(0, await database.CountRowsAsync(company.SchemaName, "sales"));

        var afterLookup = await GetPosProductsAsync(client, $"marketId={market.Id}&barcode=SALE-RESERVED");
        Assert.Equal(2, Assert.Single(afterLookup).AvailableQuantity);
    }

    [PostgresIntegrationFact]
    public async Task CreateSale_ForDepartmentAssignedSellerUsesDepartmentInventoryScope()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);
        using var apiFactory = new TenantApiFactory(options);

        await database.EnsureRequiredRolesAsync();

        var company = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName("sale_department_scope"),
            name: "sale_department_scope",
            dropSchemaOnDispose: true);
        var market = await database.InsertMarketAsync(company.SchemaName, "Market A");
        var departmentA = await database.InsertDepartmentAsync(company.SchemaName, market.Id, "Department A");
        var departmentB = await database.InsertDepartmentAsync(company.SchemaName, market.Id, "Department B");
        var product = await database.InsertProductAsync(company.SchemaName, name: "Department Sale Product");
        var departmentAInventory = await database.InsertInventoryAsync(
            company.SchemaName,
            product.Id,
            market.Id,
            departmentA.Id,
            quantity: 6);
        var departmentBInventory = await database.InsertInventoryAsync(
            company.SchemaName,
            product.Id,
            market.Id,
            departmentB.Id,
            quantity: 4);
        var admin = await database.CreateUserAsync(company, roleName: "CompanyAdmin");
        var seller = await database.CreateUserAsync(company, roleName: "Seller");
        await database.InsertStaffAssignmentAsync(company.SchemaName, seller.Id, market.Id, departmentA.Id);

        using var sellerClient = apiFactory.CreateAuthenticatedClient(database, seller);
        using var adminClient = apiFactory.CreateAuthenticatedClient(database, admin);

        var response = await sellerClient.PostAsJsonAsync("/api/sales", new CreateSaleRequest
        {
            MarketId = market.Id,
            PaymentMethod = "Cash",
            TotalAmount = 10,
            Items =
            [
                new CreateSaleItemRequest
                {
                    ProductId = product.Id,
                    Quantity = 2,
                    UnitPrice = 5
                }
            ]
        });
        response.EnsureSuccessStatusCode();

        var updatedDepartmentAInventory = await database.GetInventoryDetailsAsync(
            company.SchemaName,
            departmentAInventory.Id);
        var updatedDepartmentBInventory = await database.GetInventoryDetailsAsync(
            company.SchemaName,
            departmentBInventory.Id);

        Assert.Equal(4, updatedDepartmentAInventory?.Quantity);
        Assert.Equal(4, updatedDepartmentBInventory?.Quantity);

        var movements = await GetInventoryMovementsAsync(adminClient, departmentAInventory.Id);
        Assert.Single(movements, movement =>
            movement.MovementType == "SaleCompleted" &&
            movement.QuantityChanged == -2);
    }

    private static async Task<IReadOnlyList<InventoryMovementDto>> GetInventoryMovementsAsync(
        HttpClient client,
        int inventoryId)
    {
        var response = await client.GetAsync($"/api/inventory/{inventoryId}/movements");
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ServiceResult<PagedResult<InventoryMovementDto>>>();
        Assert.NotNull(result);
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);

        return result.Data.Items.ToList();
    }

    private sealed class CollectingLoggerProvider : ILoggerProvider
    {
        private readonly object _syncRoot = new();
        private readonly List<string> _messages = [];

        public IReadOnlyCollection<string> Messages
        {
            get
            {
                lock (_syncRoot)
                {
                    return _messages.ToArray();
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => new CollectingLogger(this);

        public void Dispose()
        {
        }

        private void Add(string message)
        {
            lock (_syncRoot)
            {
                _messages.Add(message);
            }
        }

        private sealed class CollectingLogger(CollectingLoggerProvider provider) : ILogger
        {
            public IDisposable BeginScope<TState>(TState state)
                where TState : notnull =>
                NullScope.Instance;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                provider.Add(formatter(state, exception));
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }

    private static async Task<IReadOnlyList<PosProductLookupItemDto>> GetPosProductsAsync(
        HttpClient client,
        string query)
    {
        var response = await client.GetAsync($"/api/inventory/pos-products?{query}");
        response.EnsureSuccessStatusCode();

        var result = await response.Content
            .ReadFromJsonAsync<ServiceResult<IReadOnlyCollection<PosProductLookupItemDto>>>();
        Assert.NotNull(result);
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);

        return result.Data.ToList();
    }

    private static async Task<SaleDto> CreateSaleAsync(
        HttpClient client,
        int marketId,
        int productId)
    {
        var response = await client.PostAsJsonAsync("/api/sales", new CreateSaleRequest
        {
            MarketId = marketId,
            PaymentMethod = "Cash",
            TotalAmount = 5,
            Items =
            [
                new CreateSaleItemRequest
                {
                    ProductId = productId,
                    Quantity = 1,
                    UnitPrice = 5
                }
            ]
        });
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ServiceResult<SaleDto>>();
        Assert.NotNull(result?.Data);

        return result.Data;
    }
}
