namespace MarketFlow.Application.Features.Purchases.DTOs;

public class CreatePurchaseRequest
{
    public int SupplierId { get; set; }

    public int MarketId { get; set; }

    public DateOnly? PurchaseDate { get; set; }

    public string Status { get; set; } = "Pending";

    public decimal TotalAmount { get; set; }

    public string? Notes { get; set; }

    public IReadOnlyCollection<CreatePurchaseItemRequest> Items { get; set; } = [];
}

public class CreatePurchaseItemRequest
{
    public int ProductId { get; set; }

    public int Quantity { get; set; }

    public decimal UnitCost { get; set; }
}
