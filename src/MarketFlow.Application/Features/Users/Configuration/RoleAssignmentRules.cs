namespace MarketFlow.Application.Features.Users.Configuration;

public static class RoleAssignmentRules
{
    public const string RootAdmin = "RootAdmin";
    public const string CompanyAdmin = "CompanyAdmin";
    public const string Seller = "Seller";
    public const string MainOperator = "MainOperator";
    public const string DepartmentManager = "DepartmentManager";

    private static readonly Dictionary<string, RoleRequirements> Rules = new(StringComparer.OrdinalIgnoreCase)
    {
        [Seller] = new RoleRequirements(RequiresMarket: true, AllowsDepartment: false),
        [MainOperator] = new RoleRequirements(RequiresMarket: true, AllowsDepartment: false),
        [DepartmentManager] = new RoleRequirements(RequiresMarket: true, RequiresDepartment: true),
        [CompanyAdmin] = new RoleRequirements(AllowsMarket: false, AllowsDepartment: false)
    };

    public static string NormalizeRoleName(string roleName)
    {
        var trimmedRoleName = roleName.Trim();

        return trimmedRoleName.ToUpperInvariant() switch
        {
            "SELLER" => Seller,
            "MAINOPERATOR" => MainOperator,
            "DEPARTMENTMANAGER" => DepartmentManager,
            "COMPANYADMIN" => CompanyAdmin,
            "ROOTADMIN" => RootAdmin,
            _ => trimmedRoleName
        };
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
                return $"{roleName} requires market and department assignment.";
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
