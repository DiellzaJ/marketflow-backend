namespace MarketFlow.Application.Features.Dashboard.DTOs;

public class SalesSummaryQuery
{
    public DateOnly? From { get; set; }

    public DateOnly? To { get; set; }
}
