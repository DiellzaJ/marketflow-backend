using MarketFlow.Application.Features.AI.DTOs;

namespace MarketFlow.Application.Features.AI.Interfaces;

public interface IOpenAiClient
{
    Task<AiCompletionResponseDto> GenerateTextAsync(
        AiCompletionRequestDto request,
        CancellationToken cancellationToken = default);
}
