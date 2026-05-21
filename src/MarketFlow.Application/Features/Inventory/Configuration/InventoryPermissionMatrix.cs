namespace MarketFlow.Application.Features.Inventory.Configuration;

public static class InventoryPermissionMatrix
{
    public const string ViewInventory = "inventory:read";
    public const string CreateInventoryRecords = "inventory:create";
    public const string UpdateStock = "stock:update";
    public const string AdjustStock = "stock:adjust";
    public const string DeleteInventoryRecords = "inventory:delete";
    public const string ViewInventoryMovements = "inventory-movements:read";
    public const string TransferStock = "stock:transfer";

    public const string NoTenantInventoryAccess = "No tenant inventory access";
    public const string CompanyInventory = "All company inventory";
    public const string AssignedMarketInventory = "Assigned market inventory";
    public const string AssignedDepartmentInventory = "Assigned department inventory";
    public const string AssignedMarketOrDepartmentInventory = "Assigned market or department inventory";
    public const string PosAvailabilityOnly = "Assigned POS market availability only";

    public static readonly IReadOnlyDictionary<string, InventoryRolePermissions> Roles =
        new Dictionary<string, InventoryRolePermissions>(StringComparer.OrdinalIgnoreCase)
        {
            ["RootAdmin"] = new(
                Scope: NoTenantInventoryAccess,
                AllowedPermissions: []),
            ["CompanyAdmin"] = new(
                Scope: CompanyInventory,
                AllowedPermissions:
                [
                    ViewInventory,
                    CreateInventoryRecords,
                    UpdateStock,
                    AdjustStock,
                    DeleteInventoryRecords,
                    ViewInventoryMovements,
                    TransferStock
                ]),
            ["MainOperator"] = new(
                Scope: AssignedMarketInventory,
                AllowedPermissions:
                [
                    ViewInventory,
                    CreateInventoryRecords,
                    UpdateStock,
                    AdjustStock,
                    DeleteInventoryRecords,
                    ViewInventoryMovements,
                    TransferStock
                ]),
            ["DepartmentManager"] = new(
                Scope: AssignedDepartmentInventory,
                AllowedPermissions:
                [
                    ViewInventory,
                    UpdateStock,
                    AdjustStock,
                    ViewInventoryMovements,
                    TransferStock
                ]),
            ["InventoryEmployee"] = new(
                Scope: AssignedMarketOrDepartmentInventory,
                AllowedPermissions:
                [
                    ViewInventory,
                    UpdateStock,
                    AdjustStock,
                    ViewInventoryMovements
                ]),
            ["Seller"] = new(
                Scope: PosAvailabilityOnly,
                AllowedPermissions:
                [
                    ViewInventory
                ])
        };

    public static bool Can(string roleName, string permission)
    {
        return Roles.TryGetValue(roleName, out var rolePermissions) &&
            rolePermissions.AllowedPermissions.Contains(permission, StringComparer.OrdinalIgnoreCase);
    }
}

public sealed record InventoryRolePermissions(
    string Scope,
    IReadOnlyCollection<string> AllowedPermissions);
