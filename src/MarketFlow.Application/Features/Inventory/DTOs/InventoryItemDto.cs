namespace MarketFlow.Application.Features.Inventory.DTOs;

public class InventoryItemDto
{
    public int Id { get; set; }

    public int ProductId { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public int MarketId { get; set; }

    public string MarketName { get; set; } = string.Empty;

    public int Quantity { get; set; }

    public int ReservedQuantity { get; set; }
}
