namespace MarketFlow.Application.Features.Purchases.DTOs;

public class PurchaseDto
{
    public int Id { get; set; }

    public int SupplierId { get; set; }

    public string SupplierName { get; set; } = string.Empty;

    public int MarketId { get; set; }

    public string MarketName { get; set; } = string.Empty;

    public DateOnly PurchaseDate { get; set; }

    public DateOnly? ExpectedDate { get; set; }

    public string Status { get; set; } = string.Empty;

    public decimal TotalAmount { get; set; }

    public string? Notes { get; set; }

    public int ItemCount { get; set; }

    public int TotalQuantity { get; set; }

    public int ReceivedQuantity { get; set; }

    public IReadOnlyCollection<PurchaseItemDto> Items { get; set; } = [];
}

public class PurchaseItemDto
{
    public int Id { get; set; }

    public int ProductId { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public int Quantity { get; set; }

    public int ReceivedQuantity { get; set; }

    public decimal UnitCost { get; set; }

    public decimal LineTotal { get; set; }
}
