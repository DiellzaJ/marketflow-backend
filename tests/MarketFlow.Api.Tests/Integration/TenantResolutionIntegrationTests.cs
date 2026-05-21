namespace MarketFlow.Api.Tests.Integration;

public sealed class TenantResolutionIntegrationTests
{
    [Fact]
    public async Task ProductsEndpoint_WithStaleSchemaClaim_UsesDatabaseResolvedTenantSchema()
    {
        if (!TenantIntegrationTestDatabase.HasConfiguredConnectionString)
        {
            return;
        }

        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);
        using var apiFactory = new TenantApiFactory(options);

        await database.EnsureRequiredRolesAsync();

        var companyA = await database.CreateCompanyAsync(dropSchemaOnDispose: true);
        var companyB = await database.CreateCompanyAsync(dropSchemaOnDispose: true);
        var userA = await database.CreateUserAsync(companyA, roleName: "CompanyAdmin");

        await database.InsertProductAsync(
            companyA.SchemaName,
            name: "Company A Product",
            barcode: "COMPANY-A-001");
        await database.InsertProductAsync(
            companyB.SchemaName,
            name: "Company B Product",
            barcode: "COMPANY-B-001");

        using var client = apiFactory.CreateAuthenticatedClient(
            database,
            userA with { SchemaName = companyB.SchemaName });

        var response = await client.GetAsync("/api/products");
        var responseBody = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        Assert.Contains("Company A Product", responseBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Company B Product", responseBody, StringComparison.Ordinal);
        Assert.DoesNotContain(companyA.SchemaName, responseBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(companyB.SchemaName, responseBody, StringComparison.OrdinalIgnoreCase);
    }
}
