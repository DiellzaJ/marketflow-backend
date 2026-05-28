namespace MarketFlow.Application.Features.AI.DTOs;

public class AiDashboardSummaryRequest
{
    public DateOnly? From { get; set; }

    public DateOnly? To { get; set; }

    public int? MarketId { get; set; }

    public int? DepartmentId { get; set; }

    public int TopProductsLimit { get; set; } = 5;

    public int LowStockLimit { get; set; } = 10;
}
