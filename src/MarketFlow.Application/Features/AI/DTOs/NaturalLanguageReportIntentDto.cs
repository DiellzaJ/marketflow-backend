namespace MarketFlow.Application.Features.AI.DTOs;

public sealed class NaturalLanguageReportIntentDto
{
    public AiReportType ReportType { get; init; }

    public decimal Confidence { get; init; } = 1m;
}
