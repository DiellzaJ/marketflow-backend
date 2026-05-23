using MarketFlow.Api.Authorization;
using MarketFlow.Api.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MarketFlow.Api.Tests.Authorization;

public sealed class MarketsAuthorizationPolicyTests
{
    private const string CompanyAdminPermissions = """
        {
            "markets:read": true,
            "markets:create": true,
            "markets:update": true,
            "markets:delete": true,
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

    public static TheoryData<string, string, string, string> MarketPolicies => new()
    {
        { AuthorizationPolicies.ReadMarkets, "markets", "read", CompanyAdminPermissions },
        { AuthorizationPolicies.CreateMarkets, "markets", "create", CompanyAdminPermissions },
        { AuthorizationPolicies.UpdateMarkets, "markets", "update", CompanyAdminPermissions },
        { AuthorizationPolicies.DeleteMarkets, "markets", "delete", CompanyAdminPermissions }
    };

    [Theory]
    [MemberData(nameof(MarketPolicies))]
    public void MarketPolicy_IsRegisteredWithExpectedRequirement(
        string policyName,
        string expectedPermission,
        string expectedAccess,
        string permissions)
    {
        using var serviceProvider = BuildServiceProvider();

        var requirement = GetPolicyRequirement(serviceProvider, policyName);
        var allowed = PermissionEvaluator.TryHasPermission(
            permissions,
            requirement.Permission,
            requirement.Access);

        Assert.Equal(expectedPermission, requirement.Permission);
        Assert.Equal(expectedAccess, requirement.Access);
        Assert.True(allowed);
    }

    [Theory]
    [InlineData(AuthorizationPolicies.ReadMarkets, SellerPermissions, true)]
    [InlineData(AuthorizationPolicies.CreateMarkets, SellerPermissions, false)]
    [InlineData(AuthorizationPolicies.UpdateMarkets, SellerPermissions, false)]
    [InlineData(AuthorizationPolicies.DeleteMarkets, SellerPermissions, false)]
    [InlineData(AuthorizationPolicies.ReadMarkets, InventoryOnlyPermissions, false)]
    public void MarketPolicies_GrantOnlyExpectedPermissions(
        string policyName,
        string permissions,
        bool expectedAllowed)
    {
        using var serviceProvider = BuildServiceProvider();

        var requirement = GetPolicyRequirement(serviceProvider, policyName);
        var allowed = PermissionEvaluator.TryHasPermission(
            permissions,
            requirement.Permission,
            requirement.Access);

        Assert.Equal(expectedAllowed, allowed);
    }

    private static ServiceProvider BuildServiceProvider()
    {
        return new ServiceCollection()
            .AddAuthorization(options => options.AddMarketFlowPolicies())
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
