using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MarketFlow.Application.Features.AI.DTOs;
using MarketFlow.Application.Features.AI.Interfaces;
using Microsoft.Extensions.Options;

namespace MarketFlow.Infrastructure.OpenAI;

public sealed class OpenAiClient : IOpenAiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly OpenAiOptions _options;

    public OpenAiClient(HttpClient httpClient, IOptions<OpenAiOptions> options)
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

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("OpenAI API key is not configured.");
        }

        var model = string.IsNullOrWhiteSpace(request.Model)
            ? _options.Model
            : request.Model;

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new InvalidOperationException("OpenAI model is not configured.");
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        httpRequest.Content = JsonContent.Create(new ChatCompletionRequest(
            model,
            CreateMessages(request),
            request.Temperature), options: JsonOptions);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        response.EnsureSuccessStatusCode();

        var completion = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(
            cancellationToken: cancellationToken);

        var text = completion?.Choices?.FirstOrDefault()?.Message?.Content;

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("OpenAI returned an empty response.");
        }

        return new AiCompletionResponseDto
        {
            Text = text,
            Model = completion?.Model ?? model
        };
    }

    private static IReadOnlyList<ChatMessage> CreateMessages(AiCompletionRequestDto request)
    {
        var messages = new List<ChatMessage>();

        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            messages.Add(new ChatMessage("system", request.SystemPrompt));
        }

        messages.Add(new ChatMessage("user", request.Prompt));
        return messages;
    }

    private sealed record ChatCompletionRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages,
        [property: JsonPropertyName("temperature")] decimal? Temperature);

    private sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record ChatCompletionResponse(
        [property: JsonPropertyName("model")] string? Model,
        [property: JsonPropertyName("choices")] IReadOnlyList<ChatChoice>? Choices);

    private sealed record ChatChoice(
        [property: JsonPropertyName("message")] ChatMessage? Message);
}
