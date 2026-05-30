namespace MarketFlow.Application.Features.Sales.DTOs;

public class CreateSaleRequest
{
    public int MarketId { get; set; }

    public DateOnly? SaleDate { get; set; }

    public string PaymentMethod { get; set; } = "Cash";

    public decimal DiscountAmount { get; set; }

    public decimal TotalAmount { get; set; }

    public string? Notes { get; set; }

    public IReadOnlyCollection<CreateSaleItemRequest> Items { get; set; } = [];
}

public class CreateSaleItemRequest
{
    public int ProductId { get; set; }

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }
}
