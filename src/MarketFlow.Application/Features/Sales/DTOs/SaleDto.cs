namespace MarketFlow.Application.Features.Sales.DTOs;

public class SaleDto
{
    public Guid Id { get; set; }

    public string ReferenceNumber { get; set; } = string.Empty;

    public decimal TotalAmount { get; set; }
}
