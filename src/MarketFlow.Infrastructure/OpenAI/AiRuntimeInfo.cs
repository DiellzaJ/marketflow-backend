using MarketFlow.Application.Features.AI.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace MarketFlow.Infrastructure.OpenAI;

public sealed class AiRuntimeInfo : IAiRuntimeInfo
{
    private readonly AiOptions _aiOptions;
    private readonly OpenAiOptions _openAiOptions;
    private readonly OllamaOptions _ollamaOptions;
    private readonly IConfiguration _configuration;

    public AiRuntimeInfo(
        IOptions<AiOptions> aiOptions,
        IOptions<OpenAiOptions> openAiOptions,
        IOptions<OllamaOptions> ollamaOptions,
        IConfiguration configuration)
    {
        _aiOptions = aiOptions.Value;
        _openAiOptions = openAiOptions.Value;
        _ollamaOptions = ollamaOptions.Value;
        _configuration = configuration;
    }

    public string Provider => ResolvedProvider.ToString();

    public string Model => ResolvedProvider switch
    {
        AiProvider.OpenAI => _openAiOptions.Model,
        AiProvider.Ollama => _ollamaOptions.Model,
        _ => "fake-ai-development"
    };

    private AiProvider ResolvedProvider
    {
        get
        {
            var providerName = _configuration[$"{AiOptions.SectionName}:Provider"];

            if (!string.IsNullOrWhiteSpace(providerName))
            {
                return _aiOptions.Provider;
            }

            var legacyUseFakeClient = _configuration[$"{OpenAiOptions.SectionName}:UseFakeClient"];

            return bool.TryParse(legacyUseFakeClient, out var useFakeClient) && !useFakeClient
                ? AiProvider.OpenAI
                : AiProvider.Fake;
        }
    }
}
