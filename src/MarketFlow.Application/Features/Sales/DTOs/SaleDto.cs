namespace MarketFlow.Application.Features.Sales.DTOs;

public class SaleDto
{
    public int Id { get; set; }

    public string ReferenceNumber { get; set; } = string.Empty;

    public int MarketId { get; set; }

    public DateOnly SaleDate { get; set; }

    public string PaymentMethod { get; set; } = string.Empty;

    public decimal TotalAmount { get; set; }
}
