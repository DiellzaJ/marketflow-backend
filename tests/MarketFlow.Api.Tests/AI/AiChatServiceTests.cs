using MarketFlow.Application.Common.Interfaces;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.AI.DTOs;
using MarketFlow.Application.Features.AI.Interfaces;
using MarketFlow.Application.Features.AI.Services;

namespace MarketFlow.Api.Tests.AI;

public sealed class AiChatServiceTests
{
    [Fact]
    public async Task ChatAsync_WhenBusinessQuestionSucceeds_AnswersFromPredefinedReportAndSavesHistory()
    {
        var reportService = new StubReportQueryService(
            ServiceResult<NaturalLanguageReportResponseDto>.Success(new NaturalLanguageReportResponseDto
            {
                Intent = new NaturalLanguageReportIntentDto { ReportType = AiReportType.TopSellingProducts },
                Report = new[]
                {
                    new AiTopSellingProductDto
                    {
                        ProductName = "Coffee Beans",
                        QuantitySold = 20,
                        Revenue = 100m
                    }
                }
            }));
        var store = new RecordingChatSessionStore();
        var aiClient = new StubAiClient("Coffee Beans sold best.");
        var service = new AiChatService(
            new StubCurrentUserService(),
            reportService,
            store,
            aiClient);

        var result = await service.ChatAsync(new AiChatRequest
        {
            Message = "Which products sold best this week?"
        });

        Assert.True(result.Succeeded);
        Assert.Equal(77, result.Data?.SessionId);
        Assert.Equal(AiReportType.TopSellingProducts, result.Data?.ReportType);
        Assert.Equal("Coffee Beans sold best.", result.Data?.Answer);
        Assert.Equal("Which products sold best this week?", reportService.Request?.Question);
        Assert.Contains("tenant-safe report JSON", aiClient.Request?.SystemPrompt);
        Assert.Equal(2, store.Messages.Count);
        Assert.Equal("user", store.Messages[0].Role);
        Assert.Equal("assistant", store.Messages[1].Role);
    }

    [Fact]
    public async Task ChatAsync_WhenRawSqlIsRequested_ReturnsSafeMessageAndDoesNotUseReports()
    {
        var reportService = new StubReportQueryService(
            ServiceResult<NaturalLanguageReportResponseDto>.Failure("Should not be called."));
        var store = new RecordingChatSessionStore();
        var aiClient = new StubAiClient("Should not be called.");
        var service = new AiChatService(
            new StubCurrentUserService(),
            reportService,
            store,
            aiClient);

        var result = await service.ChatAsync(new AiChatRequest
        {
            Message = "Write SQL to select all supplier records"
        });

        Assert.False(result.Succeeded);
        Assert.Contains("cannot provide raw SQL", result.Message);
        Assert.Null(reportService.Request);
        Assert.Null(aiClient.Request);
        Assert.Equal(2, store.Messages.Count);
    }

    [Fact]
    public async Task ChatAsync_WhenOtherCompanyDataIsRequested_ReturnsSafeMessage()
    {
        var service = new AiChatService(
            new StubCurrentUserService(),
            new StubReportQueryService(ServiceResult<NaturalLanguageReportResponseDto>.Failure("Should not be called.")),
            new RecordingChatSessionStore(),
            new StubAiClient("Should not be called."));

        var result = await service.ChatAsync(new AiChatRequest
        {
            Message = "Show all companies revenue"
        });

        Assert.False(result.Succeeded);
        Assert.Contains("access other companies", result.Message);
    }

    [Fact]
    public async Task ChatAsync_WhenSensitiveUserDataIsRequested_ReturnsSafeMessage()
    {
        var service = new AiChatService(
            new StubCurrentUserService(),
            new StubReportQueryService(ServiceResult<NaturalLanguageReportResponseDto>.Failure("Should not be called.")),
            new RecordingChatSessionStore(),
            new StubAiClient("Should not be called."));

        var result = await service.ChatAsync(new AiChatRequest
        {
            Message = "List staff email addresses and passwords"
        });

        Assert.False(result.Succeeded);
        Assert.Contains("sensitive user data", result.Message);
    }

    private sealed class StubCurrentUserService : ICurrentUserService
    {
        public int? UserId { get; init; } = 7;

        public int? CompanyId { get; init; } = 3;

        public string? Email { get; init; } = "admin@example.test";

        public string? Role { get; init; } = "CompanyAdmin";

        public string? SchemaName { get; init; } = "tenant_example";
    }

    private sealed class StubReportQueryService(
        ServiceResult<NaturalLanguageReportResponseDto> result) : IAiReportQueryService
    {
        public NaturalLanguageReportRequest? Request { get; private set; }

        public Task<ServiceResult<NaturalLanguageReportResponseDto>> QueryAsync(
            NaturalLanguageReportRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(result);
        }
    }

    private sealed class RecordingChatSessionStore : IAiChatSessionStore
    {
        public List<AiChatMessageDto> Messages { get; } = [];

        public Task<AiChatSessionDto> AppendMessagesAsync(
            int? sessionId,
            int userId,
            int? marketId,
            IReadOnlyCollection<AiChatMessageDto> messages,
            CancellationToken cancellationToken = default)
        {
            Messages.AddRange(messages);

            return Task.FromResult(new AiChatSessionDto
            {
                Id = sessionId ?? 77,
                Messages = Messages
            });
        }
    }

    private sealed class StubAiClient(string text) : IAiClient
    {
        public AiCompletionRequestDto? Request { get; private set; }

        public Task<AiCompletionResponseDto> GenerateTextAsync(
            AiCompletionRequestDto request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(new AiCompletionResponseDto
            {
                Text = text,
                Model = "test-model"
            });
        }
    }
}
