using System.Net;
using System.Net.Http.Json;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Inventory.DTOs;

namespace MarketFlow.Api.Tests.Integration;

public sealed class InventoryScopeIntegrationTests
{
    private const string TenantSchemaHeaderName = "X-Tenant-Schema";

    [PostgresIntegrationFact]
    public async Task MainOperator_CanOnlyReadAndModifyAssignedMarketInventory()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);
        using var apiFactory = new TenantApiFactory(options);

        var setup = await CreateSingleTenantInventorySetupAsync(database, "main_operator_scope");
        var user = await database.CreateUserAsync(setup.Company, roleName: "MainOperator");
        await database.InsertStaffAssignmentAsync(setup.Company.SchemaName, user.Id, setup.MarketA.Id);

        using var client = apiFactory.CreateAuthenticatedClient(database, user);

        var list = await GetInventoryAsync(client);
        Assert.Contains(list, item => item.Id == setup.MarketAInventory.Id);
        Assert.Contains(list, item => item.Id == setup.DepartmentAInventory.Id);
        Assert.DoesNotContain(list, item => item.Id == setup.MarketBInventory.Id);

        var hiddenDetail = await client.GetAsync($"/api/inventory/{setup.MarketBInventory.Id}");
        Assert.Equal(HttpStatusCode.NotFound, hiddenDetail.StatusCode);

        var hiddenMovements = await client.GetAsync($"/api/inventory/{setup.MarketBInventory.Id}/movements");
        Assert.Equal(HttpStatusCode.NotFound, hiddenMovements.StatusCode);

        var createOutsideScope = await client.PostAsJsonAsync(
            "/api/inventory",
            new CreateInventoryItemRequest
            {
                ProductId = setup.ProductA.Id,
                MarketId = setup.MarketB.Id,
                Quantity = 4
            });
        Assert.Equal(HttpStatusCode.BadRequest, createOutsideScope.StatusCode);

        var updateOutsideScope = await client.PutAsJsonAsync(
            $"/api/inventory/{setup.MarketBInventory.Id}",
            new UpdateInventoryItemRequest { Quantity = 99, ReservedQuantity = 0 });
        Assert.Equal(HttpStatusCode.NotFound, updateOutsideScope.StatusCode);

        var patchOutsideScope = await client.PatchAsJsonAsync(
            $"/api/inventory/{setup.MarketBInventory.Id}",
            new PatchInventoryItemRequest { Quantity = 88 });
        Assert.Equal(HttpStatusCode.NotFound, patchOutsideScope.StatusCode);

        var protectedInventory = await database.GetInventoryDetailsAsync(
            setup.Company.SchemaName,
            setup.MarketBInventory.Id);
        Assert.Equal(setup.MarketBInventory, protectedInventory);
    }

    [PostgresIntegrationFact]
    public async Task DepartmentManager_CanOnlyReadAndModifyAssignedDepartmentInventory()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);
        using var apiFactory = new TenantApiFactory(options);

        var setup = await CreateSingleTenantInventorySetupAsync(database, "department_manager_scope");
        var user = await database.CreateUserAsync(setup.Company, roleName: "DepartmentManager");
        await database.InsertStaffAssignmentAsync(
            setup.Company.SchemaName,
            user.Id,
            setup.MarketA.Id,
            setup.DepartmentA.Id);

        using var client = apiFactory.CreateAuthenticatedClient(database, user);

        var list = await GetInventoryAsync(client);
        Assert.Single(list);
        Assert.Equal(setup.DepartmentAInventory.Id, list[0].Id);

        var updateOutsideScope = await client.PutAsJsonAsync(
            $"/api/inventory/{setup.MarketAInventory.Id}",
            new UpdateInventoryItemRequest { Quantity = 42, ReservedQuantity = 0 });
        Assert.Equal(HttpStatusCode.NotFound, updateOutsideScope.StatusCode);

        var updateInScope = await client.PatchAsJsonAsync(
            $"/api/inventory/{setup.DepartmentAInventory.Id}",
            new PatchInventoryItemRequest { Quantity = 43 });
        updateInScope.EnsureSuccessStatusCode();

        var updatedInventory = await database.GetInventoryDetailsAsync(
            setup.Company.SchemaName,
            setup.DepartmentAInventory.Id);
        Assert.Equal(43, updatedInventory?.Quantity);

        var movements = await GetInventoryMovementsAsync(client, setup.DepartmentAInventory.Id);
        Assert.Contains(
            movements,
                movement => movement.InventoryId == setup.DepartmentAInventory.Id &&
                movement.MovementType == "ManualAdjustment" &&
                movement.QuantityChanged == 31);
    }

    [PostgresIntegrationFact]
    public async Task InventoryEmployee_UsesDepartmentScopeWhenAssignedToDepartment_OtherwiseMarketScope()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);
        using var apiFactory = new TenantApiFactory(options);

        var setup = await CreateSingleTenantInventorySetupAsync(database, "inventory_employee_scope");
        var departmentUser = await database.CreateUserAsync(setup.Company, roleName: "InventoryEmployee");
        await database.InsertStaffAssignmentAsync(
            setup.Company.SchemaName,
            departmentUser.Id,
            setup.MarketA.Id,
            setup.DepartmentA.Id);

        using var departmentClient = apiFactory.CreateAuthenticatedClient(database, departmentUser);
        var departmentList = await GetInventoryAsync(departmentClient);
        Assert.Single(departmentList);
        Assert.Equal(setup.DepartmentAInventory.Id, departmentList[0].Id);

        var marketUser = await database.CreateUserAsync(setup.Company, roleName: "InventoryEmployee");
        await database.InsertStaffAssignmentAsync(setup.Company.SchemaName, marketUser.Id, setup.MarketA.Id);

        using var marketClient = apiFactory.CreateAuthenticatedClient(database, marketUser);
        var marketList = await GetInventoryAsync(marketClient);
        Assert.Contains(marketList, item => item.Id == setup.MarketAInventory.Id);
        Assert.Contains(marketList, item => item.Id == setup.DepartmentAInventory.Id);
        Assert.DoesNotContain(marketList, item => item.Id == setup.MarketBInventory.Id);
    }

    [PostgresIntegrationFact]
    public async Task CompanyAdmin_CanReadAllCompanyInventory_ButCannotReachAnotherCompanyInventory()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);
        using var apiFactory = new TenantApiFactory(options);

        var companyASetup = await CreateSingleTenantInventorySetupAsync(database, "inventory_tenant_a");
        var companyBSetup = await CreateSingleTenantInventorySetupAsync(database, "inventory_tenant_b");
        var companyBProtectedProduct = await database.InsertProductAsync(
            companyBSetup.Company.SchemaName,
            name: "inventory_tenant_b Protected Product");
        var companyBProtectedInventory = await database.InsertInventoryAsync(
            companyBSetup.Company.SchemaName,
            companyBProtectedProduct.Id,
            companyBSetup.MarketB.Id,
            quantity: 55);
        var companyAUser = await database.CreateUserAsync(companyASetup.Company, roleName: "CompanyAdmin");

        using var client = apiFactory.CreateAuthenticatedClient(database, companyAUser);
        client.DefaultRequestHeaders.Add(TenantSchemaHeaderName, companyBSetup.Company.SchemaName);

        var list = await GetInventoryAsync(client);
        Assert.Contains(list, item => item.Id == companyASetup.MarketAInventory.Id);
        Assert.Contains(list, item => item.Id == companyASetup.MarketBInventory.Id);
        Assert.DoesNotContain(list, item => item.ProductName.Contains("inventory_tenant_b", StringComparison.Ordinal));

        Assert.NotEqual(companyASetup.MarketAInventory.Id, companyBProtectedInventory.Id);
        Assert.NotEqual(companyASetup.MarketBInventory.Id, companyBProtectedInventory.Id);

        var updateOtherCompany = await client.PutAsJsonAsync(
            $"/api/inventory/{companyBProtectedInventory.Id}",
            new UpdateInventoryItemRequest { Quantity = 77, ReservedQuantity = 0 });
        Assert.Equal(HttpStatusCode.NotFound, updateOtherCompany.StatusCode);

        var protectedInventory = await database.GetInventoryDetailsAsync(
            companyBSetup.Company.SchemaName,
            companyBProtectedInventory.Id);
        Assert.Equal(companyBProtectedInventory, protectedInventory);

        var hiddenMovements = await client.GetAsync($"/api/inventory/{companyBProtectedInventory.Id}/movements");
        Assert.Equal(HttpStatusCode.NotFound, hiddenMovements.StatusCode);
    }

    [PostgresIntegrationFact]
    public async Task LowStockEndpoint_ReturnsQuantityScopedLowStockItems()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);
        using var apiFactory = new TenantApiFactory(options);

        await database.EnsureRequiredRolesAsync();

        var company = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName("low_stock_scope"),
            name: "low_stock_scope",
            dropSchemaOnDispose: true);
        var marketA = await database.InsertMarketAsync(company.SchemaName, "Market A");
        var marketB = await database.InsertMarketAsync(company.SchemaName, "Market B");
        var departmentA = await database.InsertDepartmentAsync(company.SchemaName, marketA.Id, "Produce");

        var lowProduct = await database.InsertProductAsync(
            company.SchemaName,
            name: "Low Product",
            minStockAlert: 5);
        var equalProduct = await database.InsertProductAsync(
            company.SchemaName,
            name: "Equal Product",
            minStockAlert: 2);
        var healthyProduct = await database.InsertProductAsync(
            company.SchemaName,
            name: "Healthy Product",
            minStockAlert: 1);
        var outsideScopeProduct = await database.InsertProductAsync(
            company.SchemaName,
            name: "Outside Scope Product",
            minStockAlert: 10);

        var lowInventory = await database.InsertInventoryAsync(
            company.SchemaName,
            lowProduct.Id,
            marketA.Id,
            quantity: 3);
        var equalInventory = await database.InsertInventoryAsync(
            company.SchemaName,
            equalProduct.Id,
            marketA.Id,
            departmentA.Id,
            quantity: 2);
        var healthyInventory = await database.InsertInventoryAsync(
            company.SchemaName,
            healthyProduct.Id,
            marketA.Id,
            quantity: 8);
        var outsideScopeInventory = await database.InsertInventoryAsync(
            company.SchemaName,
            outsideScopeProduct.Id,
            marketB.Id,
            quantity: 1);

        var user = await database.CreateUserAsync(company, roleName: "MainOperator");
        await database.InsertStaffAssignmentAsync(company.SchemaName, user.Id, marketA.Id);

        using var client = apiFactory.CreateAuthenticatedClient(database, user);

        var lowStock = await GetLowStockInventoryAsync(client);

        Assert.Contains(lowStock, item =>
            item.Id == lowInventory.Id &&
            item.Quantity == 3 &&
            item.MinStockAlert == 5 &&
            item.SuggestedRestockQuantity == 2 &&
            item.IsLowStock);
        Assert.Contains(lowStock, item =>
            item.Id == equalInventory.Id &&
            item.Quantity == 2 &&
            item.MinStockAlert == 2 &&
            item.SuggestedRestockQuantity == 0 &&
            item.IsLowStock);
        Assert.DoesNotContain(lowStock, item => item.Id == healthyInventory.Id);
        Assert.DoesNotContain(lowStock, item => item.Id == outsideScopeInventory.Id);
    }

    [PostgresIntegrationFact]
    public async Task TransferEndpoint_UpdatesStockAndCreatesMovementHistory()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);
        using var apiFactory = new TenantApiFactory(options);

        await database.EnsureRequiredRolesAsync();

        var company = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName("transfer_success"),
            name: "transfer_success",
            dropSchemaOnDispose: true);
        var marketA = await database.InsertMarketAsync(company.SchemaName, "Market A");
        var marketB = await database.InsertMarketAsync(company.SchemaName, "Market B");
        var product = await database.InsertProductAsync(company.SchemaName, name: "Transfer Product");
        var source = await database.InsertInventoryAsync(
            company.SchemaName,
            product.Id,
            marketA.Id,
            quantity: 10);
        var destination = await database.InsertInventoryAsync(
            company.SchemaName,
            product.Id,
            marketB.Id,
            quantity: 2);
        var user = await database.CreateUserAsync(company, roleName: "CompanyAdmin");

        using var client = apiFactory.CreateAuthenticatedClient(database, user);

        var response = await client.PostAsJsonAsync(
            "/api/inventory/transfer",
            new TransferInventoryRequest
            {
                ProductId = product.Id,
                FromMarketId = marketA.Id,
                ToMarketId = marketB.Id,
                Quantity = 4,
                Note = "Store balancing"
            });

        response.EnsureSuccessStatusCode();

        var updatedSource = await database.GetInventoryDetailsAsync(company.SchemaName, source.Id);
        var updatedDestination = await database.GetInventoryDetailsAsync(company.SchemaName, destination.Id);
        Assert.Equal(6, updatedSource?.Quantity);
        Assert.Equal(6, updatedDestination?.Quantity);

        var sourceMovements = await GetInventoryMovementsAsync(client, source.Id);
        var destinationMovements = await GetInventoryMovementsAsync(client, destination.Id);
        Assert.Contains(sourceMovements, movement =>
            movement.MovementType == "TransferOut" &&
            movement.QuantityChanged == -4 &&
            movement.Note == "Store balancing");
        Assert.Contains(destinationMovements, movement =>
            movement.MovementType == "TransferIn" &&
            movement.QuantityChanged == 4 &&
            movement.Note == "Store balancing");
    }

    [PostgresIntegrationFact]
    public async Task TransferEndpoint_FailsWhenSourceStockIsInsufficient()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);
        using var apiFactory = new TenantApiFactory(options);

        await database.EnsureRequiredRolesAsync();

        var company = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName("transfer_insufficient"),
            name: "transfer_insufficient",
            dropSchemaOnDispose: true);
        var marketA = await database.InsertMarketAsync(company.SchemaName, "Market A");
        var marketB = await database.InsertMarketAsync(company.SchemaName, "Market B");
        var product = await database.InsertProductAsync(company.SchemaName, name: "Transfer Product");
        var source = await database.InsertInventoryAsync(
            company.SchemaName,
            product.Id,
            marketA.Id,
            quantity: 3);
        var destination = await database.InsertInventoryAsync(
            company.SchemaName,
            product.Id,
            marketB.Id,
            quantity: 2);
        var user = await database.CreateUserAsync(company, roleName: "CompanyAdmin");

        using var client = apiFactory.CreateAuthenticatedClient(database, user);

        var response = await client.PostAsJsonAsync(
            "/api/inventory/transfer",
            new TransferInventoryRequest
            {
                ProductId = product.Id,
                FromMarketId = marketA.Id,
                ToMarketId = marketB.Id,
                Quantity = 4
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var unchangedSource = await database.GetInventoryDetailsAsync(company.SchemaName, source.Id);
        var unchangedDestination = await database.GetInventoryDetailsAsync(company.SchemaName, destination.Id);
        Assert.Equal(3, unchangedSource?.Quantity);
        Assert.Equal(2, unchangedDestination?.Quantity);
    }

    [PostgresIntegrationFact]
    public async Task TransferEndpoint_FailsWhenDestinationIsOutsideUserScope()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);
        using var apiFactory = new TenantApiFactory(options);

        await database.EnsureRequiredRolesAsync();

        var company = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName("transfer_scope"),
            name: "transfer_scope",
            dropSchemaOnDispose: true);
        var marketA = await database.InsertMarketAsync(company.SchemaName, "Market A");
        var marketB = await database.InsertMarketAsync(company.SchemaName, "Market B");
        var product = await database.InsertProductAsync(company.SchemaName, name: "Transfer Product");
        var source = await database.InsertInventoryAsync(
            company.SchemaName,
            product.Id,
            marketA.Id,
            quantity: 10);
        var destination = await database.InsertInventoryAsync(
            company.SchemaName,
            product.Id,
            marketB.Id,
            quantity: 2);
        var user = await database.CreateUserAsync(company, roleName: "MainOperator");
        await database.InsertStaffAssignmentAsync(company.SchemaName, user.Id, marketA.Id);

        using var client = apiFactory.CreateAuthenticatedClient(database, user);

        var response = await client.PostAsJsonAsync(
            "/api/inventory/transfer",
            new TransferInventoryRequest
            {
                ProductId = product.Id,
                FromMarketId = marketA.Id,
                ToMarketId = marketB.Id,
                Quantity = 4
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var unchangedSource = await database.GetInventoryDetailsAsync(company.SchemaName, source.Id);
        var unchangedDestination = await database.GetInventoryDetailsAsync(company.SchemaName, destination.Id);
        Assert.Equal(10, unchangedSource?.Quantity);
        Assert.Equal(2, unchangedDestination?.Quantity);
    }

    private static async Task<IReadOnlyList<InventoryItemDto>> GetInventoryAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/inventory");
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ServiceResult<PagedResult<InventoryItemDto>>>();
        Assert.NotNull(result);
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);

        return result.Data.Items.ToList();
    }

    private static async Task<IReadOnlyList<InventoryItemDto>> GetLowStockInventoryAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/inventory/low-stock");
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ServiceResult<IReadOnlyCollection<InventoryItemDto>>>();
        Assert.NotNull(result);
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);

        return result.Data.ToList();
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

    private static async Task<InventoryScopeSetup> CreateSingleTenantInventorySetupAsync(
        TenantIntegrationTestDatabase database,
        string schemaPrefix)
    {
        await database.EnsureRequiredRolesAsync();

        var company = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName(schemaPrefix),
            name: schemaPrefix,
            dropSchemaOnDispose: true);
        var marketA = await database.InsertMarketAsync(company.SchemaName, "Market A");
        var marketB = await database.InsertMarketAsync(company.SchemaName, "Market B");
        var departmentA = await database.InsertDepartmentAsync(company.SchemaName, marketA.Id, "Produce");
        var productA = await database.InsertProductAsync(company.SchemaName, name: $"{schemaPrefix} Product A");
        var productB = await database.InsertProductAsync(company.SchemaName, name: $"{schemaPrefix} Product B");
        var productC = await database.InsertProductAsync(company.SchemaName, name: $"{schemaPrefix} Product C");
        var marketAInventory = await database.InsertInventoryAsync(
            company.SchemaName,
            productA.Id,
            marketA.Id,
            quantity: 11);
        var departmentAInventory = await database.InsertInventoryAsync(
            company.SchemaName,
            productB.Id,
            marketA.Id,
            departmentA.Id,
            quantity: 12);
        var marketBInventory = await database.InsertInventoryAsync(
            company.SchemaName,
            productC.Id,
            marketB.Id,
            quantity: 13);

        return new InventoryScopeSetup(
            company,
            marketA,
            marketB,
            departmentA,
            productA,
            marketAInventory,
            departmentAInventory,
            marketBInventory);
    }

    private sealed record InventoryScopeSetup(
        TenantTestCompany Company,
        TenantTestMarket MarketA,
        TenantTestMarket MarketB,
        TenantTestDepartment DepartmentA,
        TenantTestProduct ProductA,
        TenantTestInventoryItem MarketAInventory,
        TenantTestInventoryItem DepartmentAInventory,
        TenantTestInventoryItem MarketBInventory);
}
