namespace MarketFlow.Application.Features.Inventory.DTOs;

public class PosProductLookupItemDto
{
    public int ProductId { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public string? Barcode { get; set; }

    public decimal UnitPrice { get; set; }

    public int AvailableQuantity { get; set; }
}
