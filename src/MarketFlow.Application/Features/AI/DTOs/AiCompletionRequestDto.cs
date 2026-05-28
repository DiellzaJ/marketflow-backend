namespace MarketFlow.Application.Features.AI.DTOs;

public sealed class AiCompletionRequestDto
{
    public required string Prompt { get; init; }

    public string? SystemPrompt { get; init; }

    public string? Model { get; init; }

    public decimal? Temperature { get; init; }
}
