namespace MarketFlow.Application.Features.Inventory.DTOs;

public class TransferInventoryRequest
{
    public int FromInventoryId { get; set; }

    public int ToInventoryId { get; set; }

    public int Quantity { get; set; }

    public string? Note { get; set; }
}
