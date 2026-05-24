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
    public const string UpdateInventory = "UpdateInventory";
    public const string AdjustInventory = "AdjustInventory";
    public const string DeleteInventory = "DeleteInventory";
    public const string ReadInventoryMovements = "ReadInventoryMovements";
    public const string TransferInventory = "TransferInventory";

    public const string ReadMarkets = "ReadMarkets";
    public const string CreateMarkets = "CreateMarkets";
    public const string UpdateMarkets = "UpdateMarkets";
    public const string DeleteMarkets = "DeleteMarkets";

    public const string ReadDepartments = "ReadDepartments";
    public const string CreateDepartments = "CreateDepartments";
    public const string UpdateDepartments = "UpdateDepartments";
    public const string DeleteDepartments = "DeleteDepartments";

    public const string UpdateInventoryStock = UpdateInventory;
    public const string AdjustInventoryStock = AdjustInventory;
    public const string TransferInventoryStock = TransferInventory;
}
