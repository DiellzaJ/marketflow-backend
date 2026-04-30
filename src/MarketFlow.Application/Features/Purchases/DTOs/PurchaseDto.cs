namespace MarketFlow.Application.Features.Purchases.DTOs;

public class PurchaseDto
{
    public Guid Id { get; set; }

    public string ReferenceNumber { get; set; } = string.Empty;

    public decimal TotalAmount { get; set; }
}
