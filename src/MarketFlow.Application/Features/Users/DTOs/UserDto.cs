namespace MarketFlow.Application.Features.Users.DTOs;

public class UserDto
{
    public int Id { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string RoleName { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public UserAssignmentSummaryDto? Assignment { get; set; }
}
