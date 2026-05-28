using MarketFlow.Application.Features.AI.DTOs;
using MarketFlow.Application.Features.AI.Interfaces;
using MarketFlow.Application.Features.AI.Services;

namespace MarketFlow.Api.Tests.AI;

public sealed class AiInventoryInsightServiceTests
{
    [Fact]
    public async Task GenerateInventoryRecommendationsAsync_DetectsLowCriticalAndOverstockProducts()
    {
        var openAiClient = new StubOpenAiClient(new AiCompletionResponseDto
        {
            Text = """
                {"explanations":[
                    {"productId":1,"explanation":"Coffee needs urgent restocking."},
                    {"productId":2,"explanation":"Milk is under the alert level."},
                    {"productId":3,"explanation":"Tea has too much stock for recent demand."}
                ]}
                """,
            Model = "test-model"
        });
        var service = new AiInventoryInsightService(
            new StubAiInventoryInsightDataService(
            [
                new AiInventoryInsightDataDto
                {
                    ProductId = 1,
                    ProductName = "Coffee",
                    CurrentStock = 2,
                    MinimumStockAlert = 10,
                    TotalQuantitySold = 30
                },
                new AiInventoryInsightDataDto
                {
                    ProductId = 2,
                    ProductName = "Milk",
                    CurrentStock = 8,
                    MinimumStockAlert = 10,
                    TotalQuantitySold = 20
                },
                new AiInventoryInsightDataDto
                {
                    ProductId = 3,
                    ProductName = "Tea",
                    CurrentStock = 100,
                    MinimumStockAlert = 10,
                    TotalQuantitySold = 10
                }
            ]),
            openAiClient);

        var result = await service.GenerateInventoryRecommendationsAsync(new AiInventoryRecommendationRequest
        {
            SalesHistoryDays = 10,
            TargetStockDays = 14,
            OverstockDays = 60
        });

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(3, result.Data.Count);

        var critical = result.Data.Single(recommendation => recommendation.ProductId == 1);
        Assert.Equal("CriticalLowStock", critical.IssueType);
        Assert.Equal("Critical", critical.UrgencyLevel);
        Assert.Equal(3m, critical.AverageDailySales);
        Assert.Equal(40, critical.RecommendedPurchaseQuantity);
        Assert.Equal("Coffee needs urgent restocking.", critical.Explanation);

        var lowStock = result.Data.Single(recommendation => recommendation.ProductId == 2);
        Assert.Equal("LowStock", lowStock.IssueType);
        Assert.Equal("High", lowStock.UrgencyLevel);
        Assert.Equal(20, lowStock.RecommendedPurchaseQuantity);

        var overstock = result.Data.Single(recommendation => recommendation.ProductId == 3);
        Assert.Equal("Overstock", overstock.IssueType);
        Assert.Equal("Medium", overstock.UrgencyLevel);
        Assert.Equal(0, overstock.RecommendedPurchaseQuantity);
    }

    [Fact]
    public async Task GenerateInventoryRecommendationsAsync_WhenOpenAiChangesNumbers_KeepsBackendCalculations()
    {
        var service = new AiInventoryInsightService(
            new StubAiInventoryInsightDataService(
            [
                new AiInventoryInsightDataDto
                {
                    ProductId = 5,
                    ProductName = "Sugar",
                    CurrentStock = 1,
                    MinimumStockAlert = 10,
                    TotalQuantitySold = 50
                }
            ]),
            new StubOpenAiClient(new AiCompletionResponseDto
            {
                Text = """
                    {"explanations":[{"productId":5,"explanation":"Buy 999 units even though this is just explanatory text."}]}
                    """,
                Model = "test-model"
            }));

        var result = await service.GenerateInventoryRecommendationsAsync(new AiInventoryRecommendationRequest
        {
            SalesHistoryDays = 10,
            TargetStockDays = 14,
            OverstockDays = 60
        });

        var recommendation = Assert.Single(result.Data!);
        Assert.Equal(69, recommendation.RecommendedPurchaseQuantity);
        Assert.Equal("Critical", recommendation.UrgencyLevel);
        Assert.Contains("999", recommendation.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateInventoryRecommendationsAsync_PassesFiltersToDataService()
    {
        var dataService = new StubAiInventoryInsightDataService([]);
        var service = new AiInventoryInsightService(
            dataService,
            new StubOpenAiClient(new AiCompletionResponseDto
            {
                Text = "{\"explanations\":[]}",
                Model = "test-model"
            }));

        var result = await service.GenerateInventoryRecommendationsAsync(new AiInventoryRecommendationRequest
        {
            MarketId = 2,
            DepartmentId = 4
        });

        Assert.True(result.Succeeded);
        Assert.NotNull(dataService.Request);
        Assert.Equal(2, dataService.Request.MarketId);
        Assert.Equal(4, dataService.Request.DepartmentId);
    }

    [Fact]
    public async Task GenerateInventoryRecommendationsAsync_WhenValuesAreInvalid_ReturnsFailureWithoutCallingDependencies()
    {
        var dataService = new StubAiInventoryInsightDataService([]);
        var openAiClient = new StubOpenAiClient(new AiCompletionResponseDto
        {
            Text = "{\"explanations\":[]}",
            Model = "test-model"
        });
        var service = new AiInventoryInsightService(dataService, openAiClient);

        var result = await service.GenerateInventoryRecommendationsAsync(new AiInventoryRecommendationRequest
        {
            SalesHistoryDays = 0
        });

        Assert.False(result.Succeeded);
        Assert.Null(dataService.Request);
        Assert.Null(openAiClient.Request);
    }

    private sealed class StubAiInventoryInsightDataService(
        IReadOnlyCollection<AiInventoryInsightDataDto> data) : IAiInventoryInsightDataService
    {
        public AiInventoryRecommendationRequest? Request { get; private set; }

        public Task<IReadOnlyCollection<AiInventoryInsightDataDto>> GetAiInventoryInsightDataAsync(
            AiInventoryRecommendationRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(data);
        }
    }

    private sealed class StubOpenAiClient(AiCompletionResponseDto response) : IOpenAiClient
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
