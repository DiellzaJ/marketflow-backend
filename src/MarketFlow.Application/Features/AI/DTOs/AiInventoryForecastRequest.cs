namespace MarketFlow.Application.Features.AI.DTOs;

public class AiInventoryForecastRequest
{
    public int SalesHistoryDays { get; set; } = 30;

    public int ForecastDays { get; set; } = 7;

    public int? MarketId { get; set; }

    public int? DepartmentId { get; set; }
}
