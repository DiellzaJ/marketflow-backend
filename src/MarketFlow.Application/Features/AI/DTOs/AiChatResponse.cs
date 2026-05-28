namespace MarketFlow.Application.Features.AI.DTOs;

public sealed class AiChatResponse
{
    public int SessionId { get; init; }

    public string Answer { get; init; } = string.Empty;

    public AiReportType? ReportType { get; init; }
}
