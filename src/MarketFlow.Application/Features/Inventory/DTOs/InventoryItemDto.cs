namespace MarketFlow.Application.Features.Inventory.DTOs;

public class InventoryItemDto
{
    public Guid ProductId { get; set; }

    public int QuantityOnHand { get; set; }

    public int ReorderLevel { get; set; }
}
