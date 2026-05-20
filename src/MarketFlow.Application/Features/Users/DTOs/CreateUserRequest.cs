namespace MarketFlow.Application.Features.Users.DTOs;

public class CreateUserRequest
{
    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public int? CompanyId { get; set; }

    public string RoleName { get; set; } = "Seller";

    /// <summary>
    /// Required for operational users such as Seller, MainOperator, DepartmentManager, and InventoryEmployee.
    /// CompanyAdmin users must omit this value.
    /// </summary>
    public int? MarketId { get; set; }

    /// <summary>
    /// Optional department assignment inside the selected market.
    /// </summary>
    public int? DepartmentId { get; set; }

    public bool IsActive { get; set; } = true;
}
