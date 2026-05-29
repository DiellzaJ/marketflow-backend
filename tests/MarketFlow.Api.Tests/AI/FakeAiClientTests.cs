using MarketFlow.Api.Extensions;
using MarketFlow.Application.Features.AI.DTOs;
using MarketFlow.Application.Features.AI.Interfaces;
using MarketFlow.Infrastructure.OpenAI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MarketFlow.Api.Tests.AI;

public sealed class FakeAiClientTests
{
    [Fact]
    public async Task GenerateTextAsync_ReturnsStableFakeResponse()
    {
        var client = new FakeAiClient();

        var first = await client.GenerateTextAsync(new AiCompletionRequestDto
        {
            Prompt = "Summarize sales"
        });
        var second = await client.GenerateTextAsync(new AiCompletionRequestDto
        {
            Prompt = "Summarize sales"
        });

        Assert.Equal(first.Text, second.Text);
        Assert.Contains("Fake development AI response", first.Text);
        Assert.Equal("fake-ai-development", first.Model);
    }

    [Fact]
    public async Task GenerateTextAsync_WhenClassifyingReport_ReturnsPredictableIntentJson()
    {
        var client = new FakeAiClient();

        var response = await client.GenerateTextAsync(new AiCompletionRequestDto
        {
            SystemPrompt = "Classify the user's business question into exactly one allowed report type.",
            Prompt = "Question: Which products sold best this week?"
        });

        Assert.Equal("""{"reportType":"TopSellingProducts","confidence":0.99}""", response.Text);
    }

    [Fact]
    public void AddApiServices_WhenProviderIsFake_RegistersFakeAiClient()
    {
        using var services = CreateServiceProvider("Fake");

        var client = services.GetRequiredService<IAiClient>();

        Assert.IsType<FakeAiClient>(client);
    }

    [Fact]
    public void AddApiServices_WhenProviderIsOpenAi_RegistersRealOpenAiClient()
    {
        using var services = CreateServiceProvider("OpenAI");

        var client = services.GetRequiredService<IAiClient>();

        Assert.IsType<OpenAiClient>(client);
    }

    [Fact]
    public void AddApiServices_WhenProviderIsOllama_RegistersOllamaAiClient()
    {
        using var services = CreateServiceProvider("Ollama");

        var client = services.GetRequiredService<IAiClient>();

        Assert.IsType<OllamaAiClient>(client);
    }

    [Fact]
    public void AddApiServices_WhenProviderIsMissing_RegistersFakeAiClientByDefault()
    {
        using var services = CreateServiceProvider(provider: null);

        var client = services.GetRequiredService<IAiClient>();

        Assert.IsType<FakeAiClient>(client);
    }

    private static ServiceProvider CreateServiceProvider(string? provider)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=marketflow_test;Username=test;Password=test",
            ["Jwt:Secret"] = "0123456789abcdef0123456789abcdef",
            ["Jwt:Issuer"] = "MarketFlow.Tests",
            ["Jwt:Audience"] = "MarketFlow.Tests",
            ["OpenAi:Model"] = "test-model",
            ["Ollama:BaseUrl"] = "http://localhost:11434",
            ["Ollama:Model"] = "llama3.1"
        };

        if (!string.IsNullOrWhiteSpace(provider))
        {
            settings["Ai:Provider"] = provider;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        return new ServiceCollection()
            .AddLogging()
            .AddApiServices(configuration)
            .BuildServiceProvider();
    }
}
