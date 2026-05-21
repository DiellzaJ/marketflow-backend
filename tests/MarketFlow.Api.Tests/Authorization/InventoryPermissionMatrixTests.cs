using MarketFlow.Application.Features.Inventory.Configuration;

namespace MarketFlow.Api.Tests.Authorization;

public sealed class InventoryPermissionMatrixTests
{
    public static TheoryData<string, string, bool> RolePermissions => new()
    {
        { "RootAdmin", InventoryPermissionMatrix.ViewInventory, false },
        { "RootAdmin", InventoryPermissionMatrix.CreateInventoryRecords, false },
        { "RootAdmin", InventoryPermissionMatrix.UpdateStock, false },
        { "RootAdmin", InventoryPermissionMatrix.AdjustStock, false },
        { "RootAdmin", InventoryPermissionMatrix.DeleteInventoryRecords, false },
        { "RootAdmin", InventoryPermissionMatrix.ViewInventoryMovements, false },
        { "RootAdmin", InventoryPermissionMatrix.TransferStock, false },

        { "CompanyAdmin", InventoryPermissionMatrix.ViewInventory, true },
        { "CompanyAdmin", InventoryPermissionMatrix.CreateInventoryRecords, true },
        { "CompanyAdmin", InventoryPermissionMatrix.UpdateStock, true },
        { "CompanyAdmin", InventoryPermissionMatrix.AdjustStock, true },
        { "CompanyAdmin", InventoryPermissionMatrix.DeleteInventoryRecords, true },
        { "CompanyAdmin", InventoryPermissionMatrix.ViewInventoryMovements, true },
        { "CompanyAdmin", InventoryPermissionMatrix.TransferStock, true },

        { "MainOperator", InventoryPermissionMatrix.ViewInventory, true },
        { "MainOperator", InventoryPermissionMatrix.CreateInventoryRecords, true },
        { "MainOperator", InventoryPermissionMatrix.UpdateStock, true },
        { "MainOperator", InventoryPermissionMatrix.AdjustStock, true },
        { "MainOperator", InventoryPermissionMatrix.DeleteInventoryRecords, true },
        { "MainOperator", InventoryPermissionMatrix.ViewInventoryMovements, true },
        { "MainOperator", InventoryPermissionMatrix.TransferStock, true },

        { "DepartmentManager", InventoryPermissionMatrix.ViewInventory, true },
        { "DepartmentManager", InventoryPermissionMatrix.CreateInventoryRecords, false },
        { "DepartmentManager", InventoryPermissionMatrix.UpdateStock, true },
        { "DepartmentManager", InventoryPermissionMatrix.AdjustStock, true },
        { "DepartmentManager", InventoryPermissionMatrix.DeleteInventoryRecords, false },
        { "DepartmentManager", InventoryPermissionMatrix.ViewInventoryMovements, true },
        { "DepartmentManager", InventoryPermissionMatrix.TransferStock, true },

        { "InventoryEmployee", InventoryPermissionMatrix.ViewInventory, true },
        { "InventoryEmployee", InventoryPermissionMatrix.CreateInventoryRecords, false },
        { "InventoryEmployee", InventoryPermissionMatrix.UpdateStock, true },
        { "InventoryEmployee", InventoryPermissionMatrix.AdjustStock, true },
        { "InventoryEmployee", InventoryPermissionMatrix.DeleteInventoryRecords, false },
        { "InventoryEmployee", InventoryPermissionMatrix.ViewInventoryMovements, true },
        { "InventoryEmployee", InventoryPermissionMatrix.TransferStock, false },

        { "Seller", InventoryPermissionMatrix.ViewInventory, true },
        { "Seller", InventoryPermissionMatrix.CreateInventoryRecords, false },
        { "Seller", InventoryPermissionMatrix.UpdateStock, false },
        { "Seller", InventoryPermissionMatrix.AdjustStock, false },
        { "Seller", InventoryPermissionMatrix.DeleteInventoryRecords, false },
        { "Seller", InventoryPermissionMatrix.ViewInventoryMovements, false },
        { "Seller", InventoryPermissionMatrix.TransferStock, false }
    };

    public static TheoryData<string, string> RoleScopes => new()
    {
        { "RootAdmin", InventoryPermissionMatrix.NoTenantInventoryAccess },
        { "CompanyAdmin", InventoryPermissionMatrix.CompanyInventory },
        { "MainOperator", InventoryPermissionMatrix.AssignedMarketInventory },
        { "DepartmentManager", InventoryPermissionMatrix.AssignedDepartmentInventory },
        { "InventoryEmployee", InventoryPermissionMatrix.AssignedMarketOrDepartmentInventory },
        { "Seller", InventoryPermissionMatrix.PosAvailabilityOnly }
    };

    [Theory]
    [MemberData(nameof(RolePermissions))]
    public void Can_ReturnsExpectedInventoryPermission(
        string role,
        string permission,
        bool expectedAllowed)
    {
        Assert.Equal(expectedAllowed, InventoryPermissionMatrix.Can(role, permission));
    }

    [Theory]
    [MemberData(nameof(RoleScopes))]
    public void Roles_DefineExpectedInventoryScope(string role, string expectedScope)
    {
        Assert.Equal(expectedScope, InventoryPermissionMatrix.Roles[role].Scope);
    }
}
