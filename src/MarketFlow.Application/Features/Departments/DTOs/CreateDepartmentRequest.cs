namespace MarketFlow.Application.Features.Departments.DTOs;

public class CreateDepartmentRequest
{
    public int MarketId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }
}
