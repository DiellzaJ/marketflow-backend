using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MarketFlow.Application.Features.AI.DTOs;
using MarketFlow.Infrastructure.OpenAI;
using Microsoft.Extensions.Options;

namespace MarketFlow.Api.Tests.AI;

public sealed class OllamaAiClientTests
{
    [Fact]
    public async Task GenerateTextAsync_PostsChatRequestAndParsesMockedResponse()
    {
        using var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                model = "llama-test",
                message = new
                {
                    role = "assistant",
                    content = "Mocked Ollama answer."
                }
            })
        });
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://ollama.test/")
        };
        var client = new OllamaAiClient(
            httpClient,
            Options.Create(new OllamaOptions { Model = "llama3.1" }));

        var response = await client.GenerateTextAsync(new AiCompletionRequestDto
        {
            SystemPrompt = "System rules",
            Prompt = "Summarize sales",
            Model = "llama-request"
        });

        Assert.Equal("Mocked Ollama answer.", response.Text);
        Assert.Equal("llama-test", response.Model);
        Assert.Equal(HttpMethod.Post, handler.Request?.Method);
        Assert.Equal(new Uri("http://ollama.test/api/chat"), handler.Request?.RequestUri);

        using var document = JsonDocument.Parse(handler.Body!);
        var root = document.RootElement;
        Assert.Equal("llama-request", root.GetProperty("model").GetString());
        Assert.False(root.GetProperty("stream").GetBoolean());

        var messages = root.GetProperty("messages");
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("System rules", messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal("Summarize sales", messages[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task GenerateTextAsync_WhenResponseIsEmpty_ThrowsWithoutRequiringLocalOllama()
    {
        using var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                model = "llama-test",
                message = new
                {
                    role = "assistant",
                    content = ""
                }
            })
        });
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://ollama.test/")
        };
        var client = new OllamaAiClient(
            httpClient,
            Options.Create(new OllamaOptions { Model = "llama3.1" }));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.GenerateTextAsync(new AiCompletionRequestDto
            {
                Prompt = "Summarize sales"
            }));

        Assert.Equal("Ollama returned an empty response.", exception.Message);
        Assert.Equal(1, handler.CallCount);
    }

    private sealed class RecordingHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string? Body { get; private set; }

        public int CallCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Request = request;
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return response;
        }
    }
}
