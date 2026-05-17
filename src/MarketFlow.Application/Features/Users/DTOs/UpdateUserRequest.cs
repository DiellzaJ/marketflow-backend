namespace MarketFlow.Application.Features.Users.DTOs;

public class UpdateUserRequest
{
    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string RoleName { get; set; } = "Seller";

    public string? Password { get; set; }

    public bool IsActive { get; set; } = true;
}
