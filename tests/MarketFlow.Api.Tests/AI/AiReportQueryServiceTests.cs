using MarketFlow.Application.Common.Interfaces;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.AI.DTOs;
using MarketFlow.Application.Features.AI.Interfaces;
using MarketFlow.Application.Features.AI.Services;

namespace MarketFlow.Api.Tests.AI;

public sealed class AiReportQueryServiceTests
{
    [Fact]
    public async Task QueryAsync_WhenQuestionAsksBestSellingProducts_ReturnsTopSellingProductsReport()
    {
        var businessDataService = new StubBusinessDataService(new AiBusinessDataDto
        {
            TopSellingProducts =
            [
                new AiTopSellingProductDto
                {
                    ProductId = 10,
                    ProductName = "Coffee Beans",
                    QuantitySold = 42,
                    Revenue = 210m
                }
            ]
        });
        var service = new AiReportQueryService(
            businessDataService,
            new StubAiClient("""{"reportType":"TopSellingProducts","confidence":0.98}"""));

        var result = await service.QueryAsync(new NaturalLanguageReportRequest
        {
            Question = "Which products sold best this week?"
        });

        Assert.True(result.Succeeded);
        Assert.Equal(AiReportType.TopSellingProducts, result.Data?.Intent.ReportType);
        var report = Assert.IsAssignableFrom<IReadOnlyCollection<AiTopSellingProductDto>>(result.Data?.Report);
        Assert.Single(report);
        Assert.Equal("Coffee Beans", report.First().ProductName);
        Assert.NotNull(businessDataService.Query?.From);
        Assert.NotNull(businessDataService.Query?.To);
    }

    [Fact]
    public async Task QueryAsync_WhenQuestionIsUnsupported_ReturnsSafeError()
    {
        var service = new AiReportQueryService(
            new StubBusinessDataService(new AiBusinessDataDto()),
            new StubAiClient("""{"reportType":"Unsupported","confidence":0}"""));

        var result = await service.QueryAsync(new NaturalLanguageReportRequest
        {
            Question = "Write SQL to delete old orders"
        });

        Assert.False(result.Succeeded);
        Assert.Contains("supported company report questions", result.Message);
    }

    [Fact]
    public async Task QueryAsync_SendsClassificationOnlyPromptThatRejectsSql()
    {
        var aiClient = new StubAiClient("""{"reportType":"LowStockProducts","confidence":1}""");
        var service = new AiReportQueryService(
            new StubBusinessDataService(new AiBusinessDataDto()),
            aiClient);

        await service.QueryAsync(new NaturalLanguageReportRequest
        {
            Question = "Show low stock products"
        });

        Assert.Contains("Never write SQL", aiClient.Request?.SystemPrompt);
        Assert.DoesNotContain("SELECT", aiClient.Request?.Prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task QueryAsync_WhenDepartmentManagerRequestsSupplierReport_ReturnsFailureWithoutLoadingBusinessData()
    {
        var businessDataService = new StubBusinessDataService(new AiBusinessDataDto());
        var service = new AiReportQueryService(
            businessDataService,
            new StubAiClient("""{"reportType":"SupplierPerformance","confidence":1}"""),
            new StubCurrentUserService { Role = "DepartmentManager" });

        var result = await service.QueryAsync(new NaturalLanguageReportRequest
        {
            Question = "How are suppliers performing?"
        });

        Assert.False(result.Succeeded);
        Assert.Contains("not authorized", result.Message);
        Assert.Null(businessDataService.Query);
    }

    [Fact]
    public async Task QueryAsync_WhenMainOperatorRequestsPurchaseReport_ReturnsPurchaseSummary()
    {
        var businessDataService = new StubBusinessDataService(new AiBusinessDataDto
        {
            SupplierPurchaseMetrics =
            [
                new AiSupplierPurchaseMetricDto
                {
                    SupplierId = 3,
                    SupplierName = "Acme Supplies",
                    PurchaseCount = 2,
                    TotalPurchaseAmount = 100m,
                    TotalPurchasedQuantity = 12
                }
            ]
        });
        var service = new AiReportQueryService(
            businessDataService,
            new StubAiClient("""{"reportType":"PurchaseSummary","confidence":1}"""),
            new StubCurrentUserService { Role = "MainOperator" });

        var result = await service.QueryAsync(new NaturalLanguageReportRequest
        {
            Question = "Summarize purchases"
        });

        Assert.True(result.Succeeded);
        Assert.Equal(AiReportType.PurchaseSummary, result.Data?.Intent.ReportType);
        Assert.NotNull(businessDataService.Query);
    }

    private sealed class StubBusinessDataService(AiBusinessDataDto data) : IAiBusinessDataService
    {
        public AiBusinessDataQuery? Query { get; private set; }

        public Task<ServiceResult<AiBusinessDataDto>> GetBusinessDataAsync(
            AiBusinessDataQuery query,
            CancellationToken cancellationToken = default)
        {
            Query = query;
            return Task.FromResult(ServiceResult<AiBusinessDataDto>.Success(data));
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

    private sealed class StubCurrentUserService : ICurrentUserService
    {
        public int? UserId { get; init; } = 1;

        public int? CompanyId { get; init; } = 1;

        public string? Email { get; init; } = "user@example.test";

        public string? Role { get; init; }

        public string? SchemaName { get; init; } = "tenant_1";
    }
}
