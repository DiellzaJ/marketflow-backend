namespace MarketFlow.Infrastructure.OpenAI;

public enum AiProvider
{
    Fake,
    OpenAI,
    Ollama
}

public sealed class AiOptions
{
    public const string SectionName = "Ai";

    public AiProvider Provider { get; init; } = AiProvider.Fake;
}
