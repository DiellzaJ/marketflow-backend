namespace MarketFlow.Application.Features.AI.DTOs;

public sealed class AiChatMessageDto
{
    public string Role { get; init; } = string.Empty;

    public string Content { get; init; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; }

    public AiReportType? ReportType { get; init; }
}
