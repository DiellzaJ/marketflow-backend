namespace MarketFlow.Application.Features.AI.DTOs;

public class AiBusinessDataQuery
{
    public DateOnly? From { get; set; }

    public DateOnly? To { get; set; }

    public int? MarketId { get; set; }

    public int? DepartmentId { get; set; }

    public int TopProductsLimit { get; set; } = 10;

    public int LowStockLimit { get; set; } = 20;
}
