namespace MarketFlow.Application.Features.Users.DTOs;

public class PatchUserRequest
{
    public string? FullName { get; set; }

    public string? Email { get; set; }

    public string? RoleName { get; set; }

    public string? Password { get; set; }

    public bool? IsActive { get; set; }
}
