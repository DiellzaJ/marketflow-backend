namespace MarketFlow.Application.Features.Purchases.DTOs;

public class UpdatePurchaseRequest
{
    public int SupplierId { get; set; }

    public int MarketId { get; set; }

    public DateOnly PurchaseDate { get; set; }

    public string Status { get; set; } = "Draft";

    public decimal TotalAmount { get; set; }

    public string? Notes { get; set; }

    public IReadOnlyCollection<CreatePurchaseItemRequest>? Items { get; set; }
}
