namespace MarketFlow.Application.Features.AI.DTOs;

public sealed class NaturalLanguageReportRequest
{
    public string Question { get; init; } = string.Empty;

    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }

    public int? MarketId { get; init; }

    public int? DepartmentId { get; init; }

    public int? Limit { get; init; }
}
