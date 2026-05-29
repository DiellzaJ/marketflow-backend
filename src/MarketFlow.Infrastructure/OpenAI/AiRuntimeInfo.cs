using MarketFlow.Application.Features.AI.Interfaces;
using Microsoft.Extensions.Options;

namespace MarketFlow.Infrastructure.OpenAI;

public sealed class AiRuntimeInfo : IAiRuntimeInfo
{
    private readonly AiOptions _aiOptions;
    private readonly OpenAiOptions _openAiOptions;
    private readonly OllamaOptions _ollamaOptions;

    public AiRuntimeInfo(
        IOptions<AiOptions> aiOptions,
        IOptions<OpenAiOptions> openAiOptions,
        IOptions<OllamaOptions> ollamaOptions)
    {
        _aiOptions = aiOptions.Value;
        _openAiOptions = openAiOptions.Value;
        _ollamaOptions = ollamaOptions.Value;
    }

    public string Provider => _aiOptions.Provider.ToString();

    public string Model => _aiOptions.Provider switch
    {
        AiProvider.OpenAI => _openAiOptions.Model,
        AiProvider.Ollama => _ollamaOptions.Model,
        _ => "fake-ai-development"
    };
}
