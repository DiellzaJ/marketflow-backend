using MarketFlow.Api.Authorization;
using MarketFlow.Api.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MarketFlow.Api.Tests.Authorization;

public sealed class UsersAuthorizationPolicyTests
{
    public static TheoryData<string, string, string[]> UserPolicies => new()
    {
        { AuthorizationPolicies.ReadUsers, "read", ["RootAdmin", "CompanyAdmin", "MainOperator"] },
        { AuthorizationPolicies.CreateUsers, "create", ["RootAdmin", "CompanyAdmin"] },
        { AuthorizationPolicies.UpdateUsers, "update", ["RootAdmin", "CompanyAdmin", "MainOperator"] },
        { AuthorizationPolicies.DeleteUsers, "delete", ["RootAdmin", "CompanyAdmin", "MainOperator"] }
    };

    [Theory]
    [MemberData(nameof(UserPolicies))]
    public void UserPolicy_RequiresExpectedPermissionAndManagementRoles(
        string policyName,
        string expectedAccess,
        string[] expectedRoles)
    {
        using var serviceProvider = new ServiceCollection()
            .AddAuthorization(options => options.AddMarketFlowPolicies())
            .BuildServiceProvider();

        var authorizationOptions = serviceProvider.GetRequiredService<IOptions<AuthorizationOptions>>().Value;
        var policy = authorizationOptions.GetPolicy(policyName);

        Assert.NotNull(policy);

        var permissionRequirement = Assert.Single(policy.Requirements.OfType<PermissionRequirement>());
        var rolesRequirement = Assert.Single(policy.Requirements.OfType<RolesAuthorizationRequirement>());

        Assert.Equal("users", permissionRequirement.Permission);
        Assert.Equal(expectedAccess, permissionRequirement.Access);
        Assert.Equal(expectedRoles.Length, rolesRequirement.AllowedRoles.Count());

        foreach (var expectedRole in expectedRoles)
        {
            Assert.Contains(expectedRole, rolesRequirement.AllowedRoles);
        }
    }
}
