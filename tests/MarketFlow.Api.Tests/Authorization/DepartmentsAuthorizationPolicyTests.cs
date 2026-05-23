using MarketFlow.Api.Authorization;
using MarketFlow.Api.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MarketFlow.Api.Tests.Authorization;

public sealed class DepartmentsAuthorizationPolicyTests
{
    private const string CompanyAdminPermissions = """
        {
            "departments:read": true,
            "inventory:read": true
        }
        """;

    private const string MarketsOnlyPermissions = """
        {
            "markets:read": true
        }
        """;

    private const string SellerPermissions = """
        {
            "departments:read": true,
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
    public void ReadDepartmentsPolicy_IsRegisteredWithDepartmentsReadRequirement()
    {
        using var serviceProvider = BuildServiceProvider();

        var requirement = GetPolicyRequirement(serviceProvider, AuthorizationPolicies.ReadDepartments);

        Assert.Equal("departments", requirement.Permission);
        Assert.Equal("read", requirement.Access);
    }

    [Theory]
    [InlineData(CompanyAdminPermissions, true)]
    [InlineData(SellerPermissions, true)]
    [InlineData(InventoryOnlyPermissions, false)]
    [InlineData(MarketsOnlyPermissions, false)]
    public void ReadDepartmentsPolicy_GrantsOnlyDepartmentsReadPermission(
        string permissions,
        bool expectedAllowed)
    {
        using var serviceProvider = BuildServiceProvider();

        var requirement = GetPolicyRequirement(serviceProvider, AuthorizationPolicies.ReadDepartments);
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
