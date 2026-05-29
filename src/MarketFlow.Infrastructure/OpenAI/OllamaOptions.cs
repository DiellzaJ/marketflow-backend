namespace MarketFlow.Infrastructure.OpenAI;

public sealed class OllamaOptions
{
    public const string SectionName = "Ollama";

    public Uri BaseUrl { get; init; } = new("http://localhost:11434");

    public string Model { get; init; } = "llama3.1";
}
