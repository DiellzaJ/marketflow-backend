namespace MarketFlow.Application.Features.AI.DTOs;

public class AiDashboardSummaryResponse
{
    public AiDashboardKpisDto Kpis { get; set; } = new();

    public string Summary { get; set; } = string.Empty;

    public IReadOnlyCollection<string> RecommendedActions { get; set; } = [];

    public string Model { get; set; } = string.Empty;
}

public class AiDashboardKpisDto
{
    public decimal TotalSales { get; set; }

    public long TotalOrders { get; set; }

    public decimal AverageOrderValue { get; set; }

    public long TotalItemsSold { get; set; }

    public IReadOnlyCollection<AiTopSellingProductDto> TopSellingProducts { get; set; } = [];

    public IReadOnlyCollection<AiLowStockProductDto> LowStockWarnings { get; set; } = [];
}
