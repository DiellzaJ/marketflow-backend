namespace MarketFlow.Application.Features.Users.DTOs;

public class PatchUserRequest
{
    public string? FullName { get; set; }

    public string? RoleName { get; set; }

    /// <summary>
    /// When supplied, reassigns the user to the selected market. Set alongside DepartmentId for department-scoped roles.
    /// </summary>
    public int? MarketId { get; set; }

    /// <summary>
    /// When supplied with MarketId, assigns the user to a department inside the selected market.
    /// </summary>
    public int? DepartmentId { get; set; }

    public bool? IsActive { get; set; }
}
