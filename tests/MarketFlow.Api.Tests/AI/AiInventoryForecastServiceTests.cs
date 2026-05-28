using MarketFlow.Application.Features.AI.DTOs;
using MarketFlow.Application.Features.AI.Interfaces;
using MarketFlow.Application.Features.AI.Services;

namespace MarketFlow.Api.Tests.AI;

public sealed class AiInventoryForecastServiceTests
{
    [Fact]
    public async Task GenerateInventoryForecastAsync_CalculatesDemandPerProduct()
    {
        var service = new AiInventoryForecastService(new StubTenantQueryService(
        [
            new AiInventoryForecastDataDto
            {
                ProductId = 10,
                ProductName = "Coffee Beans",
                CurrentStock = 60,
                TotalQuantitySold = 30
            }
        ]));

        var result = await service.GenerateInventoryForecastAsync(new AiInventoryForecastRequest
        {
            SalesHistoryDays = 10,
            ForecastDays = 7
        });

        Assert.True(result.Succeeded);
        var product = Assert.Single(result.Data!.Products);
        Assert.Equal(3m, product.AverageDailySales);
        Assert.Equal(21m, product.ForecastDemand);
        Assert.Equal(20m, product.DaysOfStockRemaining);
    }

    [Fact]
    public async Task GenerateInventoryForecastAsync_PassesMarketAndDepartmentFilters()
    {
        var tenantQueryService = new StubTenantQueryService([]);
        var service = new AiInventoryForecastService(tenantQueryService);

        var result = await service.GenerateInventoryForecastAsync(new AiInventoryForecastRequest
        {
            SalesHistoryDays = 14,
            ForecastDays = 30,
            MarketId = 3,
            DepartmentId = 8
        });

        Assert.True(result.Succeeded);
        Assert.NotNull(tenantQueryService.Request);
        Assert.Equal(3, tenantQueryService.Request.MarketId);
        Assert.Equal(8, tenantQueryService.Request.DepartmentId);
        Assert.Equal(3, result.Data!.MarketId);
        Assert.Equal(8, result.Data.DepartmentId);
    }

    [Fact]
    public async Task GenerateInventoryForecastAsync_HandlesProductsWithNoSalesHistorySafely()
    {
        var service = new AiInventoryForecastService(new StubTenantQueryService(
        [
            new AiInventoryForecastDataDto
            {
                ProductId = 20,
                ProductName = "Milk",
                CurrentStock = 12,
                TotalQuantitySold = 0
            }
        ]));

        var result = await service.GenerateInventoryForecastAsync(new AiInventoryForecastRequest
        {
            SalesHistoryDays = 30,
            ForecastDays = 7
        });

        Assert.True(result.Succeeded);
        var product = Assert.Single(result.Data!.Products);
        Assert.Equal(0m, product.AverageDailySales);
        Assert.Equal(0m, product.ForecastDemand);
        Assert.Null(product.DaysOfStockRemaining);
    }

    [Fact]
    public async Task GenerateInventoryForecastAsync_WhenRequestValuesAreInvalid_ReturnsFailure()
    {
        var tenantQueryService = new StubTenantQueryService([]);
        var service = new AiInventoryForecastService(tenantQueryService);

        var result = await service.GenerateInventoryForecastAsync(new AiInventoryForecastRequest
        {
            SalesHistoryDays = 0,
            ForecastDays = 7
        });

        Assert.False(result.Succeeded);
        Assert.Null(tenantQueryService.Request);
    }

    private sealed class StubTenantQueryService(
        IReadOnlyCollection<AiInventoryForecastDataDto> forecastData) : IAiInventoryForecastDataService
    {
        public AiInventoryForecastRequest? Request { get; private set; }

        public Task<IReadOnlyCollection<AiInventoryForecastDataDto>> GetAiInventoryForecastDataAsync(
            AiInventoryForecastRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(forecastData);
        }
    }
}
