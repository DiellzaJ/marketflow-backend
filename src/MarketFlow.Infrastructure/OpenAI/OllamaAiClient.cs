using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MarketFlow.Application.Features.AI.DTOs;
using MarketFlow.Application.Features.AI.Interfaces;
using Microsoft.Extensions.Options;

namespace MarketFlow.Infrastructure.OpenAI;

public sealed class OllamaAiClient : IAiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly OllamaOptions _options;

    public OllamaAiClient(HttpClient httpClient, IOptions<OllamaOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<AiCompletionResponseDto> GenerateTextAsync(
        AiCompletionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            throw new ArgumentException("Prompt is required.", nameof(request));
        }

        var model = string.IsNullOrWhiteSpace(request.Model)
            ? _options.Model
            : request.Model;

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new InvalidOperationException("Ollama model is not configured.");
        }

        using var response = await _httpClient.PostAsJsonAsync(
            "api/chat",
            new OllamaChatRequest(model, CreateMessages(request), Stream: false),
            JsonOptions,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var completion = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(
            cancellationToken: cancellationToken);
        var text = completion?.Message?.Content;

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("Ollama returned an empty response.");
        }

        return new AiCompletionResponseDto
        {
            Text = text,
            Model = completion?.Model ?? model
        };
    }

    private static IReadOnlyList<OllamaChatMessage> CreateMessages(AiCompletionRequestDto request)
    {
        var messages = new List<OllamaChatMessage>();

        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            messages.Add(new OllamaChatMessage("system", request.SystemPrompt));
        }

        messages.Add(new OllamaChatMessage("user", request.Prompt));
        return messages;
    }

    private sealed record OllamaChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<OllamaChatMessage> Messages,
        [property: JsonPropertyName("stream")] bool Stream);

    private sealed record OllamaChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record OllamaChatResponse(
        [property: JsonPropertyName("model")] string? Model,
        [property: JsonPropertyName("message")] OllamaChatMessage? Message);
}
