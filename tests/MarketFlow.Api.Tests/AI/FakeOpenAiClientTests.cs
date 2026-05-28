using MarketFlow.Api.Extensions;
using MarketFlow.Application.Features.AI.DTOs;
using MarketFlow.Application.Features.AI.Interfaces;
using MarketFlow.Infrastructure.OpenAI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MarketFlow.Api.Tests.AI;

public sealed class FakeOpenAiClientTests
{
    [Fact]
    public async Task GenerateTextAsync_ReturnsStableFakeResponse()
    {
        var client = new FakeOpenAiClient(Options.Create(new OpenAiOptions()));

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
        Assert.StartsWith("fake-openai-development", first.Model);
    }

    [Fact]
    public async Task GenerateTextAsync_WhenClassifyingReport_ReturnsPredictableIntentJson()
    {
        var client = new FakeOpenAiClient(Options.Create(new OpenAiOptions()));

        var response = await client.GenerateTextAsync(new AiCompletionRequestDto
        {
            SystemPrompt = "Classify the user's business question into exactly one allowed report type.",
            Prompt = "Question: Which products sold best this week?"
        });

        Assert.Equal("""{"reportType":"TopSellingProducts","confidence":0.99}""", response.Text);
    }

    [Fact]
    public void AddApiServices_WhenUseFakeClientIsTrue_RegistersFakeOpenAiClient()
    {
        using var services = CreateServiceProvider(useFakeClient: true);

        var client = services.GetRequiredService<IOpenAiClient>();

        Assert.IsType<FakeOpenAiClient>(client);
    }

    [Fact]
    public void AddApiServices_WhenUseFakeClientIsFalse_RegistersRealOpenAiClient()
    {
        using var services = CreateServiceProvider(useFakeClient: false);

        var client = services.GetRequiredService<IOpenAiClient>();

        Assert.IsType<OpenAiClient>(client);
    }

    private static ServiceProvider CreateServiceProvider(bool useFakeClient)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=marketflow_test;Username=test;Password=test",
                ["Jwt:Secret"] = "0123456789abcdef0123456789abcdef",
                ["Jwt:Issuer"] = "MarketFlow.Tests",
                ["Jwt:Audience"] = "MarketFlow.Tests",
                ["OpenAi:UseFakeClient"] = useFakeClient.ToString(),
                ["OpenAi:Model"] = "test-model"
            })
            .Build();

        return new ServiceCollection()
            .AddLogging()
            .AddApiServices(configuration)
            .BuildServiceProvider();
    }
}
