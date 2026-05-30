namespace MarketFlow.Application.Features.Products.DTOs;

public class ProductDto
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? Barcode { get; set; }

    public int? CategoryId { get; set; }

    public string? CategoryName { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal CostPrice { get; set; }

    public decimal TaxRate { get; set; }

    public int MinStockAlert { get; set; }

    public bool IsActive { get; set; }
}
