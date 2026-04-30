using MarketFlow.Domain.Enums;

namespace MarketFlow.Domain.Entities;

public class User : TenantEntity
{
    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public UserRole RoleType { get; set; } = UserRole.Employee;
}
