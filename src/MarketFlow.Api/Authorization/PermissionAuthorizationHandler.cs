using System.Security.Claims;
using System.Text.Json;
using MarketFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace MarketFlow.Api.Authorization;

public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly ApplicationDbContext _dbContext;

    public PermissionAuthorizationHandler(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        var userIdValue = context.User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!int.TryParse(userIdValue, out var userId))
        {
            return;
        }

        var user = await _dbContext.Users
            .AsNoTracking()
            .Include(x => x.Company)
            .Include(x => x.Role)
            .FirstOrDefaultAsync(x => x.Id == userId);

        if (user is null || !user.IsActive || !user.Company.IsActive)
        {
            return;
        }

        if (PermissionEvaluator.TryHasPermission(user.Role.Permissions, requirement.Permission, requirement.Access))
        {
            context.Succeed(requirement);
        }
    }
}

internal static class PermissionEvaluator
{
    public static bool TryHasPermission(string permissionsJson, string permission, string? requiredAccess = null)
    {
        try
        {
            return HasPermission(permissionsJson, permission, requiredAccess);
        }
        catch (JsonException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool HasPermission(string permissionsJson, string permission, string? requiredAccess)
    {
        using var document = JsonDocument.Parse(permissionsJson);
        var root = document.RootElement;

        if (IsTruthy(root, "all"))
        {
            return true;
        }

        if (requiredAccess is not null &&
            root.TryGetProperty($"{permission}:{requiredAccess}", out var actionValue))
        {
            return IsTruthy(actionValue);
        }

        if (!root.TryGetProperty(permission, out var value))
        {
            return false;
        }

        if (requiredAccess is null)
        {
            return IsTruthy(value);
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.String => GrantsAccess(value.GetString(), requiredAccess),
            JsonValueKind.Array => value.EnumerateArray()
                .Any(x => x.ValueKind == JsonValueKind.String && GrantsAccess(x.GetString(), requiredAccess)),
            _ => false
        };
    }

    private static bool IsTruthy(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value) && IsTruthy(value);
    }

    private static bool IsTruthy(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.String => !string.Equals(value.GetString(), "false", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static bool GrantsAccess(string? grantedAccess, string requiredAccess)
    {
        if (string.IsNullOrWhiteSpace(grantedAccess))
        {
            return false;
        }

        if (string.Equals(grantedAccess, requiredAccess, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(requiredAccess, "read", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(grantedAccess, "update", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return grantedAccess
            .Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(x => string.Equals(x, requiredAccess, StringComparison.OrdinalIgnoreCase));
    }
}
