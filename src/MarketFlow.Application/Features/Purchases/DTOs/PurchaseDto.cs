namespace MarketFlow.Application.Features.Purchases.DTOs;

public class PurchaseDto
{
    public int Id { get; set; }

    public int SupplierId { get; set; }

    public int MarketId { get; set; }

    public DateOnly PurchaseDate { get; set; }

    public string Status { get; set; } = string.Empty;

    public decimal TotalAmount { get; set; }
}
