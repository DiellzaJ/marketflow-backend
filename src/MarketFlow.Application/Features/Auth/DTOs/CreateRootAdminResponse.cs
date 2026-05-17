namespace MarketFlow.Application.Features.Auth.DTOs;

public class CreateRootAdminResponse
{
    public int UserId { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public int CompanyId { get; set; }

    public string SchemaName { get; set; } = string.Empty;
}
