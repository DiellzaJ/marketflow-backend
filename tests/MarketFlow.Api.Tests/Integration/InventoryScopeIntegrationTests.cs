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
                movement.MovementType == "Adjustment" &&
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

    private static async Task<IReadOnlyList<InventoryItemDto>> GetInventoryAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/inventory");
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ServiceResult<IReadOnlyList<InventoryItemDto>>>();
        Assert.NotNull(result);
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);

        return result.Data;
    }

    private static async Task<IReadOnlyList<InventoryMovementDto>> GetInventoryMovementsAsync(
        HttpClient client,
        int inventoryId)
    {
        var response = await client.GetAsync($"/api/inventory/{inventoryId}/movements");
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ServiceResult<IReadOnlyList<InventoryMovementDto>>>();
        Assert.NotNull(result);
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);

        return result.Data;
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
