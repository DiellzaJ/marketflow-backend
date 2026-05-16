namespace MarketFlow.Application.Features.Auth.DTOs;

public class RegisterRequest
{
    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public int CompanyId { get; set; }

    public string RoleName { get; set; } = "Seller";
}