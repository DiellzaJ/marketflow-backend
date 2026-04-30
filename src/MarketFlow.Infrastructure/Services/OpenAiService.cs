namespace MarketFlow.Infrastructure.Services;

public class OpenAiService
{
    public Task<string> GenerateTextAsync(
        string prompt,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult($"Placeholder response for: {prompt}");
    }
}
