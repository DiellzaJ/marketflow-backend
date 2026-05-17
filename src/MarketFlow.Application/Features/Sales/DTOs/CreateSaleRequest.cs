namespace MarketFlow.Application.Features.Sales.DTOs;

public class CreateSaleRequest
{
    public int MarketId { get; set; }

    public DateOnly? SaleDate { get; set; }

    public string PaymentMethod { get; set; } = "Cash";

    public decimal DiscountAmount { get; set; }

    public decimal TotalAmount { get; set; }

    public string? Notes { get; set; }
}
