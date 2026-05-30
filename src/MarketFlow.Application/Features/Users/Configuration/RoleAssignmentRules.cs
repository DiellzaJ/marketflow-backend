namespace MarketFlow.Application.Features.Users.Configuration;

public static class RoleAssignmentRules
{
    public const string RootAdmin = "RootAdmin";
    public const string CompanyAdmin = "CompanyAdmin";
    public const string Seller = "Seller";
    public const string MainOperator = "MainOperator";
    public const string DepartmentManager = "DepartmentManager";
    public const string InventoryEmployee = "InventoryEmployee";

    private static readonly IReadOnlyDictionary<string, int> RoleRanks = new Dictionary<string, int>(
        StringComparer.OrdinalIgnoreCase)
    {
        [RootAdmin] = 600,
        [CompanyAdmin] = 500,
        [MainOperator] = 400,
        [DepartmentManager] = 300,
        [InventoryEmployee] = 200,
        [Seller] = 100
    };

    private static readonly Dictionary<string, RoleRequirements> Rules = new(StringComparer.OrdinalIgnoreCase)
    {
        [Seller] = new RoleRequirements(RequiresMarket: true),
        [MainOperator] = new RoleRequirements(RequiresMarket: true),
        [DepartmentManager] = new RoleRequirements(RequiresMarket: true, RequiresDepartment: true),
        [InventoryEmployee] = new RoleRequirements(RequiresMarket: true),
        [CompanyAdmin] = new RoleRequirements(AllowsMarket: false, AllowsDepartment: false)
    };

    private static readonly HashSet<string> CreatableRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        CompanyAdmin,
        Seller,
        MainOperator,
        DepartmentManager,
        InventoryEmployee
    };

    private static readonly Dictionary<string, HashSet<string>> AllowedTargetRolesByCaller =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [RootAdmin] = CreatableRoles,
            [CompanyAdmin] = CreatableRoles
        };

    public static string NormalizeRoleName(string roleName)
    {
        var trimmedRoleName = roleName.Trim();

        return trimmedRoleName.ToUpperInvariant() switch
        {
            "SELLER" => Seller,
            "MAINOPERATOR" => MainOperator,
            "DEPARTMENTMANAGER" => DepartmentManager,
            "INVENTORYEMPLOYEE" => InventoryEmployee,
            "COMPANYADMIN" => CompanyAdmin,
            "ROOTADMIN" => RootAdmin,
            _ => trimmedRoleName
        };
    }

    public static bool IsUserManagementRole(string? roleName)
    {
        var normalizedRoleName = NormalizeRoleName(roleName ?? string.Empty);

        return string.Equals(normalizedRoleName, RootAdmin, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalizedRoleName, CompanyAdmin, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalizedRoleName, MainOperator, StringComparison.OrdinalIgnoreCase);
    }

    public static int CompareRoleRank(string? leftRoleName, string? rightRoleName)
    {
        var leftRank = GetRoleRank(leftRoleName);
        var rightRank = GetRoleRank(rightRoleName);

        return leftRank.CompareTo(rightRank);
    }

    public static bool IsLowerRole(string? actorRoleName, string? targetRoleName)
    {
        return CompareRoleRank(actorRoleName, targetRoleName) > 0;
    }

    public static bool IsSameRole(string? leftRoleName, string? rightRoleName)
    {
        return string.Equals(
            NormalizeRoleName(leftRoleName ?? string.Empty),
            NormalizeRoleName(rightRoleName ?? string.Empty),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Validates whether a role can be created with the provided market and department assignment.
    /// </summary>
    /// <returns>An error message when invalid; otherwise null.</returns>
    public static string? ValidateAssignment(string roleName, int? marketId, int? departmentId)
    {
        var normalizedRoleName = NormalizeRoleName(roleName);

        if (!Rules.TryGetValue(normalizedRoleName, out var requirements))
        {
            return departmentId.HasValue && !marketId.HasValue
                ? "Department assignment requires market assignment."
                : null;
        }

        return requirements.Validate(normalizedRoleName, marketId, departmentId);
    }

    public static string? ValidateCreatePermission(string? callerRoleName, string targetRoleName)
    {
        var normalizedTargetRoleName = NormalizeRoleName(targetRoleName);

        if (string.IsNullOrWhiteSpace(normalizedTargetRoleName))
        {
            return "Role is required.";
        }

        if (string.Equals(normalizedTargetRoleName, RootAdmin, StringComparison.OrdinalIgnoreCase))
        {
            return "RootAdmin users cannot be created through this endpoint.";
        }

        if (!CreatableRoles.Contains(normalizedTargetRoleName))
        {
            return $"Role '{normalizedTargetRoleName}' cannot be created through this endpoint.";
        }

        var normalizedCallerRoleName = NormalizeRoleName(callerRoleName ?? string.Empty);

        if (AllowedTargetRolesByCaller.TryGetValue(normalizedCallerRoleName, out var allowedTargetRoles) &&
            allowedTargetRoles.Contains(normalizedTargetRoleName))
        {
            return null;
        }

        return $"Role '{normalizedCallerRoleName}' is not allowed to create {normalizedTargetRoleName} users.";
    }

    private static int GetRoleRank(string? roleName)
    {
        var normalizedRoleName = NormalizeRoleName(roleName ?? string.Empty);

        return RoleRanks.TryGetValue(normalizedRoleName, out var rank)
            ? rank
            : 0;
    }

    private sealed record RoleRequirements(
        bool RequiresMarket = false,
        bool RequiresDepartment = false,
        bool AllowsMarket = true,
        bool AllowsDepartment = true)
    {
        public string? Validate(string roleName, int? marketId, int? departmentId)
        {
            var hasMarket = marketId.HasValue;
            var hasDepartment = departmentId.HasValue;

            if (RequiresMarket && !hasMarket)
            {
                return RequiresDepartment
                    ? $"{roleName} requires market and department assignment."
                    : $"{roleName} requires market assignment.";
            }

            if (RequiresDepartment && !hasDepartment)
            {
                return $"{roleName} requires department assignment.";
            }

            if (!AllowsMarket && hasMarket)
            {
                return $"{roleName} cannot be assigned to a market or department.";
            }

            if (!AllowsDepartment && hasDepartment)
            {
                return $"{roleName} cannot be assigned to a department.";
            }

            if (hasDepartment && !hasMarket)
            {
                return "Department assignment requires market assignment.";
            }

            return null;
        }
    }
}
