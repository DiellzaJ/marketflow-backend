namespace MarketFlow.Application.Common.Interfaces;

public sealed record TenantContext(
    string SchemaName,
    bool UserIsActive,
    bool CompanyIsActive);
