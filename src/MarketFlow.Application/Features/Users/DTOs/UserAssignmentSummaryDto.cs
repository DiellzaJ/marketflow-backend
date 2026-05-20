namespace MarketFlow.Application.Features.Users.DTOs;

public class UserAssignmentSummaryDto
{
    public int MarketId { get; set; }

    public string MarketName { get; set; } = string.Empty;

    public int? DepartmentId { get; set; }

    public string? DepartmentName { get; set; }
}
