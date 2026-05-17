namespace MarketFlow.Application.Features.Inventory.DTOs;

public class CreateInventoryItemRequest
{
    public int ProductId { get; set; }

    public int MarketId { get; set; }

    public int? DepartmentId { get; set; }

    public int Quantity { get; set; }

    public int ReservedQuantity { get; set; }
}
