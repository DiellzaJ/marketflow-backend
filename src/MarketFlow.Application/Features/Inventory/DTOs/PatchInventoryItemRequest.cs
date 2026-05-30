namespace MarketFlow.Application.Features.Inventory.DTOs;

public class PatchInventoryItemRequest
{
    public int? Quantity { get; set; }

    public int? ReservedQuantity { get; set; }
}
