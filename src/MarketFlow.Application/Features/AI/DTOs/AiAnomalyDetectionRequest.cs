namespace MarketFlow.Application.Features.AI.DTOs;

public class AiAnomalyDetectionRequest
{
    public DateOnly? From { get; set; }

    public DateOnly? To { get; set; }

    public int? MarketId { get; set; }

    public int? DepartmentId { get; set; }

    public decimal HighDiscountRateThreshold { get; set; } = 0.30m;

    public int LargeStockMovementThreshold { get; set; } = 100;

    public decimal SalesSpikeMultiplier { get; set; } = 3m;

    public int MinimumSalesSpikeQuantity { get; set; } = 10;
}
