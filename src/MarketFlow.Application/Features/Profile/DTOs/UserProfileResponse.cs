namespace MarketFlow.Application.Features.Profile.DTOs;

public class UserProfileResponse
{
    public int Id { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public int? CompanyId { get; set; }

    public int? MarketId { get; set; }

    public int? DepartmentId { get; set; }

    public bool IsActive { get; set; }
}
