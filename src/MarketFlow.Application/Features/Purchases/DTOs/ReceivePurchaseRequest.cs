namespace MarketFlow.Application.Features.Purchases.DTOs;

public class ReceivePurchaseRequest
{
    public IReadOnlyCollection<ReceivePurchaseItemRequest> Items { get; set; } = [];
}

public class ReceivePurchaseItemRequest
{
    public int ProductId { get; set; }

    public int Quantity { get; set; }
}
