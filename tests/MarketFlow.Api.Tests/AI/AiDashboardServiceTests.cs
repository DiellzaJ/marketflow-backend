using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.AI.DTOs;
using MarketFlow.Application.Features.AI.Interfaces;
using MarketFlow.Application.Features.AI.Services;

namespace MarketFlow.Api.Tests.AI;

public sealed class AiDashboardServiceTests
{
    [Fact]
    public async Task GenerateDashboardSummaryAsync_ReturnsKpisSummaryAndActions()
    {
        var data = CreateBusinessData();
        var businessDataService = new StubAiBusinessDataService(ServiceResult<AiBusinessDataDto>.Success(data));
        var aiClient = new StubAiClient(new AiCompletionResponseDto
        {
            Text = """
                {"summary":"Revenue is strong and stock needs attention.","recommendedActions":["Restock Coffee Beans.","Promote Milk."]}
                """,
            Model = "test-model"
        });
        var service = new AiDashboardService(businessDataService, aiClient);

        var result = await service.GenerateDashboardSummaryAsync(
            new AiDashboardSummaryRequest { TopProductsLimit = 5, LowStockLimit = 10 },
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(250m, result.Data.Kpis.TotalSales);
        Assert.Equal(5, result.Data.Kpis.TotalOrders);
        Assert.Equal(50m, result.Data.Kpis.AverageOrderValue);
        Assert.Equal("Revenue is strong and stock needs attention.", result.Data.Summary);
        Assert.Equal(["Restock Coffee Beans.", "Promote Milk."], result.Data.RecommendedActions);
        Assert.Equal("test-model", result.Data.Model);
    }

    [Fact]
    public async Task GenerateDashboardSummaryAsync_SendsOnlyAggregatedDashboardDataToOpenAi()
    {
        var businessDataService = new StubAiBusinessDataService(
            ServiceResult<AiBusinessDataDto>.Success(CreateBusinessData()));
        var aiClient = new StubAiClient(new AiCompletionResponseDto
        {
            Text = "{\"summary\":\"Summary.\",\"recommendedActions\":[]}",
            Model = "test-model"
        });
        var service = new AiDashboardService(businessDataService, aiClient);

        await service.GenerateDashboardSummaryAsync(new AiDashboardSummaryRequest(), CancellationToken.None);

        Assert.NotNull(aiClient.Request);
        Assert.Contains("\"totalSales\":250", aiClient.Request.Prompt);
        Assert.Contains("Coffee Beans", aiClient.Request.Prompt);
        Assert.DoesNotContain("productId", aiClient.Request.Prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sale_id", aiClient.Request.Prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("customer", aiClient.Request.Prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerateDashboardSummaryAsync_WhenDatesAreInvalid_ReturnsFailureWithoutCallingOpenAi()
    {
        var aiClient = new StubAiClient(new AiCompletionResponseDto
        {
            Text = "unused",
            Model = "unused"
        });
        var service = new AiDashboardService(
            new StubAiBusinessDataService(ServiceResult<AiBusinessDataDto>.Success(CreateBusinessData())),
            aiClient);

        var result = await service.GenerateDashboardSummaryAsync(new AiDashboardSummaryRequest
        {
            From = new DateOnly(2026, 5, 2),
            To = new DateOnly(2026, 5, 1)
        }, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(aiClient.Request);
    }

    private static AiBusinessDataDto CreateBusinessData()
    {
        return new AiBusinessDataDto
        {
            SalesMetrics = new AiSalesMetricsDto
            {
                TotalRevenue = 250m,
                TotalSales = 5,
                AverageSaleAmount = 50m,
                TotalItemsSold = 12
            },
            TopSellingProducts =
            [
                new AiTopSellingProductDto
                {
                    ProductId = 10,
                    ProductName = "Coffee Beans",
                    QuantitySold = 8,
                    Revenue = 160m
                }
            ],
            LowStockProducts =
            [
                new AiLowStockProductDto
                {
                    ProductId = 10,
                    ProductName = "Coffee Beans",
                    MarketName = "Main Market",
                    AvailableQuantity = 2,
                    MinimumStockAlert = 5,
                    SuggestedRestockQuantity = 3
                }
            ]
        };
    }

    private sealed class StubAiBusinessDataService(
        ServiceResult<AiBusinessDataDto> result) : IAiBusinessDataService
    {
        public Task<ServiceResult<AiBusinessDataDto>> GetBusinessDataAsync(
            AiBusinessDataQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
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
