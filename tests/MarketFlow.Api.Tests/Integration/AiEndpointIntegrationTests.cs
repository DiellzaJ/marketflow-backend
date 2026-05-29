using System.Net;
using System.Net.Http.Json;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.AI.DTOs;

namespace MarketFlow.Api.Tests.Integration;

public sealed class AiEndpointIntegrationTests
{
    private const string TenantSchemaHeaderName = "X-Tenant-Schema";

    [PostgresIntegrationFact]
    public async Task InventoryForecast_ReturnsOnlyCurrentTenantProducts_WhenSchemaOverrideTargetsAnotherTenant()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);
        using var apiFactory = new TenantApiFactory(options);

        await database.EnsureRequiredRolesAsync();
        var companyA = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName("ai_tenant_a"),
            name: "AI Tenant A",
            dropSchemaOnDispose: true);
        var companyB = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName("ai_tenant_b"),
            name: "AI Tenant B",
            dropSchemaOnDispose: true);
        var marketA = await database.InsertMarketAsync(companyA.SchemaName, "AI Tenant A Market");
        var marketB = await database.InsertMarketAsync(companyB.SchemaName, "AI Tenant B Market");
        var productA = await database.InsertProductAsync(companyA.SchemaName, name: "AI Tenant A Coffee");
        var productB = await database.InsertProductAsync(companyB.SchemaName, name: "AI Tenant B Tea");
        await database.InsertInventoryAsync(companyA.SchemaName, productA.Id, marketA.Id, quantity: 12);
        await database.InsertInventoryAsync(companyB.SchemaName, productB.Id, marketB.Id, quantity: 99);
        var adminA = await database.CreateUserAsync(companyA, roleName: "CompanyAdmin");

        using var client = apiFactory.CreateAuthenticatedClient(database, adminA);
        client.DefaultRequestHeaders.Add(TenantSchemaHeaderName, companyB.SchemaName);

        var response = await client.PostAsJsonAsync("/api/ai/inventory-forecast", new AiInventoryForecastRequest
        {
            SalesHistoryDays = 7,
            ForecastDays = 14
        });
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ServiceResult<AiInventoryForecastResponse>>();
        Assert.True(result?.Succeeded);
        var forecast = Assert.IsType<AiInventoryForecastResponse>(result!.Data);
        var product = Assert.Single(forecast.Products);
        Assert.Equal(productA.Id, product.ProductId);
        Assert.Equal("AI Tenant A Coffee", product.ProductName);
        Assert.Equal(12, product.CurrentStock);
        Assert.DoesNotContain(forecast.Products, item => item.ProductName == "AI Tenant B Tea");
    }

    [PostgresIntegrationFact]
    public async Task InventoryForecast_DeniesAnonymousAndUnauthorizedRoles()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);
        using var apiFactory = new TenantApiFactory(options);

        await database.EnsureRequiredRolesAsync();
        var company = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName("ai_auth"),
            name: "AI Auth",
            dropSchemaOnDispose: true);
        var seller = await database.CreateUserAsync(company, roleName: "Seller");

        using var anonymousClient = apiFactory.CreateClient();
        using var sellerClient = apiFactory.CreateAuthenticatedClient(database, seller);
        var request = new AiInventoryForecastRequest
        {
            SalesHistoryDays = 7,
            ForecastDays = 14
        };

        var unauthorized = await anonymousClient.PostAsJsonAsync("/api/ai/inventory-forecast", request);
        var forbidden = await sellerClient.PostAsJsonAsync("/api/ai/inventory-forecast", request);

        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }
}
