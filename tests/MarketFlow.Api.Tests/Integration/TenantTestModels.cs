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

public sealed record TenantTestProduct(
    int Id,
    string Name,
    string Barcode,
    int CategoryId);

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
