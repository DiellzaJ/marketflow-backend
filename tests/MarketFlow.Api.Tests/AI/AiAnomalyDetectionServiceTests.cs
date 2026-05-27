using MarketFlow.Application.Features.AI.DTOs;
using MarketFlow.Application.Features.AI.Interfaces;
using MarketFlow.Application.Features.AI.Services;

namespace MarketFlow.Api.Tests.AI;

public sealed class AiAnomalyDetectionServiceTests
{
    [Fact]
    public async Task DetectAnomaliesAsync_WhenDataIsNormal_ReturnsNoAnomaliesWithoutCallingOpenAi()
    {
        var openAiClient = new StubOpenAiClient(new AiCompletionResponseDto
        {
            Text = "{\"explanations\":[]}",
            Model = "test-model"
        });
        var service = new AiAnomalyDetectionService(
            new StubAiAnomalyDetectionDataService(new AiAnomalyDetectionDataDto
            {
                Sales =
                [
                    new AiAnomalySaleDataDto
                    {
                        SaleId = 1,
                        ReferenceNumber = "SALE-000001",
                        SaleDate = new DateOnly(2026, 5, 1),
                        TotalAmount = 95m,
                        DiscountAmount = 5m
                    }
                ],
                SaleItems =
                [
                    new AiAnomalySaleItemDataDto
                    {
                        SaleId = 1,
                        ReferenceNumber = "SALE-000001",
                        SaleDate = new DateOnly(2026, 5, 1),
                        ProductId = 10,
                        ProductName = "Coffee",
                        Quantity = 2,
                        UnitPrice = 12m,
                        CostPrice = 8m
                    }
                ],
                StockMovements =
                [
                    new AiAnomalyStockMovementDataDto
                    {
                        MovementId = 1,
                        MovementType = "SaleCompleted",
                        QuantityChanged = -2,
                        ReferenceNumber = "sale:1",
                        CreatedAt = DateTimeOffset.UtcNow,
                        ProductId = 10,
                        ProductName = "Coffee",
                        HasMatchingReference = true
                    }
                ],
                DailyProductSales =
                [
                    new AiAnomalyDailyProductSalesDataDto
                    {
                        ProductId = 10,
                        ProductName = "Coffee",
                        SaleDate = new DateOnly(2026, 5, 1),
                        QuantitySold = 4,
                        AverageDailyQuantity = 3m
                    }
                ]
            }),
            openAiClient);

        var result = await service.DetectAnomaliesAsync(new AiAnomalyDetectionRequest());

        Assert.True(result.Succeeded);
        Assert.Empty(result.Data!);
        Assert.Null(openAiClient.Request);
    }

    [Fact]
    public async Task DetectAnomaliesAsync_DetectsConfiguredRuleBasedAnomalies()
    {
        var service = new AiAnomalyDetectionService(
            new StubAiAnomalyDetectionDataService(CreateAnomalyData()),
            new StubOpenAiClient(new AiCompletionResponseDto
            {
                Text = """
                    {"explanations":[
                        {"index":0,"explanation":"This entry needs review."},
                        {"index":1,"explanation":"This price needs review."},
                        {"index":2,"explanation":"This movement needs review."},
                        {"index":3,"explanation":"This reference needs review."},
                        {"index":4,"explanation":"This spike needs review."}
                    ]}
                    """,
                Model = "test-model"
            }));

        var result = await service.DetectAnomaliesAsync(new AiAnomalyDetectionRequest
        {
            HighDiscountRateThreshold = 0.30m,
            LargeStockMovementThreshold = 100,
            SalesSpikeMultiplier = 3m,
            MinimumSalesSpikeQuantity = 10
        });

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Contains(result.Data, anomaly => anomaly.AnomalyType == "HighDiscount");
        Assert.Contains(result.Data, anomaly => anomaly.AnomalyType == "BelowCostSale");
        Assert.Contains(result.Data, anomaly => anomaly.AnomalyType == "LargeStockAdjustment");
        Assert.Contains(result.Data, anomaly => anomaly.AnomalyType == "UnmatchedStockMovement");
        Assert.Contains(result.Data, anomaly => anomaly.AnomalyType == "SalesSpike");
    }

    [Fact]
    public async Task DetectAnomaliesAsync_SanitizesAccusatoryAiWording()
    {
        var service = new AiAnomalyDetectionService(
            new StubAiAnomalyDetectionDataService(new AiAnomalyDetectionDataDto
            {
                Sales =
                [
                    new AiAnomalySaleDataDto
                    {
                        SaleId = 1,
                        ReferenceNumber = "SALE-000001",
                        SaleDate = new DateOnly(2026, 5, 1),
                        TotalAmount = 50m,
                        DiscountAmount = 50m
                    }
                ]
            }),
            new StubOpenAiClient(new AiCompletionResponseDto
            {
                Text = """
                    {"explanations":[{"index":0,"explanation":"This looks like fraud by the cashier."}]}
                    """,
                Model = "test-model"
            }));

        var result = await service.DetectAnomaliesAsync(new AiAnomalyDetectionRequest());

        var anomaly = Assert.Single(result.Data!);
        Assert.DoesNotContain("fraud", anomaly.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("review", anomaly.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DetectAnomaliesAsync_PassesFiltersToDataService()
    {
        var dataService = new StubAiAnomalyDetectionDataService(new AiAnomalyDetectionDataDto());
        var service = new AiAnomalyDetectionService(
            dataService,
            new StubOpenAiClient(new AiCompletionResponseDto
            {
                Text = "{\"explanations\":[]}",
                Model = "test-model"
            }));

        var result = await service.DetectAnomaliesAsync(new AiAnomalyDetectionRequest
        {
            From = new DateOnly(2026, 5, 1),
            To = new DateOnly(2026, 5, 31),
            MarketId = 2,
            DepartmentId = 3
        });

        Assert.True(result.Succeeded);
        Assert.NotNull(dataService.Request);
        Assert.Equal(new DateOnly(2026, 5, 1), dataService.Request.From);
        Assert.Equal(new DateOnly(2026, 5, 31), dataService.Request.To);
        Assert.Equal(2, dataService.Request.MarketId);
        Assert.Equal(3, dataService.Request.DepartmentId);
    }

    [Fact]
    public async Task DetectAnomaliesAsync_WhenRequestIsInvalid_ReturnsFailureWithoutCallingDependencies()
    {
        var dataService = new StubAiAnomalyDetectionDataService(new AiAnomalyDetectionDataDto());
        var openAiClient = new StubOpenAiClient(new AiCompletionResponseDto
        {
            Text = "{\"explanations\":[]}",
            Model = "test-model"
        });
        var service = new AiAnomalyDetectionService(dataService, openAiClient);

        var result = await service.DetectAnomaliesAsync(new AiAnomalyDetectionRequest
        {
            From = new DateOnly(2026, 5, 31),
            To = new DateOnly(2026, 5, 1)
        });

        Assert.False(result.Succeeded);
        Assert.Null(dataService.Request);
        Assert.Null(openAiClient.Request);
    }

    private static AiAnomalyDetectionDataDto CreateAnomalyData() =>
        new()
        {
            Sales =
            [
                new AiAnomalySaleDataDto
                {
                    SaleId = 1,
                    ReferenceNumber = "SALE-000001",
                    SaleDate = new DateOnly(2026, 5, 1),
                    TotalAmount = 60m,
                    DiscountAmount = 40m
                }
            ],
            SaleItems =
            [
                new AiAnomalySaleItemDataDto
                {
                    SaleId = 2,
                    ReferenceNumber = "SALE-000002",
                    SaleDate = new DateOnly(2026, 5, 2),
                    ProductId = 20,
                    ProductName = "Milk",
                    Quantity = 1,
                    UnitPrice = 4m,
                    CostPrice = 6m
                }
            ],
            StockMovements =
            [
                new AiAnomalyStockMovementDataDto
                {
                    MovementId = 3,
                    MovementType = "ManualAdjustment",
                    QuantityChanged = 150,
                    ReferenceNumber = "manual:3",
                    CreatedAt = new DateTimeOffset(2026, 5, 3, 0, 0, 0, TimeSpan.Zero),
                    ProductId = 30,
                    ProductName = "Tea",
                    HasMatchingReference = true
                },
                new AiAnomalyStockMovementDataDto
                {
                    MovementId = 4,
                    MovementType = "SaleCompleted",
                    QuantityChanged = -5,
                    ReferenceNumber = "sale:999",
                    CreatedAt = new DateTimeOffset(2026, 5, 4, 0, 0, 0, TimeSpan.Zero),
                    ProductId = 40,
                    ProductName = "Sugar",
                    HasMatchingReference = false
                }
            ],
            DailyProductSales =
            [
                new AiAnomalyDailyProductSalesDataDto
                {
                    ProductId = 50,
                    ProductName = "Flour",
                    SaleDate = new DateOnly(2026, 5, 5),
                    QuantitySold = 30,
                    AverageDailyQuantity = 5m
                }
            ]
        };

    private sealed class StubAiAnomalyDetectionDataService(
        AiAnomalyDetectionDataDto data) : IAiAnomalyDetectionDataService
    {
        public AiAnomalyDetectionRequest? Request { get; private set; }

        public Task<AiAnomalyDetectionDataDto> GetAiAnomalyDetectionDataAsync(
            AiAnomalyDetectionRequest request,
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
