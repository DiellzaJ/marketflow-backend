namespace MarketFlow.Application.Features.Departments.DTOs;

public class DepartmentDto
{
    public int Id { get; set; }

    public int MarketId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsActive { get; set; }
}
