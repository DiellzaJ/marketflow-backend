using System.Text.RegularExpressions;
using MarketFlow.Application.Common.Interfaces;

namespace MarketFlow.Infrastructure.MultiTenancy;

public class TenantProvider
{
    private static readonly Regex SchemaNamePattern = new(
        "^[a-zA-Z_][a-zA-Z0-9_]{0,62}$",
        RegexOptions.Compiled);

    private readonly ICurrentUserService _currentUserService;

    public TenantProvider(ICurrentUserService currentUserService)
    {
        _currentUserService = currentUserService;
    }

    public int? GetCurrentCompanyId()
    {
        return _currentUserService.CompanyId;
    }

    public string GetCurrentSchemaName()
    {
        var schemaName = _currentUserService.SchemaName;

        if (string.IsNullOrWhiteSpace(schemaName) || !SchemaNamePattern.IsMatch(schemaName))
        {
            throw new UnauthorizedAccessException("Tenant schema is missing or invalid.");
        }

        return schemaName;
    }
}
