namespace MarketFlow.Application.Features.Users.DTOs;

public class UserAssignmentSummaryDto
{
    /// <summary>
    /// Tenant-schema market/store identifier.
    /// </summary>
    public int MarketId { get; set; }

    /// <summary>
    /// Tenant-schema market/store display name.
    /// </summary>
    public string MarketName { get; set; } = string.Empty;

    /// <summary>
    /// Tenant-schema department identifier when the user is assigned to a department.
    /// </summary>
    public int? DepartmentId { get; set; }

    /// <summary>
    /// Tenant-schema department display name when the user is assigned to a department.
    /// </summary>
    public string? DepartmentName { get; set; }
}
