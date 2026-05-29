using MarketFlow.Application.Features.AI.DTOs;
using MarketFlow.Application.Features.AI.Interfaces;
using MarketFlow.Application.Features.AI.Services;

namespace MarketFlow.Api.Tests.AI;

public sealed class AiPurchaseRecommendationServiceTests
{
    [Fact]
    public async Task GeneratePurchaseRecommendationsAsync_RecommendsOnlyProductsBelowForecastNeed()
    {
        var aiClient = new StubAiClient(new AiCompletionResponseDto
        {
            Text = """
                {"explanations":[{"productId":1,"explanation":"Buy coffee from Acme to cover demand."}]}
                """,
            Model = "test-model"
        });
        var service = new AiPurchaseRecommendationService(
            new StubAiPurchaseRecommendationDataService(
            [
                new AiPurchaseRecommendationDataDto
                {
                    ProductId = 1,
                    ProductName = "Coffee",
                    CurrentStock = 10,
                    TotalQuantitySold = 40,
                    PendingPurchaseQuantity = 5,
                    PreferredSupplierId = 7,
                    PreferredSupplierName = "Acme Supplies"
                },
                new AiPurchaseRecommendationDataDto
                {
                    ProductId = 2,
                    ProductName = "Tea",
                    CurrentStock = 100,
                    TotalQuantitySold = 20,
                    PendingPurchaseQuantity = 0,
                    PreferredSupplierId = 8,
                    PreferredSupplierName = "Tea Supplier"
                }
            ]),
            aiClient);

        var result = await service.GeneratePurchaseRecommendationsAsync(new AiPurchaseRecommendationRequest
        {
            SalesHistoryDays = 10,
            TargetStockDays = 14
        });

        Assert.True(result.Succeeded);
        var recommendation = Assert.Single(result.Data!);
        Assert.Equal(1, recommendation.ProductId);
        Assert.Equal(4m, recommendation.AverageDailySales);
        Assert.Equal(56m, recommendation.ForecastDemand);
        Assert.Equal(5, recommendation.PendingPurchaseQuantity);
        Assert.Equal(41, recommendation.RecommendedPurchaseQuantity);
        Assert.Equal(7, recommendation.PreferredSupplierId);
        Assert.Equal("Acme Supplies", recommendation.PreferredSupplierName);
        Assert.Equal("Buy coffee from Acme to cover demand.", recommendation.Explanation);
    }

    [Fact]
    public async Task GeneratePurchaseRecommendationsAsync_ConsidersPendingPurchasesAndNeverReturnsNegativeQuantity()
    {
        var aiClient = new StubAiClient(new AiCompletionResponseDto
        {
            Text = "{\"explanations\":[]}",
            Model = "test-model"
        });
        var service = new AiPurchaseRecommendationService(
            new StubAiPurchaseRecommendationDataService(
            [
                new AiPurchaseRecommendationDataDto
                {
                    ProductId = 3,
                    ProductName = "Milk",
                    CurrentStock = 4,
                    TotalQuantitySold = 10,
                    PendingPurchaseQuantity = 20
                }
            ]),
            aiClient);

        var result = await service.GeneratePurchaseRecommendationsAsync(new AiPurchaseRecommendationRequest
        {
            SalesHistoryDays = 10,
            TargetStockDays = 14
        });

        Assert.True(result.Succeeded);
        Assert.Empty(result.Data!);
        Assert.Null(aiClient.Request);
    }

    [Fact]
    public async Task GeneratePurchaseRecommendationsAsync_WhenOpenAiChangesQuantity_KeepsBackendQuantity()
    {
        var service = new AiPurchaseRecommendationService(
            new StubAiPurchaseRecommendationDataService(
            [
                new AiPurchaseRecommendationDataDto
                {
                    ProductId = 4,
                    ProductName = "Sugar",
                    CurrentStock = 0,
                    TotalQuantitySold = 30,
                    PendingPurchaseQuantity = 0
                }
            ]),
            new StubAiClient(new AiCompletionResponseDto
            {
                Text = """
                    {"explanations":[{"productId":4,"explanation":"Buy 999 units, phrased by AI only."}]}
                    """,
                Model = "test-model"
            }));

        var result = await service.GeneratePurchaseRecommendationsAsync(new AiPurchaseRecommendationRequest
        {
            SalesHistoryDays = 10,
            TargetStockDays = 14
        });

        var recommendation = Assert.Single(result.Data!);
        Assert.Equal(42, recommendation.RecommendedPurchaseQuantity);
        Assert.Contains("999", recommendation.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GeneratePurchaseRecommendationsAsync_PassesFiltersToDataService()
    {
        var dataService = new StubAiPurchaseRecommendationDataService([]);
        var service = new AiPurchaseRecommendationService(
            dataService,
            new StubAiClient(new AiCompletionResponseDto
            {
                Text = "{\"explanations\":[]}",
                Model = "test-model"
            }));

        var result = await service.GeneratePurchaseRecommendationsAsync(new AiPurchaseRecommendationRequest
        {
            MarketId = 2,
            DepartmentId = 6
        });

        Assert.True(result.Succeeded);
        Assert.NotNull(dataService.Request);
        Assert.Equal(2, dataService.Request.MarketId);
        Assert.Equal(6, dataService.Request.DepartmentId);
    }

    [Fact]
    public async Task GeneratePurchaseRecommendationsAsync_WhenRequestIsInvalid_ReturnsFailureWithoutCallingDependencies()
    {
        var dataService = new StubAiPurchaseRecommendationDataService([]);
        var aiClient = new StubAiClient(new AiCompletionResponseDto
        {
            Text = "{\"explanations\":[]}",
            Model = "test-model"
        });
        var service = new AiPurchaseRecommendationService(dataService, aiClient);

        var result = await service.GeneratePurchaseRecommendationsAsync(new AiPurchaseRecommendationRequest
        {
            TargetStockDays = 0
        });

        Assert.False(result.Succeeded);
        Assert.Null(dataService.Request);
        Assert.Null(aiClient.Request);
    }

    private sealed class StubAiPurchaseRecommendationDataService(
        IReadOnlyCollection<AiPurchaseRecommendationDataDto> data) : IAiPurchaseRecommendationDataService
    {
        public AiPurchaseRecommendationRequest? Request { get; private set; }

        public Task<IReadOnlyCollection<AiPurchaseRecommendationDataDto>> GetAiPurchaseRecommendationDataAsync(
            AiPurchaseRecommendationRequest request,
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
