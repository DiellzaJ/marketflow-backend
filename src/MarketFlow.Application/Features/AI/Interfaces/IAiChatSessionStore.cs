using MarketFlow.Application.Features.AI.DTOs;

namespace MarketFlow.Application.Features.AI.Interfaces;

public interface IAiChatSessionStore
{
    Task<AiChatSessionDto> AppendMessagesAsync(
        int? sessionId,
        int userId,
        int? marketId,
        IReadOnlyCollection<AiChatMessageDto> messages,
        CancellationToken cancellationToken = default);
}
