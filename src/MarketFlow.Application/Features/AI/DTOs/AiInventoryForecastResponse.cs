namespace MarketFlow.Application.Features.AI.DTOs;

public class AiInventoryForecastResponse
{
    public int SalesHistoryDays { get; set; }

    public int ForecastDays { get; set; }

    public int? MarketId { get; set; }

    public int? DepartmentId { get; set; }

    public IReadOnlyCollection<AiInventoryProductForecastDto> Products { get; set; } = [];
}

public class AiInventoryProductForecastDto
{
    public int ProductId { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public int CurrentStock { get; set; }

    public long TotalQuantitySold { get; set; }

    public decimal AverageDailySales { get; set; }

    public decimal ForecastDemand { get; set; }

    public decimal? DaysOfStockRemaining { get; set; }
}
