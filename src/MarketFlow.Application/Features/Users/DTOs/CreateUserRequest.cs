namespace MarketFlow.Application.Features.Users.DTOs;

public class CreateUserRequest
{
    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public int? CompanyId { get; set; }

    public string RoleName { get; set; } = "Seller";

    public int? MarketId { get; set; }

    public int? DepartmentId { get; set; }

    public bool IsActive { get; set; } = true;
}
