namespace MarketFlow.Application.Features.AI.DTOs;

public class AiSupplierInsightDataDto
{
    public int SupplierId { get; set; }

    public string SupplierName { get; set; } = string.Empty;

    public long TotalPurchases { get; set; }

    public long ReceivedPurchases { get; set; }

    public long CancelledPurchases { get; set; }

    public decimal? AverageDeliveryDays { get; set; }

    public decimal TotalAmountSpent { get; set; }
}
