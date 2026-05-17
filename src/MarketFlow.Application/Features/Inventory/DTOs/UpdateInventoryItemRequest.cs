namespace MarketFlow.Application.Features.Inventory.DTOs;

public class UpdateInventoryItemRequest
{
    public int Quantity { get; set; }

    public int ReservedQuantity { get; set; }
}
