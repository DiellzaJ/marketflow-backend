using System.Net;
using System.Net.Http.Json;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Sales.DTOs;

namespace MarketFlow.Api.Tests.Integration;

public sealed class SalesScopeIntegrationTests
{
    private const string TenantSchemaHeaderName = "X-Tenant-Schema";

    [PostgresIntegrationFact]
    public async Task MainOperator_CanOnlyReadAndModifyAssignedMarketSales()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);
        using var apiFactory = new TenantApiFactory(options);

        await database.EnsureRequiredRolesAsync();
        var company = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName("sales_market_scope"),
            name: "sales_market_scope",
            dropSchemaOnDispose: true);
        var marketA = await database.InsertMarketAsync(company.SchemaName, "Market A");
        var marketB = await database.InsertMarketAsync(company.SchemaName, "Market B");
        var productA = await database.InsertProductAsync(company.SchemaName, name: "Market A Sale Product");
        var productB = await database.InsertProductAsync(company.SchemaName, name: "Market B Sale Product");
        await database.InsertInventoryAsync(company.SchemaName, productA.Id, marketA.Id, quantity: 10);
        await database.InsertInventoryAsync(company.SchemaName, productB.Id, marketB.Id, quantity: 10);
        var admin = await database.CreateUserAsync(company, roleName: "CompanyAdmin");

        using var adminClient = apiFactory.CreateAuthenticatedClient(database, admin);
        var marketASale = await CreateSaleAsync(adminClient, marketA.Id, productA.Id);
        var marketBSale = await CreateSaleAsync(adminClient, marketB.Id, productB.Id);

        var mainOperator = await database.CreateUserAsync(company, roleName: "MainOperator");
        await database.InsertStaffAssignmentAsync(company.SchemaName, mainOperator.Id, marketA.Id);

        using var client = apiFactory.CreateAuthenticatedClient(database, mainOperator);

        var sales = await GetSalesAsync(client);
        Assert.Contains(sales, sale => sale.Id == marketASale.Id);
        Assert.DoesNotContain(sales, sale => sale.Id == marketBSale.Id);

        var hiddenDetails = await client.GetAsync($"/api/sales/{marketBSale.Id}");
        Assert.Equal(HttpStatusCode.NotFound, hiddenDetails.StatusCode);

        var updateOutsideScope = await client.PatchAsJsonAsync(
            $"/api/sales/{marketBSale.Id}",
            new PatchSaleRequest { TotalAmount = 99 });
        Assert.Equal(HttpStatusCode.NotFound, updateOutsideScope.StatusCode);

        var createOutsideScope = await client.PostAsJsonAsync("/api/sales", new CreateSaleRequest
        {
            MarketId = marketB.Id,
            PaymentMethod = "Cash",
            TotalAmount = 5,
            Items =
            [
                new CreateSaleItemRequest
                {
                    ProductId = productB.Id,
                    Quantity = 1,
                    UnitPrice = 5
                }
            ]
        });
        Assert.Equal(HttpStatusCode.BadRequest, createOutsideScope.StatusCode);
    }

    [PostgresIntegrationFact]
    public async Task DepartmentManager_CanReadAssignedDepartmentSalesAfterInventoryIsDeleted()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);
        using var apiFactory = new TenantApiFactory(options);

        await database.EnsureRequiredRolesAsync();
        var company = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName("sales_department_scope"),
            name: "sales_department_scope",
            dropSchemaOnDispose: true);
        var market = await database.InsertMarketAsync(company.SchemaName, "Market A");
        var departmentA = await database.InsertDepartmentAsync(company.SchemaName, market.Id, "Produce");
        var departmentB = await database.InsertDepartmentAsync(company.SchemaName, market.Id, "Dairy");
        var productA = await database.InsertProductAsync(company.SchemaName, name: "Department A Sale Product");
        var productB = await database.InsertProductAsync(company.SchemaName, name: "Department B Sale Product");
        var departmentAInventory = await database.InsertInventoryAsync(
            company.SchemaName,
            productA.Id,
            market.Id,
            departmentA.Id,
            quantity: 10);
        await database.InsertInventoryAsync(company.SchemaName, productB.Id, market.Id, departmentB.Id, quantity: 10);

        var sellerA = await database.CreateUserAsync(company, roleName: "Seller");
        await database.InsertStaffAssignmentAsync(company.SchemaName, sellerA.Id, market.Id, departmentA.Id);
        using var sellerAClient = apiFactory.CreateAuthenticatedClient(database, sellerA);
        var departmentASale = await CreateSaleAsync(sellerAClient, market.Id, productA.Id);

        var sellerB = await database.CreateUserAsync(company, roleName: "Seller");
        await database.InsertStaffAssignmentAsync(company.SchemaName, sellerB.Id, market.Id, departmentB.Id);
        using var sellerBClient = apiFactory.CreateAuthenticatedClient(database, sellerB);
        var departmentBSale = await CreateSaleAsync(sellerBClient, market.Id, productB.Id);

        var inventoryDeleted = await database.DeleteInventoryAsync(
            company.SchemaName,
            departmentAInventory.Id);
        Assert.True(inventoryDeleted);

        var departmentManager = await database.CreateUserAsync(company, roleName: "DepartmentManager");
        await database.InsertStaffAssignmentAsync(company.SchemaName, departmentManager.Id, market.Id, departmentA.Id);

        using var client = apiFactory.CreateAuthenticatedClient(database, departmentManager);

        var sales = await GetSalesAsync(client);
        Assert.Contains(sales, sale => sale.Id == departmentASale.Id);
        Assert.DoesNotContain(sales, sale => sale.Id == departmentBSale.Id);

        var allowedDetails = await client.GetAsync($"/api/sales/{departmentASale.Id}");
        allowedDetails.EnsureSuccessStatusCode();

        var hiddenDetails = await client.GetAsync($"/api/sales/{departmentBSale.Id}");
        Assert.Equal(HttpStatusCode.NotFound, hiddenDetails.StatusCode);
    }

    [PostgresIntegrationFact]
    public async Task CompanyAdmin_CanReadTenantSales_ButCannotBypassTenantIsolation()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);
        using var apiFactory = new TenantApiFactory(options);

        await database.EnsureRequiredRolesAsync();
        var companyA = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName("sales_tenant_a"),
            name: "sales_tenant_a",
            dropSchemaOnDispose: true);
        var companyB = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName("sales_tenant_b"),
            name: "sales_tenant_b",
            dropSchemaOnDispose: true);

        var marketA1 = await database.InsertMarketAsync(companyA.SchemaName, "Tenant A Market 1");
        var marketA2 = await database.InsertMarketAsync(companyA.SchemaName, "Tenant A Market 2");
        var productA1 = await database.InsertProductAsync(companyA.SchemaName, name: "Tenant A Product 1");
        var productA2 = await database.InsertProductAsync(companyA.SchemaName, name: "Tenant A Product 2");
        await database.InsertInventoryAsync(companyA.SchemaName, productA1.Id, marketA1.Id, quantity: 10);
        await database.InsertInventoryAsync(companyA.SchemaName, productA2.Id, marketA2.Id, quantity: 10);
        var marketB = await database.InsertMarketAsync(companyB.SchemaName, "Tenant B Market");

        var companyAAdmin = await database.CreateUserAsync(companyA, roleName: "CompanyAdmin");
        var companyBAdmin = await database.CreateUserAsync(companyB, roleName: "CompanyAdmin");

        using var companyAClient = apiFactory.CreateAuthenticatedClient(database, companyAAdmin);
        var companyASale1 = await CreateSaleAsync(companyAClient, marketA1.Id, productA1.Id);
        var companyASale2 = await CreateSaleAsync(companyAClient, marketA2.Id, productA2.Id);
        const string companyBReferenceNumber = "TENANT-B-SALE-001";
        await database.InsertSaleWithReferenceNumberAsync(
            companyB.SchemaName,
            marketB.Id,
            companyBAdmin.Id,
            companyBReferenceNumber);

        companyAClient.DefaultRequestHeaders.Add(TenantSchemaHeaderName, companyB.SchemaName);

        var sales = await GetSalesAsync(companyAClient);
        Assert.Contains(sales, sale => sale.Id == companyASale1.Id);
        Assert.Contains(sales, sale => sale.Id == companyASale2.Id);
        Assert.DoesNotContain(sales, sale => sale.ReferenceNumber == companyBReferenceNumber);
    }

    [PostgresIntegrationFact]
    public async Task SalesRead_DeniesRolesWithoutSalesReadPermission()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);
        using var apiFactory = new TenantApiFactory(options);

        await database.EnsureRequiredRolesAsync();
        var company = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName("sales_read_forbidden"),
            name: "sales_read_forbidden",
            dropSchemaOnDispose: true);
        var market = await database.InsertMarketAsync(company.SchemaName, "Market A");
        var seller = await database.CreateUserAsync(company, roleName: "Seller");
        await database.InsertStaffAssignmentAsync(company.SchemaName, seller.Id, market.Id);
        var rootAdmin = await database.CreateUserAsync(company, roleName: "RootAdmin");

        using var sellerClient = apiFactory.CreateAuthenticatedClient(database, seller);
        using var rootAdminClient = apiFactory.CreateAuthenticatedClient(database, rootAdmin);

        var sellerResponse = await sellerClient.GetAsync("/api/sales");
        var rootAdminResponse = await rootAdminClient.GetAsync("/api/sales");

        Assert.Equal(HttpStatusCode.Forbidden, sellerResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, rootAdminResponse.StatusCode);
    }

    private static async Task<IReadOnlyCollection<SaleDto>> GetSalesAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/sales");
        response.EnsureSuccessStatusCode();

        var result = await response.Content
            .ReadFromJsonAsync<ServiceResult<IReadOnlyCollection<SaleDto>>>();

        Assert.NotNull(result?.Data);
        Assert.True(result.Succeeded);

        return result.Data;
    }

    private static async Task<SaleDto> CreateSaleAsync(
        HttpClient client,
        int marketId,
        int productId,
        int quantity = 1,
        decimal unitPrice = 5)
    {
        var response = await client.PostAsJsonAsync("/api/sales", new CreateSaleRequest
        {
            MarketId = marketId,
            PaymentMethod = "Cash",
            TotalAmount = quantity * unitPrice,
            Items =
            [
                new CreateSaleItemRequest
                {
                    ProductId = productId,
                    Quantity = quantity,
                    UnitPrice = unitPrice
                }
            ]
        });
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ServiceResult<SaleDto>>();
        Assert.NotNull(result?.Data);
        Assert.True(result.Succeeded);

        return result.Data;
    }
}
