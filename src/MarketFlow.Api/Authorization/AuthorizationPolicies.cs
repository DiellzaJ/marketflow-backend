namespace MarketFlow.Api.Authorization;

public static class AuthorizationPolicies
{
    public const string RootAdminOnly = "RootAdminOnly";
    public const string ManageCompanies = "ManageCompanies";
    public const string ManageUsers = "ManageUsers";
    public const string ManageProducts = "ManageProducts";
    public const string ManagePurchases = "ManagePurchases";
    public const string ManageSales = "ManageSales";
    public const string ManageInventory = "ManageInventory";
    public const string ReadInventory = "ReadInventory";
}
