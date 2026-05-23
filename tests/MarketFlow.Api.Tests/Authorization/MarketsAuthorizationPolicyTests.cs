using MarketFlow.Api.Authorization;
using MarketFlow.Api.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MarketFlow.Api.Tests.Authorization;

public sealed class MarketsAuthorizationPolicyTests
{
    private const string CompanyAdminPermissions = """
        {
            "markets:read": true,
            "inventory:read": true
        }
        """;

    private const string SellerPermissions = """
        {
            "markets:read": true,
            "products:read": true,
            "sales:create": true,
            "inventory:read": true
        }
        """;

    private const string InventoryOnlyPermissions = """
        {
            "inventory:read": true
        }
        """;

    [Fact]
    public void ReadMarketsPolicy_IsRegisteredWithMarketsReadRequirement()
    {
        using var serviceProvider = BuildServiceProvider();

        var requirement = GetPolicyRequirement(serviceProvider, AuthorizationPolicies.ReadMarkets);

        Assert.Equal("markets", requirement.Permission);
        Assert.Equal("read", requirement.Access);
    }

    [Theory]
    [InlineData(CompanyAdminPermissions, true)]
    [InlineData(SellerPermissions, true)]
    [InlineData(InventoryOnlyPermissions, false)]
    public void ReadMarketsPolicy_GrantsOnlyMarketsReadPermission(
        string permissions,
        bool expectedAllowed)
    {
        using var serviceProvider = BuildServiceProvider();

        var requirement = GetPolicyRequirement(serviceProvider, AuthorizationPolicies.ReadMarkets);
        var allowed = PermissionEvaluator.TryHasPermission(
            permissions,
            requirement.Permission,
            requirement.Access);

        Assert.Equal(expectedAllowed, allowed);
    }

    private static ServiceProvider BuildServiceProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=marketflow;Username=test;Password=test",
                ["Jwt:Issuer"] = "marketflow-tests",
                ["Jwt:Audience"] = "marketflow-tests",
                ["Jwt:Secret"] = "marketflow-tests-secret-with-enough-length"
            })
            .Build();

        return new ServiceCollection()
            .AddApiServices(configuration)
            .BuildServiceProvider();
    }

    private static PermissionRequirement GetPolicyRequirement(
        IServiceProvider serviceProvider,
        string policyName)
    {
        var authorizationOptions = serviceProvider.GetRequiredService<IOptions<AuthorizationOptions>>().Value;
        var policy = authorizationOptions.GetPolicy(policyName);

        Assert.NotNull(policy);

        return Assert.Single(policy.Requirements.OfType<PermissionRequirement>());
    }
}
