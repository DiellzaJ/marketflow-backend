namespace MarketFlow.Application.Features.Sales.DTOs;

public class SaleDetailsResponse
{
    public int Id { get; set; }

    public string ReferenceNumber { get; set; } = string.Empty;

    public decimal TotalAmount { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public string? CashierName { get; set; }

    public string? MarketName { get; set; }

    public IReadOnlyCollection<SaleItemResponse> Items { get; set; } = [];
}

public class SaleItemResponse
{
    public int ProductId { get; set; }

    public string? ProductName { get; set; }

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal LineTotal { get; set; }
}
