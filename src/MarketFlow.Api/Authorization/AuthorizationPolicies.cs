namespace MarketFlow.Api.Authorization;

public static class AuthorizationPolicies
{
    public const string RootAdminOnly = "RootAdminOnly";
    public const string ManageCompanies = "ManageCompanies";

    public const string ReadUsers = "ReadUsers";
    public const string CreateUsers = "CreateUsers";
    public const string UpdateUsers = "UpdateUsers";
    public const string DeleteUsers = "DeleteUsers";

    public const string ReadProducts = "ReadProducts";
    public const string CreateProducts = "CreateProducts";
    public const string UpdateProducts = "UpdateProducts";
    public const string DeleteProducts = "DeleteProducts";

    public const string ReadPurchases = "ReadPurchases";
    public const string CreatePurchases = "CreatePurchases";
    public const string UpdatePurchases = "UpdatePurchases";
    public const string DeletePurchases = "DeletePurchases";

    public const string ReadSales = "ReadSales";
    public const string CreateSales = "CreateSales";
    public const string UpdateSales = "UpdateSales";
    public const string DeleteSales = "DeleteSales";

    public const string CreateInventory = "CreateInventory";
    public const string ReadInventory = "ReadInventory";
    public const string UpdateInventoryStock = "UpdateInventoryStock";
    public const string AdjustInventoryStock = "AdjustInventoryStock";
    public const string DeleteInventory = "DeleteInventory";
    public const string ReadInventoryMovements = "ReadInventoryMovements";
    public const string TransferInventoryStock = "TransferInventoryStock";
}
