namespace MarketFlow.Application.Features.AI.DTOs;

public sealed class AiCompletionResponseDto
{
    public required string Text { get; init; }

    public required string Model { get; init; }
}
