using System.Net;
using System.Net.Http.Json;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Dashboard.DTOs;
using MarketFlow.Application.Features.Sales.DTOs;

namespace MarketFlow.Api.Tests.Integration;

public sealed class DashboardSalesSummaryIntegrationTests
{
    private const string TenantSchemaHeaderName = "X-Tenant-Schema";

    [PostgresIntegrationFact]
    public async Task SalesSummary_CalculatesTotalsAndAppliesDateFilters()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);
        using var apiFactory = new TenantApiFactory(options);

        await database.EnsureRequiredRolesAsync();
        var company = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName("dashboard_summary"),
            name: "dashboard_summary",
            dropSchemaOnDispose: true);
        var market = await database.InsertMarketAsync(company.SchemaName, "Market A");
        var product = await database.InsertProductAsync(company.SchemaName, name: "Dashboard Product");
        await database.InsertInventoryAsync(company.SchemaName, product.Id, market.Id, quantity: 20);
        var admin = await database.CreateUserAsync(company, roleName: "CompanyAdmin");

        using var client = apiFactory.CreateAuthenticatedClient(database, admin);

        await CreateSaleAsync(client, market.Id, product.Id, quantity: 2, unitPrice: 15, saleDate: new DateOnly(2026, 5, 20));
        await CreateSaleAsync(client, market.Id, product.Id, quantity: 2, unitPrice: 10, saleDate: new DateOnly(2026, 5, 21));
        await CreateSaleAsync(client, market.Id, product.Id, quantity: 4, unitPrice: 10, saleDate: new DateOnly(2026, 5, 22));

        var allSales = await GetSalesSummaryAsync(client);
        Assert.Equal(90, allSales.TotalRevenue);
        Assert.Equal(3, allSales.TotalSales);
        Assert.Equal(8, allSales.TotalItemsSold);
        Assert.Equal(30, allSales.AverageSaleAmount);

        var filtered = await GetSalesSummaryAsync(client, "from=2026-05-21&to=2026-05-22");
        Assert.Equal(60, filtered.TotalRevenue);
        Assert.Equal(2, filtered.TotalSales);
        Assert.Equal(6, filtered.TotalItemsSold);
        Assert.Equal(30, filtered.AverageSaleAmount);
    }

    [PostgresIntegrationFact]
    public async Task SalesSummary_UsesTenantIsolationEvenWhenSchemaHeaderTargetsAnotherTenant()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);
        using var apiFactory = new TenantApiFactory(options);

        await database.EnsureRequiredRolesAsync();
        var companyA = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName("dashboard_tenant_a"),
            name: "dashboard_tenant_a",
            dropSchemaOnDispose: true);
        var companyB = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName("dashboard_tenant_b"),
            name: "dashboard_tenant_b",
            dropSchemaOnDispose: true);
        var marketA = await database.InsertMarketAsync(companyA.SchemaName, "Tenant A Market");
        var marketB = await database.InsertMarketAsync(companyB.SchemaName, "Tenant B Market");
        var productA = await database.InsertProductAsync(companyA.SchemaName, name: "Tenant A Product");
        await database.InsertInventoryAsync(companyA.SchemaName, productA.Id, marketA.Id, quantity: 10);
        var adminA = await database.CreateUserAsync(companyA, roleName: "CompanyAdmin");
        var adminB = await database.CreateUserAsync(companyB, roleName: "CompanyAdmin");

        using var client = apiFactory.CreateAuthenticatedClient(database, adminA);
        await CreateSaleAsync(client, marketA.Id, productA.Id, quantity: 2, unitPrice: 15);
        await database.InsertSaleWithReferenceNumberAsync(
            companyB.SchemaName,
            marketB.Id,
            adminB.Id,
            "DASHBOARD-TENANT-B-SALE",
            totalAmount: 999);

        client.DefaultRequestHeaders.Add(TenantSchemaHeaderName, companyB.SchemaName);

        var summary = await GetSalesSummaryAsync(client);
        Assert.Equal(30, summary.TotalRevenue);
        Assert.Equal(1, summary.TotalSales);
    }

    [PostgresIntegrationFact]
    public async Task SalesSummary_RespectsDepartmentAssignmentScope()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);
        using var apiFactory = new TenantApiFactory(options);

        await database.EnsureRequiredRolesAsync();
        var company = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName("dashboard_department_scope"),
            name: "dashboard_department_scope",
            dropSchemaOnDispose: true);
        var market = await database.InsertMarketAsync(company.SchemaName, "Market A");
        var departmentA = await database.InsertDepartmentAsync(company.SchemaName, market.Id, "Produce");
        var departmentB = await database.InsertDepartmentAsync(company.SchemaName, market.Id, "Dairy");
        var productA = await database.InsertProductAsync(company.SchemaName, name: "Produce Sale Product");
        var productB = await database.InsertProductAsync(company.SchemaName, name: "Dairy Sale Product");
        await database.InsertInventoryAsync(company.SchemaName, productA.Id, market.Id, departmentA.Id, quantity: 10);
        await database.InsertInventoryAsync(company.SchemaName, productB.Id, market.Id, departmentB.Id, quantity: 10);

        var sellerA = await database.CreateUserAsync(company, roleName: "Seller");
        await database.InsertStaffAssignmentAsync(company.SchemaName, sellerA.Id, market.Id, departmentA.Id);
        using var sellerAClient = apiFactory.CreateAuthenticatedClient(database, sellerA);
        await CreateSaleAsync(sellerAClient, market.Id, productA.Id, quantity: 2, unitPrice: 15);

        var sellerB = await database.CreateUserAsync(company, roleName: "Seller");
        await database.InsertStaffAssignmentAsync(company.SchemaName, sellerB.Id, market.Id, departmentB.Id);
        using var sellerBClient = apiFactory.CreateAuthenticatedClient(database, sellerB);
        await CreateSaleAsync(sellerBClient, market.Id, productB.Id, quantity: 1, unitPrice: 50);

        var departmentManager = await database.CreateUserAsync(company, roleName: "DepartmentManager");
        await database.InsertStaffAssignmentAsync(company.SchemaName, departmentManager.Id, market.Id, departmentA.Id);
        using var client = apiFactory.CreateAuthenticatedClient(database, departmentManager);

        var summary = await GetSalesSummaryAsync(client);
        Assert.Equal(30, summary.TotalRevenue);
        Assert.Equal(1, summary.TotalSales);
        Assert.Equal(2, summary.TotalItemsSold);
    }

    [PostgresIntegrationFact]
    public async Task SalesSummary_DeniesUnauthorizedAndRolesWithoutSalesRead()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);
        using var apiFactory = new TenantApiFactory(options);

        await database.EnsureRequiredRolesAsync();
        var company = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName("dashboard_forbidden"),
            name: "dashboard_forbidden",
            dropSchemaOnDispose: true);
        var market = await database.InsertMarketAsync(company.SchemaName, "Market A");
        var seller = await database.CreateUserAsync(company, roleName: "Seller");
        await database.InsertStaffAssignmentAsync(company.SchemaName, seller.Id, market.Id);

        using var anonymousClient = apiFactory.CreateClient();
        using var sellerClient = apiFactory.CreateAuthenticatedClient(database, seller);

        var unauthorized = await anonymousClient.GetAsync("/api/dashboard/sales-summary");
        var forbidden = await sellerClient.GetAsync("/api/dashboard/sales-summary");

        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    private static async Task<SalesSummaryDto> GetSalesSummaryAsync(
        HttpClient client,
        string queryString = "")
    {
        var path = string.IsNullOrWhiteSpace(queryString)
            ? "/api/dashboard/sales-summary"
            : $"/api/dashboard/sales-summary?{queryString}";
        var response = await client.GetAsync(path);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ServiceResult<SalesSummaryDto>>();

        Assert.NotNull(result?.Data);
        Assert.True(result.Succeeded);

        return result.Data;
    }

    private static async Task CreateSaleAsync(
        HttpClient client,
        int marketId,
        int productId,
        int quantity = 1,
        decimal unitPrice = 5,
        DateOnly? saleDate = null)
    {
        var response = await client.PostAsJsonAsync("/api/sales", new CreateSaleRequest
        {
            MarketId = marketId,
            SaleDate = saleDate,
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
    }
}
