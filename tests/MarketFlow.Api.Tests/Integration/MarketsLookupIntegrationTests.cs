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

    [PostgresIntegrationFact]
    public async Task MarketCrud_UsesAuthenticatedUsersTenantSchema()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);
        using var apiFactory = new TenantApiFactory(options);

        await database.EnsureRequiredRolesAsync();

        var companyA = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName("markets_crud_a"),
            name: "Markets Crud A",
            dropSchemaOnDispose: true);
        var companyB = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName("markets_crud_b"),
            name: "Markets Crud B",
            dropSchemaOnDispose: true);
        var userA = await database.CreateUserAsync(companyA, roleName: "CompanyAdmin");

        using var client = apiFactory.CreateAuthenticatedClient(database, userA);
        client.DefaultRequestHeaders.Add(TenantSchemaHeaderName, companyB.SchemaName);
        client.DefaultRequestHeaders.Add(SchemaNameHeaderName, companyB.SchemaName);

        var createRequest = new CreateMarketRequest
        {
            Name = " Tenant A Crud Market ",
            City = " Prishtina ",
            Address = " Market Street 12 "
        };

        var createResponse = await client.PostAsJsonAsync("/api/Markets", createRequest);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var createResult = await createResponse.Content.ReadFromJsonAsync<ServiceResult<MarketDto>>(JsonOptions);
        Assert.NotNull(createResult);
        Assert.True(createResult.Succeeded);
        Assert.Equal("Tenant A Crud Market", createResult.Data?.Name);
        Assert.Equal("Prishtina", createResult.Data?.City);
        Assert.Equal("Market Street 12", createResult.Data?.Address);
        Assert.True(createResult.Data?.IsActive);

        var marketId = createResult.Data!.Id;
        Assert.Equal(1, await database.CountMarketsByNameAsync(companyA.SchemaName, "Tenant A Crud Market"));
        Assert.Equal(0, await database.CountMarketsByNameAsync(companyB.SchemaName, "Tenant A Crud Market"));

        var getResponse = await client.GetAsync($"/api/Markets/{marketId}");
        getResponse.EnsureSuccessStatusCode();
        var getResult = await getResponse.Content.ReadFromJsonAsync<ServiceResult<MarketDto>>(JsonOptions);
        Assert.Equal(marketId, getResult?.Data?.Id);

        var updateResponse = await client.PutAsJsonAsync(
            $"/api/Markets/{marketId}",
            new UpdateMarketRequest
            {
                Name = "Tenant A Updated Market",
                City = "Peja",
                Address = "Updated Address"
            });
        updateResponse.EnsureSuccessStatusCode();
        var updateResult = await updateResponse.Content.ReadFromJsonAsync<ServiceResult<MarketDto>>(JsonOptions);
        Assert.True(updateResult?.Succeeded);
        Assert.Equal("Tenant A Updated Market", updateResult?.Data?.Name);
        Assert.Equal("Peja", updateResult?.Data?.City);

        var deactivateResponse = await client.PatchAsync($"/api/Markets/{marketId}/deactivate", null);
        deactivateResponse.EnsureSuccessStatusCode();
        var deactivateResult = await deactivateResponse.Content.ReadFromJsonAsync<ServiceResult<MarketDto>>(JsonOptions);
        Assert.False(deactivateResult?.Data?.IsActive);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/Markets/{marketId}")).StatusCode);

        var activateResponse = await client.PatchAsync($"/api/Markets/{marketId}/activate", null);
        activateResponse.EnsureSuccessStatusCode();
        var activateResult = await activateResponse.Content.ReadFromJsonAsync<ServiceResult<MarketDto>>(JsonOptions);
        Assert.True(activateResult?.Data?.IsActive);

        var storedMarket = await database.GetMarketDetailsAsync(companyA.SchemaName, marketId);
        Assert.NotNull(storedMarket);
        Assert.Equal("Tenant A Updated Market", storedMarket.Name);
        Assert.Equal("Peja", storedMarket.City);
        Assert.Equal("Updated Address", storedMarket.Address);
        Assert.True(storedMarket.IsActive);
        Assert.Equal(0, await database.CountMarketsByNameAsync(companyB.SchemaName, "Tenant A Updated Market"));
    }

    [PostgresIntegrationFact]
    public async Task CreateMarket_WithDuplicateNameReturnsConflict()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);
        using var apiFactory = new TenantApiFactory(options);

        await database.EnsureRequiredRolesAsync();

        var company = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName("markets_duplicate"),
            name: "Markets Duplicate",
            dropSchemaOnDispose: true);
        var user = await database.CreateUserAsync(company, roleName: "CompanyAdmin");
        await database.InsertMarketAsync(company.SchemaName, name: "Central Market");

        using var client = apiFactory.CreateAuthenticatedClient(database, user);
        var response = await client.PostAsJsonAsync(
            "/api/Markets",
            new CreateMarketRequest { Name = " central market " });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ServiceResult<MarketDto>>(JsonOptions);
        Assert.False(result?.Succeeded);
        Assert.Equal(ServiceResultFailureType.Conflict, result?.FailureType);
        Assert.Equal("Market name is already used by another market.", result?.Message);
        Assert.Equal(1, await database.CountMarketsByNameAsync(company.SchemaName, "Central Market"));
    }

    [PostgresIntegrationFact]
    public async Task SellerCannotCreateUpdateDeactivateOrActivateMarkets()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);
        using var apiFactory = new TenantApiFactory(options);

        await database.EnsureRequiredRolesAsync();

        var company = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName("markets_seller_forbidden"),
            name: "Markets Seller Forbidden",
            dropSchemaOnDispose: true);
        var seller = await database.CreateUserAsync(company, roleName: "Seller");
        var market = await database.InsertMarketAsync(company.SchemaName, name: "Seller Forbidden Market");

        using var client = apiFactory.CreateAuthenticatedClient(database, seller);

        var createResponse = await client.PostAsJsonAsync(
            "/api/Markets",
            new CreateMarketRequest { Name = "Blocked Market" });
        var updateResponse = await client.PutAsJsonAsync(
            $"/api/Markets/{market.Id}",
            new UpdateMarketRequest { Name = "Blocked Update" });
        var deactivateResponse = await client.PatchAsync($"/api/Markets/{market.Id}/deactivate", null);
        var activateResponse = await client.PatchAsync($"/api/Markets/{market.Id}/activate", null);

        Assert.Equal(HttpStatusCode.Forbidden, createResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, updateResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, deactivateResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, activateResponse.StatusCode);

        var storedMarket = await database.GetMarketDetailsAsync(company.SchemaName, market.Id);
        Assert.NotNull(storedMarket);
        Assert.Equal("Seller Forbidden Market", storedMarket.Name);
        Assert.True(storedMarket.IsActive);
    }
}
