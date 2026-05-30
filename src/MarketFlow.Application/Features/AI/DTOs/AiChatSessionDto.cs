namespace MarketFlow.Application.Features.AI.DTOs;

public sealed class AiChatSessionDto
{
    public int Id { get; init; }

    public IReadOnlyCollection<AiChatMessageDto> Messages { get; init; } = [];
}
