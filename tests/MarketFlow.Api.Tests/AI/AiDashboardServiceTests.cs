using MarketFlow.Application.Common.Interfaces;
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

    [Fact]
    public async Task GenerateDashboardSummaryAsync_WhenCachedForTenant_ReturnsCachedSummaryWithoutCallingOpenAi()
    {
        var cache = new RecordingAiResultCache();
        await cache.SetAsync(
            "ai:dashboard:7:2:all:2026-05-01:2026-05-31:5:10:OpenAI:gpt-4.1-mini",
            new AiDashboardSummaryResponse
            {
                Summary = "Cached tenant summary.",
                RecommendedActions = ["Cached action."],
                Model = "gpt-4.1-mini"
            });
        var aiClient = new StubAiClient(new AiCompletionResponseDto
        {
            Text = "{\"summary\":\"Fresh.\",\"recommendedActions\":[]}",
            Model = "gpt-4.1-mini"
        });
        var service = new AiDashboardService(
            new StubAiBusinessDataService(ServiceResult<AiBusinessDataDto>.Success(CreateBusinessData())),
            aiClient,
            cache,
            new StubAiRuntimeInfo("OpenAI", "gpt-4.1-mini"),
            new StubCurrentUserService(7));

        var result = await service.GenerateDashboardSummaryAsync(new AiDashboardSummaryRequest
        {
            MarketId = 2,
            From = new DateOnly(2026, 5, 1),
            To = new DateOnly(2026, 5, 31)
        });

        Assert.True(result.Succeeded);
        Assert.Equal("Cached tenant summary.", result.Data?.Summary);
        Assert.Equal(["Cached action."], result.Data?.RecommendedActions);
        Assert.Null(aiClient.Request);
        Assert.Equal("ai:dashboard:7:2:all:2026-05-01:2026-05-31:5:10:OpenAI:gpt-4.1-mini", cache.LastGetKey);
    }

    [Fact]
    public async Task GenerateDashboardSummaryAsync_DoesNotReuseAnotherTenantCacheEntry()
    {
        var cache = new RecordingAiResultCache();
        await cache.SetAsync(
            "ai:dashboard:7:all:all:all:all:5:10:OpenAI:gpt-4.1-mini",
            new AiDashboardSummaryResponse { Summary = "Tenant 7 summary.", Model = "gpt-4.1-mini" });
        var aiClient = new StubAiClient(new AiCompletionResponseDto
        {
            Text = "{\"summary\":\"Tenant 8 summary.\",\"recommendedActions\":[]}",
            Model = "gpt-4.1-mini"
        });
        var service = new AiDashboardService(
            new StubAiBusinessDataService(ServiceResult<AiBusinessDataDto>.Success(CreateBusinessData())),
            aiClient,
            cache,
            new StubAiRuntimeInfo("OpenAI", "gpt-4.1-mini"),
            new StubCurrentUserService(8));

        var result = await service.GenerateDashboardSummaryAsync(new AiDashboardSummaryRequest());

        Assert.True(result.Succeeded);
        Assert.Equal("Tenant 8 summary.", result.Data?.Summary);
        Assert.NotNull(aiClient.Request);
        Assert.Equal("ai:dashboard:8:all:all:all:all:5:10:OpenAI:gpt-4.1-mini", cache.LastGetKey);
        Assert.Contains("ai:dashboard:8:all:all:all:all:5:10:OpenAI:gpt-4.1-mini", cache.StoredKeys);
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

    private sealed class RecordingAiResultCache : IAiResultCache
    {
        private readonly Dictionary<string, object> _values = new();

        public string? LastGetKey { get; private set; }

        public IReadOnlyCollection<string> StoredKeys => _values.Keys.ToArray();

        public Task<T?> GetAsync<T>(
            string key,
            CancellationToken cancellationToken = default)
        {
            LastGetKey = key;

            return Task.FromResult(
                _values.TryGetValue(key, out var value)
                    ? (T?)value
                    : default);
        }

        public Task SetAsync<T>(
            string key,
            T value,
            TimeSpan? expiration = null,
            CancellationToken cancellationToken = default)
        {
            _values[key] = value!;
            return Task.CompletedTask;
        }

        public Task InvalidateCompanyAsync(
            int companyId,
            CancellationToken cancellationToken = default)
        {
            foreach (var key in _values.Keys.Where(key => key.StartsWith($"ai:dashboard:{companyId}:")).ToArray())
            {
                _values.Remove(key);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class StubAiRuntimeInfo(string provider, string model) : IAiRuntimeInfo
    {
        public string Provider => provider;

        public string Model => model;
    }

    private sealed class StubCurrentUserService(int companyId) : ICurrentUserService
    {
        public int? UserId => 1;

        public int? CompanyId => companyId;

        public string? Email => "ai-cache@example.test";

        public string? Role => "CompanyAdmin";

        public string? SchemaName => "tenant_test";
    }
}
