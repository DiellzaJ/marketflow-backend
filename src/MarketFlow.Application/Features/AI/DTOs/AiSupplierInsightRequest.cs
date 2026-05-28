namespace MarketFlow.Application.Features.AI.DTOs;

public class AiSupplierInsightRequest
{
    public DateOnly? From { get; set; }

    public DateOnly? To { get; set; }

    public int? MarketId { get; set; }
}
