using System.Text.RegularExpressions;
using MarketFlow.Application.Common.Interfaces;

namespace MarketFlow.Infrastructure.MultiTenancy;

public class TenantProvider
{
    private static readonly Regex SchemaNamePattern = new(
        "^[a-zA-Z_][a-zA-Z0-9_]{0,62}$",
        RegexOptions.Compiled);

    private readonly ICurrentUserService _currentUserService;
    private readonly ITenantContextStore _tenantContextStore;
    private readonly SemaphoreSlim _resolutionLock = new(1, 1);
    private string? _resolvedSchemaName;

    public TenantProvider(
        ICurrentUserService currentUserService,
        ITenantContextStore tenantContextStore)
    {
        _currentUserService = currentUserService;
        _tenantContextStore = tenantContextStore;
    }

    public int? GetCurrentCompanyId()
    {
        return _currentUserService.CompanyId;
    }

    public async Task<string> GetCurrentSchemaNameAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(_resolvedSchemaName))
        {
            return _resolvedSchemaName;
        }

        await _resolutionLock.WaitAsync(cancellationToken);

        try
        {
            if (!string.IsNullOrWhiteSpace(_resolvedSchemaName))
            {
                return _resolvedSchemaName;
            }

            _resolvedSchemaName = await ResolveCurrentSchemaNameAsync(cancellationToken);

            return _resolvedSchemaName;
        }
        finally
        {
            _resolutionLock.Release();
        }
    }

    private async Task<string> ResolveCurrentSchemaNameAsync(CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId;

        if (userId is null)
        {
            throw new UnauthorizedAccessException("Authentication is required.");
        }

        var tenant = await _tenantContextStore.GetTenantContextAsync(
            userId.Value,
            cancellationToken);

        if (tenant is null || !tenant.UserIsActive || !tenant.CompanyIsActive)
        {
            throw new TenantAccessException();
        }

        var schemaName = tenant.SchemaName;

        if (string.IsNullOrWhiteSpace(schemaName) || !SchemaNamePattern.IsMatch(schemaName))
        {
            throw new TenantAccessException();
        }

        return schemaName;
    }
}
