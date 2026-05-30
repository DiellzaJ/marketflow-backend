using MarketFlow.Infrastructure.OpenAI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace MarketFlow.Api.Tests.AI;

public sealed class AiRuntimeInfoTests
{
    [Fact]
    public void Provider_WhenAiProviderIsMissingAndLegacyFakeIsFalse_ReportsOpenAi()
    {
        var runtimeInfo = CreateRuntimeInfo(new Dictionary<string, string?>
        {
            ["OpenAi:UseFakeClient"] = "false",
            ["OpenAi:Model"] = "gpt-test"
        });

        Assert.Equal("OpenAI", runtimeInfo.Provider);
        Assert.Equal("gpt-test", runtimeInfo.Model);
    }

    [Fact]
    public void Provider_WhenAiProviderIsExplicit_UsesExplicitProvider()
    {
        var runtimeInfo = CreateRuntimeInfo(new Dictionary<string, string?>
        {
            ["Ai:Provider"] = "Ollama",
            ["OpenAi:UseFakeClient"] = "false",
            ["Ollama:Model"] = "llama-test"
        });

        Assert.Equal("Ollama", runtimeInfo.Provider);
        Assert.Equal("llama-test", runtimeInfo.Model);
    }

    private static AiRuntimeInfo CreateRuntimeInfo(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        return new AiRuntimeInfo(
            Options.Create(configuration.GetSection(AiOptions.SectionName).Get<AiOptions>() ?? new AiOptions()),
            Options.Create(configuration.GetSection(OpenAiOptions.SectionName).Get<OpenAiOptions>() ?? new OpenAiOptions()),
            Options.Create(configuration.GetSection(OllamaOptions.SectionName).Get<OllamaOptions>() ?? new OllamaOptions()),
            configuration);
    }
}
