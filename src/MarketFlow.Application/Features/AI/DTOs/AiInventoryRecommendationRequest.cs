namespace MarketFlow.Application.Features.AI.DTOs;

public class AiInventoryRecommendationRequest
{
    public int SalesHistoryDays { get; set; } = 30;

    public int TargetStockDays { get; set; } = 14;

    public int OverstockDays { get; set; } = 60;

    public int? MarketId { get; set; }

    public int? DepartmentId { get; set; }
}
