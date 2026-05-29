using MarketFlow.Application.Features.AI.DTOs;
using MarketFlow.Application.Features.AI.Services;

namespace MarketFlow.Api.Tests.AI;

public sealed class AiCacheKeyTests
{
    [Fact]
    public void Dashboard_IncludesTenantFiltersRangeProviderAndModel()
    {
        var key = AiCacheKeys.Dashboard(
            42,
            new AiDashboardSummaryRequest
            {
                MarketId = 2,
                DepartmentId = 3,
                From = new DateOnly(2026, 5, 1),
                To = new DateOnly(2026, 5, 31),
                TopProductsLimit = 5,
                LowStockLimit = 10
            },
            "OpenAI",
            "gpt-4.1-mini");

        Assert.Equal("ai:dashboard:42:2:3:2026-05-01:2026-05-31:5:10:OpenAI:gpt-4.1-mini", key);
    }

    [Fact]
    public void Inventory_IncludesTenantMarketDepartmentProviderAndModel()
    {
        var key = AiCacheKeys.Inventory(
            42,
            new AiInventoryRecommendationRequest
            {
                MarketId = 2,
                DepartmentId = 3,
                SalesHistoryDays = 30,
                TargetStockDays = 14,
                OverstockDays = 60
            },
            "Ollama",
            "llama3.1");

        Assert.Equal("ai:inventory:42:2:3:30:14:60:Ollama:llama3.1", key);
    }

    [Fact]
    public void Supplier_IncludesTenantFiltersRangeProviderAndModel()
    {
        var key = AiCacheKeys.Supplier(
            42,
            new AiSupplierInsightRequest
            {
                MarketId = 2,
                From = new DateOnly(2026, 5, 1),
                To = new DateOnly(2026, 5, 31)
            },
            "Fake",
            "fake-ai-development");

        Assert.Equal("ai:supplier:42:2:2026-05-01:2026-05-31:Fake:fake-ai-development", key);
    }

    [Fact]
    public void CompanyPrefixes_AreTenantScoped()
    {
        var prefixes = AiCacheKeys.CompanyPrefixes(42);

        Assert.Equal(
            ["ai:dashboard:42:", "ai:inventory:42:", "ai:supplier:42:"],
            prefixes);
    }
}
