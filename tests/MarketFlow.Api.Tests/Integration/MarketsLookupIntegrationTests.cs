using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Markets.DTOs;

namespace MarketFlow.Api.Tests.Integration;

public sealed class MarketsLookupIntegrationTests
{
    private const string TenantSchemaHeaderName = "X-Tenant-Schema";
    private const string SchemaNameHeaderName = "X-Schema-Name";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [PostgresIntegrationFact]
    public async Task GetMarkets_ReturnsOnlyActiveMarketsFromAuthenticatedUsersTenant()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);
        using var apiFactory = new TenantApiFactory(options);

        await database.EnsureRequiredRolesAsync();

        var companyA = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName("markets_lookup_a"),
            name: "Markets Lookup A",
            dropSchemaOnDispose: true);
        var companyB = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName("markets_lookup_b"),
            name: "Markets Lookup B",
            dropSchemaOnDispose: true);
        var userA = await database.CreateUserAsync(companyA, roleName: "CompanyAdmin");

        var activeMarket = await database.InsertMarketAsync(
            companyA.SchemaName,
            name: "Tenant A Active Market",
            city: "Prishtina",
            address: "Market Street 1");
        var inactiveMarket = await database.InsertMarketAsync(
            companyA.SchemaName,
            name: "Tenant A Inactive Market",
            isActive: false);
        var otherTenantMarket = await database.InsertMarketAsync(
            companyB.SchemaName,
            name: "Tenant B Active Market");

        using var client = apiFactory.CreateAuthenticatedClient(database, userA);
        client.DefaultRequestHeaders.Add(TenantSchemaHeaderName, companyB.SchemaName);
        client.DefaultRequestHeaders.Add(SchemaNameHeaderName, companyB.SchemaName);

        // These schema hints must be ignored; tenant resolution is derived from userA on the backend.
        var encodedSchemaName = Uri.EscapeDataString(companyB.SchemaName);
        var response = await client.GetAsync($"/api/Markets?schemaName={encodedSchemaName}");
        response.EnsureSuccessStatusCode();
        var responseBody = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<ServiceResult<IReadOnlyCollection<MarketDto>>>(
            responseBody,
            JsonOptions);

        Assert.NotNull(result);
        Assert.True(result.Succeeded);
        var markets = Assert.IsAssignableFrom<IReadOnlyCollection<MarketDto>>(result.Data);
        var market = Assert.Single(markets);
        Assert.Equal(activeMarket.Id, market.Id);
        Assert.Equal(activeMarket.Name, market.Name);
        Assert.Equal("Prishtina", market.City);
        Assert.Equal("Market Street 1", market.Address);
        Assert.True(market.IsActive);
        Assert.DoesNotContain(markets, x => x.Id == inactiveMarket.Id);
        Assert.DoesNotContain(markets, x => x.Id == otherTenantMarket.Id);
        Assert.DoesNotContain(companyA.SchemaName, responseBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(companyB.SchemaName, responseBody, StringComparison.OrdinalIgnoreCase);
    }

    [PostgresIntegrationFact]
    public async Task GetMarkets_AllowsSellerWithInventoryReadPermission()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);
        using var apiFactory = new TenantApiFactory(options);

        await database.EnsureRequiredRolesAsync();

        var company = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName("markets_lookup_seller"),
            name: "Markets Lookup Seller",
            dropSchemaOnDispose: true);
        var seller = await database.CreateUserAsync(company, roleName: "Seller");
        var market = await database.InsertMarketAsync(company.SchemaName, name: "Seller Market");

        using var client = apiFactory.CreateAuthenticatedClient(database, seller);
        var response = await client.GetAsync("/api/Markets");

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ServiceResult<IReadOnlyCollection<MarketDto>>>(
            JsonOptions);

        Assert.NotNull(result);
        var markets = Assert.IsAssignableFrom<IReadOnlyCollection<MarketDto>>(result.Data);
        Assert.Contains(markets, x => x.Id == market.Id && x.Name == market.Name);
    }

    [PostgresIntegrationFact]
    public async Task GetMarkets_WithoutAuthenticationReturnsUnauthorized()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        using var apiFactory = new TenantApiFactory(options);
        using var client = apiFactory.CreateClient();

        var response = await client.GetAsync("/api/Markets");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
