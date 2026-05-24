namespace MarketFlow.Application.Features.Departments.DTOs;

public class UpdateDepartmentRequest
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool? IsActive { get; set; }
}
