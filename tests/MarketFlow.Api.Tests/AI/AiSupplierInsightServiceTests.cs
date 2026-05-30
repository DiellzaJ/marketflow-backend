using MarketFlow.Application.Features.AI.DTOs;
using MarketFlow.Application.Features.AI.Interfaces;
using MarketFlow.Application.Features.AI.Services;

namespace MarketFlow.Api.Tests.AI;

public sealed class AiSupplierInsightServiceTests
{
    [Fact]
    public async Task GenerateSupplierPerformanceInsightsAsync_CalculatesReliabilityFlagsAndRecommendationText()
    {
        var aiClient = new StubAiClient(new AiCompletionResponseDto
        {
            Text = """
                {"recommendations":[
                    {"supplierId":1,"recommendation":"Review Acme before future large orders."},
                    {"supplierId":2,"recommendation":"Monitor Fresh Foods delivery speed."},
                    {"supplierId":3,"recommendation":"Keep using Local Goods for routine purchases."}
                ]}
                """,
            Model = "test-model"
        });
        var service = new AiSupplierInsightService(
            new StubAiSupplierInsightDataService(
            [
                new AiSupplierInsightDataDto
                {
                    SupplierId = 1,
                    SupplierName = "Acme",
                    TotalPurchases = 10,
                    ReceivedPurchases = 5,
                    CancelledPurchases = 4,
                    AverageDeliveryDays = 3m,
                    TotalAmountSpent = 500m
                },
                new AiSupplierInsightDataDto
                {
                    SupplierId = 2,
                    SupplierName = "Fresh Foods",
                    TotalPurchases = 6,
                    ReceivedPurchases = 6,
                    CancelledPurchases = 0,
                    AverageDeliveryDays = 9m,
                    TotalAmountSpent = 900m
                },
                new AiSupplierInsightDataDto
                {
                    SupplierId = 3,
                    SupplierName = "Local Goods",
                    TotalPurchases = 4,
                    ReceivedPurchases = 4,
                    CancelledPurchases = 0,
                    AverageDeliveryDays = 2m,
                    TotalAmountSpent = 300m
                }
            ]),
            aiClient);

        var result = await service.GenerateSupplierPerformanceInsightsAsync(new AiSupplierInsightRequest());

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);

        var highCancellation = result.Data.Single(insight => insight.SupplierId == 1);
        Assert.Equal(0.4m, highCancellation.CancellationRate);
        Assert.Equal("Low", highCancellation.ReliabilityLevel);
        Assert.Contains("HighCancellationRate", highCancellation.Flags);
        Assert.Equal("Review Acme before future large orders.", highCancellation.RecommendationText);

        var slowDelivery = result.Data.Single(insight => insight.SupplierId == 2);
        Assert.Equal("Medium", slowDelivery.ReliabilityLevel);
        Assert.Contains("SlowDelivery", slowDelivery.Flags);

        var reliable = result.Data.Single(insight => insight.SupplierId == 3);
        Assert.Equal("High", reliable.ReliabilityLevel);
        Assert.Empty(reliable.Flags);
    }

    [Fact]
    public async Task GenerateSupplierPerformanceInsightsAsync_SendsOnlyCalculatedMetricsToOpenAi()
    {
        var aiClient = new StubAiClient(new AiCompletionResponseDto
        {
            Text = "{\"recommendations\":[]}",
            Model = "test-model"
        });
        var service = new AiSupplierInsightService(
            new StubAiSupplierInsightDataService(
            [
                new AiSupplierInsightDataDto
                {
                    SupplierId = 10,
                    SupplierName = "Warehouse Partner",
                    TotalPurchases = 3,
                    ReceivedPurchases = 2,
                    CancelledPurchases = 1,
                    AverageDeliveryDays = 5m,
                    TotalAmountSpent = 250m
                }
            ]),
            aiClient);

        await service.GenerateSupplierPerformanceInsightsAsync(new AiSupplierInsightRequest());

        Assert.NotNull(aiClient.Request);
        Assert.Contains("Warehouse Partner", aiClient.Request.Prompt, StringComparison.Ordinal);
        Assert.Contains("totalPurchases", aiClient.Request.Prompt, StringComparison.Ordinal);
        Assert.Contains("reliabilityLevel", aiClient.Request.Prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("purchaseId", aiClient.Request.Prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("createdBy", aiClient.Request.Prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerateSupplierPerformanceInsightsAsync_PassesFiltersToDataService()
    {
        var dataService = new StubAiSupplierInsightDataService([]);
        var service = new AiSupplierInsightService(
            dataService,
            new StubAiClient(new AiCompletionResponseDto
            {
                Text = "{\"recommendations\":[]}",
                Model = "test-model"
            }));

        var result = await service.GenerateSupplierPerformanceInsightsAsync(new AiSupplierInsightRequest
        {
            From = new DateOnly(2026, 5, 1),
            To = new DateOnly(2026, 5, 31),
            MarketId = 2
        });

        Assert.True(result.Succeeded);
        Assert.NotNull(dataService.Request);
        Assert.Equal(new DateOnly(2026, 5, 1), dataService.Request.From);
        Assert.Equal(new DateOnly(2026, 5, 31), dataService.Request.To);
        Assert.Equal(2, dataService.Request.MarketId);
    }

    [Fact]
    public async Task GenerateSupplierPerformanceInsightsAsync_WhenRequestIsInvalid_ReturnsFailureWithoutCallingDependencies()
    {
        var dataService = new StubAiSupplierInsightDataService([]);
        var aiClient = new StubAiClient(new AiCompletionResponseDto
        {
            Text = "{\"recommendations\":[]}",
            Model = "test-model"
        });
        var service = new AiSupplierInsightService(dataService, aiClient);

        var result = await service.GenerateSupplierPerformanceInsightsAsync(new AiSupplierInsightRequest
        {
            From = new DateOnly(2026, 5, 31),
            To = new DateOnly(2026, 5, 1)
        });

        Assert.False(result.Succeeded);
        Assert.Null(dataService.Request);
        Assert.Null(aiClient.Request);
    }

    private sealed class StubAiSupplierInsightDataService(
        IReadOnlyCollection<AiSupplierInsightDataDto> data) : IAiSupplierInsightDataService
    {
        public AiSupplierInsightRequest? Request { get; private set; }

        public Task<IReadOnlyCollection<AiSupplierInsightDataDto>> GetAiSupplierInsightDataAsync(
            AiSupplierInsightRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(data);
        }
    }

    private sealed class StubAiClient(AiCompletionResponseDto response) : IAiClient
    {
        public AiCompletionRequestDto? Request { get; private set; }

        public Task<AiCompletionResponseDto> GenerateTextAsync(
            AiCompletionRequestDto request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(response);
        }
    }
}
