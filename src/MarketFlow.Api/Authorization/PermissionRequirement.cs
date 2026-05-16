using Microsoft.AspNetCore.Authorization;

namespace MarketFlow.Api.Authorization;

public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public PermissionRequirement(string permission, string? access = null)
    {
        Permission = permission;
        Access = access;
    }

    public string Permission { get; }

    public string? Access { get; }
}
