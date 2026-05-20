namespace MarketFlow.Application.Features.Categories.DTOs;

public class CategoryDto
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }
}
