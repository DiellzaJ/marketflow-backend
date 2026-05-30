namespace MarketFlow.Application.Features.AI.DTOs;

public sealed class NaturalLanguageReportResponseDto
{
    public NaturalLanguageReportIntentDto Intent { get; init; } = new();

    public object Report { get; init; } = new();
}
