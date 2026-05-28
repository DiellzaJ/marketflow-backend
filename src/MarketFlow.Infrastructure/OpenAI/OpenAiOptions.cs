namespace MarketFlow.Infrastructure.OpenAI;

public sealed class OpenAiOptions
{
    public const string SectionName = "OpenAi";

    public string ApiKey { get; init; } = string.Empty;

    public string Model { get; init; } = "gpt-4.1-mini";

    public Uri BaseUrl { get; init; } = new("https://api.openai.com");

    public bool UseFakeClient { get; init; }
}
