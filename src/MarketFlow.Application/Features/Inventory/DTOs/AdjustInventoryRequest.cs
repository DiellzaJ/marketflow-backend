namespace MarketFlow.Application.Features.Inventory.DTOs;

public class AdjustInventoryRequest
{
    public int QuantityChange { get; set; }

    public string Reason { get; set; } = string.Empty;

    public string? Note { get; set; }
}
