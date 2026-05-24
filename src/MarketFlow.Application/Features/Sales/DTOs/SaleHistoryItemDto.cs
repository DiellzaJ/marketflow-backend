namespace MarketFlow.Application.Features.Sales.DTOs;

public class SaleHistoryItemDto
{
    public int Id { get; set; }

    public string ReferenceNumber { get; set; } = string.Empty;

    public DateOnly SaleDate { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public int MarketId { get; set; }

    public string? MarketName { get; set; }

    public int CashierUserId { get; set; }

    public string? CashierName { get; set; }

    public string Status { get; set; } = string.Empty;

    public string PaymentMethod { get; set; } = string.Empty;

    public decimal TotalAmount { get; set; }

    public int ItemCount { get; set; }
}
