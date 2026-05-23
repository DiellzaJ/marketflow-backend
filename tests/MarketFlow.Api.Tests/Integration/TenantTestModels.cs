namespace MarketFlow.Api.Tests.Integration;

public sealed record TenantTestCompany(
    int Id,
    string Name,
    string SchemaName);

public sealed record TenantTestUser(
    int Id,
    int CompanyId,
    string SchemaName,
    string FullName,
    string Email,
    string RoleName,
    bool IsActive);

public sealed record TenantTestCategory(
    int Id,
    string Name);

public sealed record TenantTestMarket(
    int Id,
    string Name);

public sealed record TenantTestDepartment(
    int Id,
    int MarketId,
    string Name,
    string? Description,
    bool IsActive);

public sealed record TenantTestSupplier(
    int Id,
    string Name);

public sealed record TenantTestStaffAssignment(
    int Id,
    int UserId,
    int MarketId,
    int? DepartmentId,
    bool IsActive);

public sealed record TenantTestProduct(
    int Id,
    string Name,
    string Barcode,
    int CategoryId);

public sealed record TenantTestInventoryItem(
    int Id,
    int ProductId,
    int MarketId,
    int? DepartmentId,
    int Quantity,
    int ReservedQuantity);

public sealed record TenantTestInventoryMovement(
    int Id,
    int InventoryId,
    string MovementType,
    int QuantityChanged,
    string? ReferenceNumber,
    int? CreatedByUserId);

public sealed record TenantTestPurchase(
    int Id,
    int SupplierId,
    int MarketId,
    string Status);

public sealed record TenantTestProductDetails(
    int Id,
    string Name,
    string Barcode,
    int CategoryId,
    decimal UnitPrice,
    decimal CostPrice,
    decimal TaxRate,
    int MinStockAlert,
    bool IsActive);
