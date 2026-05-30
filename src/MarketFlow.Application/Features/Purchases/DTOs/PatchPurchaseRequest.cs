namespace MarketFlow.Application.Features.Purchases.DTOs;

public class PatchPurchaseRequest
{
    public int? SupplierId { get; set; }

    public int? MarketId { get; set; }

    public DateOnly? PurchaseDate { get; set; }

    public DateOnly? ExpectedDate { get; set; }

    public string? Status { get; set; }

    public decimal? TotalAmount { get; set; }

    public string? Notes { get; set; }
}
