using MarketFlow.Api.Authorization;

namespace MarketFlow.Api.Tests.Authorization;

public sealed class PermissionEvaluatorTests
{
    private const string RootAdminPermissions = """{"all": true}""";

    private const string CompanyAdminPermissions = """
        {
            "users:read": true,
            "users:create": true,
            "users:update": true,
            "users:delete": true,
            "products:read": true,
            "products:create": true,
            "products:update": true,
            "products:delete": true,
            "sales:read": true,
            "sales:create": true,
            "sales:update": true,
            "sales:delete": true,
            "inventory:create": true,
            "inventory:read": true,
            "inventory:update": true,
            "inventory:delete": true,
            "purchases:read": true,
            "purchases:create": true,
            "purchases:update": true,
            "purchases:delete": true
        }
        """;

    private const string MainOperatorPermissions = """
        {
            "products:read": true,
            "products:create": true,
            "products:update": true,
            "products:delete": true,
            "sales:read": true,
            "sales:create": true,
            "sales:update": true,
            "sales:delete": true,
            "inventory:create": true,
            "inventory:read": true,
            "inventory:update": true,
            "inventory:delete": true,
            "purchases:read": true,
            "purchases:create": true,
            "purchases:update": true,
            "purchases:delete": true
        }
        """;

    private const string SellerPermissions = """
        {
            "products:read": true,
            "sales:create": true,
            "inventory:read": true,
            "inventory:update": true
        }
        """;

    public static TheoryData<string, string, string?> AllowedPermissions => new()
    {
        { RootAdminPermissions, "users", "delete" },
        { RootAdminPermissions, "companies", "delete" },
        { CompanyAdminPermissions, "users", "create" },
        { CompanyAdminPermissions, "products", "delete" },
        { MainOperatorPermissions, "products", "update" },
        { MainOperatorPermissions, "purchases", "create" },
        { MainOperatorPermissions, "purchases", "delete" },
        { MainOperatorPermissions, "sales", "delete" },
        { MainOperatorPermissions, "inventory", "create" },
        { SellerPermissions, "products", "read" },
        { SellerPermissions, "sales", "create" },
        { SellerPermissions, "inventory", "update" },
        { SellerPermissions, "sales:create", null }
    };

    public static TheoryData<string, string, string?> ForbiddenPermissions => new()
    {
        { MainOperatorPermissions, "users", "read" },
        { MainOperatorPermissions, "users", "create" },
        { SellerPermissions, "users", "read" },
        { SellerPermissions, "sales", "read" },
        { SellerPermissions, "sales", "delete" },
        { SellerPermissions, "products", "create" },
        { SellerPermissions, "products", "update" },
        { SellerPermissions, "products", "delete" },
        { SellerPermissions, "purchases", "create" },
        { SellerPermissions, "purchases", "delete" },
        { SellerPermissions, "inventory", "create" },
        { SellerPermissions, "inventory", "delete" }
    };

    [Theory]
    [MemberData(nameof(AllowedPermissions))]
    public void TryHasPermission_AllowsExpectedRoleActions(
        string permissions,
        string permission,
        string? access)
    {
        var allowed = PermissionEvaluator.TryHasPermission(permissions, permission, access);

        Assert.True(allowed);
    }

    [Theory]
    [MemberData(nameof(ForbiddenPermissions))]
    public void TryHasPermission_DeniesExpectedRoleActions(
        string permissions,
        string permission,
        string? access)
    {
        var allowed = PermissionEvaluator.TryHasPermission(permissions, permission, access);

        Assert.False(allowed);
    }

    [Theory]
    [InlineData("""{"inventory": "read-update"}""", "inventory", "read")]
    [InlineData("""{"inventory": "read-update"}""", "inventory", "update")]
    [InlineData("""{"sales": true}""", "sales", "create")]
    public void TryHasPermission_KeepsLegacyPermissionShapesWorking(
        string permissions,
        string permission,
        string access)
    {
        var allowed = PermissionEvaluator.TryHasPermission(permissions, permission, access);

        Assert.True(allowed);
    }
}
