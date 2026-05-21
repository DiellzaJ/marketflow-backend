using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;

namespace MarketFlow.Api.Tests.Integration;

public sealed class TenantIntegrationTestUtilitiesTests
{
    [Fact]
    public void CreateUniqueSchemaName_ReturnsValidDistinctSchemaNames()
    {
        var database = new TenantIntegrationTestDatabase(new TenantIntegrationTestOptions
        {
            ConnectionString = "Host=test-host;Database=test-db;Username=test-user"
        });

        var firstSchemaName = database.CreateUniqueSchemaName("Tenant-Isolation");
        var secondSchemaName = database.CreateUniqueSchemaName("Tenant-Isolation");
        var longPrefixSchemaName = database.CreateUniqueSchemaName(
            "tenant_isolation_schema_name_with_a_very_long_prefix_that_must_be_truncated");

        Assert.NotEqual(firstSchemaName, secondSchemaName);
        Assert.Matches("^[a-z][a-z0-9_]{0,62}$", firstSchemaName);
        Assert.Matches("^[a-z][a-z0-9_]{0,62}$", secondSchemaName);
        Assert.Matches("^[a-z][a-z0-9_]*_[a-f0-9]{16}$", longPrefixSchemaName);
        Assert.True(longPrefixSchemaName.Length <= 63);
    }

    [Fact]
    public void GenerateAccessToken_IncludesTenantClaims()
    {
        var database = new TenantIntegrationTestDatabase(new TenantIntegrationTestOptions
        {
            ConnectionString = "Host=test-host;Database=test-db;Username=test-user",
            JwtIssuer = "MarketFlow.Tests",
            JwtAudience = "MarketFlow.Tests",
            JwtSecret = "MarketFlow integration tests use this deterministic signing key."
        });
        var user = new TenantTestUser(
            Id: 7,
            CompanyId: 11,
            SchemaName: "tenant_alpha",
            FullName: "Tenant User",
            Email: "tenant.user@marketflow.test",
            RoleName: "CompanyAdmin",
            IsActive: true);

        var token = database.GenerateAccessToken(user);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal("7", jwt.Claims.Single(x => x.Type == ClaimTypes.NameIdentifier).Value);
        Assert.Equal("11", jwt.Claims.Single(x => x.Type == "company_id").Value);
        Assert.Equal("tenant_alpha", jwt.Claims.Single(x => x.Type == "schema_name").Value);
        Assert.Equal("CompanyAdmin", jwt.Claims.Single(x => x.Type == ClaimTypes.Role).Value);
    }

    [Fact]
    public void AttachBearerToken_AddsAuthorizationHeader()
    {
        using var client = new HttpClient();

        TenantApiFactory.AttachBearerToken(client, "test-token");

        Assert.Equal(
            new AuthenticationHeaderValue("Bearer", "test-token"),
            client.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public async Task TenantDatabaseUtilities_WhenConfigured_CreateTenantDataAndCleanUp()
    {
        if (!TenantIntegrationTestDatabase.HasConfiguredConnectionString)
        {
            return;
        }

        var options = TenantIntegrationTestOptions.FromEnvironment();
        var schemaName = string.Empty;

        await using (var database = new TenantIntegrationTestDatabase(options))
        {
            await database.EnsureRequiredRolesAsync();
            schemaName = database.CreateUniqueSchemaName("tenant_utilities");

            var company = await database.CreateCompanyAsync(
                schemaName,
                dropSchemaOnDispose: true);
            var user = await database.CreateUserAsync(company, roleName: "CompanyAdmin");
            var category = await database.InsertCategoryAsync(company.SchemaName, "Utilities Category");
            var product = await database.InsertProductAsync(
                company.SchemaName,
                category.Id,
                "Utilities Product",
                "UTILITIES-001");

            Assert.True(await database.SchemaExistsAsync(company.SchemaName));
            Assert.True(await database.TableExistsAsync(company.SchemaName, "products"));
            Assert.Equal(1, await database.CountRowsAsync(company.SchemaName, "products"));
            Assert.Equal(company.Id, user.CompanyId);
            Assert.Equal(category.Id, product.CategoryId);
            Assert.False(string.IsNullOrWhiteSpace(database.GenerateAccessToken(user)));
        }

        await using var verificationDatabase = new TenantIntegrationTestDatabase(options);
        Assert.False(await verificationDatabase.SchemaExistsAsync(schemaName));
    }
}
