namespace MarketFlow.Application.Features.Sales.DTOs;

public class SaleDto
{
    public int Id { get; set; }

    public int MarketId { get; set; }

    public DateOnly SaleDate { get; set; }

    public string PaymentMethod { get; set; } = string.Empty;

    public decimal TotalAmount { get; set; }
}
