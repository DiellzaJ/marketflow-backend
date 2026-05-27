namespace MarketFlow.Application.Features.AI.DTOs;

public class AiSupplierInsightDto
{
    public int SupplierId { get; set; }

    public string SupplierName { get; set; } = string.Empty;

    public long TotalPurchases { get; set; }

    public long ReceivedPurchases { get; set; }

    public long CancelledPurchases { get; set; }

    public decimal CancellationRate { get; set; }

    public decimal? AverageDeliveryDays { get; set; }

    public decimal TotalAmountSpent { get; set; }

    public string ReliabilityLevel { get; set; } = string.Empty;

    public IReadOnlyCollection<string> Flags { get; set; } = [];

    public string RecommendationText { get; set; } = string.Empty;
}
