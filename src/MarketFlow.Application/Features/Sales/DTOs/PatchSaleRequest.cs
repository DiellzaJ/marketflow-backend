namespace MarketFlow.Application.Features.Sales.DTOs;

public class PatchSaleRequest
{
    public int? MarketId { get; set; }

    public DateOnly? SaleDate { get; set; }

    public string? PaymentMethod { get; set; }

    public decimal? DiscountAmount { get; set; }

    public decimal? TotalAmount { get; set; }

    public string? Notes { get; set; }
}
