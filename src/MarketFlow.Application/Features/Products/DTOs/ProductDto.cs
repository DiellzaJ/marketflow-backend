namespace MarketFlow.Application.Features.Products.DTOs;

public class ProductDto
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? Barcode { get; set; }

    public decimal UnitPrice { get; set; }
}
