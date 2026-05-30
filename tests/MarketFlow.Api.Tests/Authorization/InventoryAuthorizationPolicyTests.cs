using MarketFlow.Api.Authorization;
using MarketFlow.Api.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MarketFlow.Api.Tests.Authorization;

public sealed class InventoryAuthorizationPolicyTests
{
    private const string CompanyAdminPermissions = """
        {
            "inventory:create": true,
            "inventory:read": true,
            "stock:update": true,
            "stock:adjust": true,
            "inventory:delete": true,
            "inventory-movements:read": true,
            "stock:transfer": true
        }
        """;

    private const string InventoryEmployeePermissions = """
        {
            "products:read": true,
            "inventory:read": true,
            "stock:update": true,
            "stock:adjust": true,
            "inventory-movements:read": true
        }
        """;

    private const string SellerPermissions = """
        {
            "products:read": true,
            "sales:create": true,
            "inventory:read": true
        }
        """;

    public static TheoryData<string, string, string?> RegisteredInventoryPolicies => new()
    {
        { AuthorizationPolicies.ReadInventory, "inventory", "read" },
        { AuthorizationPolicies.CreateInventory, "inventory", "create" },
        { AuthorizationPolicies.UpdateInventory, "stock", "update" },
        { AuthorizationPolicies.AdjustInventory, "stock", "adjust" },
        { AuthorizationPolicies.DeleteInventory, "inventory", "delete" },
        { AuthorizationPolicies.ReadInventoryMovements, "inventory-movements", "read" },
        { AuthorizationPolicies.TransferInventory, "stock", "transfer" }
    };

    public static TheoryData<string, string, bool> InventoryRolePolicyAccess => new()
    {
        { CompanyAdminPermissions, AuthorizationPolicies.ReadInventory, true },
        { CompanyAdminPermissions, AuthorizationPolicies.CreateInventory, true },
        { CompanyAdminPermissions, AuthorizationPolicies.UpdateInventory, true },
        { CompanyAdminPermissions, AuthorizationPolicies.AdjustInventory, true },
        { CompanyAdminPermissions, AuthorizationPolicies.DeleteInventory, true },
        { CompanyAdminPermissions, AuthorizationPolicies.ReadInventoryMovements, true },
        { CompanyAdminPermissions, AuthorizationPolicies.TransferInventory, true },

        { InventoryEmployeePermissions, AuthorizationPolicies.ReadInventory, true },
        { InventoryEmployeePermissions, AuthorizationPolicies.CreateInventory, false },
        { InventoryEmployeePermissions, AuthorizationPolicies.UpdateInventory, true },
        { InventoryEmployeePermissions, AuthorizationPolicies.AdjustInventory, true },
        { InventoryEmployeePermissions, AuthorizationPolicies.DeleteInventory, false },
        { InventoryEmployeePermissions, AuthorizationPolicies.ReadInventoryMovements, true },
        { InventoryEmployeePermissions, AuthorizationPolicies.TransferInventory, false },

        { SellerPermissions, AuthorizationPolicies.ReadInventory, true },
        { SellerPermissions, AuthorizationPolicies.CreateInventory, false },
        { SellerPermissions, AuthorizationPolicies.UpdateInventory, false },
        { SellerPermissions, AuthorizationPolicies.AdjustInventory, false },
        { SellerPermissions, AuthorizationPolicies.DeleteInventory, false },
        { SellerPermissions, AuthorizationPolicies.ReadInventoryMovements, false },
        { SellerPermissions, AuthorizationPolicies.TransferInventory, false }
    };

    [Theory]
    [MemberData(nameof(RegisteredInventoryPolicies))]
    public void InventoryPolicy_IsRegisteredWithExpectedPermissionRequirement(
        string policyName,
        string expectedPermission,
        string? expectedAccess)
    {
        using var serviceProvider = BuildServiceProvider();

        var requirement = GetPolicyRequirement(serviceProvider, policyName);

        Assert.Equal(expectedPermission, requirement.Permission);
        Assert.Equal(expectedAccess, requirement.Access);
    }

    [Theory]
    [MemberData(nameof(InventoryRolePolicyAccess))]
    public void InventoryPolicy_GrantsExpectedRoleAccess(
        string permissions,
        string policyName,
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
