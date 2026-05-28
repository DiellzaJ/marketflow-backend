namespace MarketFlow.Application.Features.AI.DTOs;

public sealed class AiChatRequest
{
    public int? SessionId { get; init; }

    public string Message { get; init; } = string.Empty;

    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }

    public int? MarketId { get; init; }

    public int? DepartmentId { get; init; }
}
