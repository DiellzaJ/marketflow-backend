using MarketFlow.Api.Controllers;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.AI.DTOs;
using MarketFlow.Application.Features.AI.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace MarketFlow.Api.Tests.AI;

public sealed class AiControllerTests
{
    [Fact]
    public async Task GenerateDashboardSummaryAsync_WhenRequestSucceeds_ReturnsOk()
    {
        var response = new AiDashboardSummaryResponse
        {
            Summary = "Sales are healthy.",
            RecommendedActions = ["Restock top items."]
        };
        var controller = new AiController(
            new StubAiDashboardService(ServiceResult<AiDashboardSummaryResponse>.Success(response)),
            new StubAiInventoryForecastService(
                ServiceResult<AiInventoryForecastResponse>.Success(new AiInventoryForecastResponse())),
            new StubAiInventoryInsightService(
                ServiceResult<IReadOnlyCollection<AiInventoryRecommendationDto>>.Success([])),
            new StubAiPurchaseRecommendationService(
                ServiceResult<IReadOnlyCollection<AiPurchaseRecommendationDto>>.Success([])),
            new StubAiSupplierInsightService(
                ServiceResult<IReadOnlyCollection<AiSupplierInsightDto>>.Success([])));

        var result = await controller.GenerateDashboardSummaryAsync(
            new AiDashboardSummaryRequest(),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var serviceResult = Assert.IsType<ServiceResult<AiDashboardSummaryResponse>>(ok.Value);
        Assert.True(serviceResult.Succeeded);
        Assert.Same(response, serviceResult.Data);
    }

    [Fact]
    public async Task GenerateDashboardSummaryAsync_WhenRequestFails_ReturnsBadRequest()
    {
        var expectedResult = ServiceResult<AiDashboardSummaryResponse>.Failure(
            "From date must be on or before to date.");
        var controller = new AiController(
            new StubAiDashboardService(expectedResult),
            new StubAiInventoryForecastService(
                ServiceResult<AiInventoryForecastResponse>.Success(new AiInventoryForecastResponse())),
            new StubAiInventoryInsightService(
                ServiceResult<IReadOnlyCollection<AiInventoryRecommendationDto>>.Success([])),
            new StubAiPurchaseRecommendationService(
                ServiceResult<IReadOnlyCollection<AiPurchaseRecommendationDto>>.Success([])),
            new StubAiSupplierInsightService(
                ServiceResult<IReadOnlyCollection<AiSupplierInsightDto>>.Success([])));

        var result = await controller.GenerateDashboardSummaryAsync(
            new AiDashboardSummaryRequest(),
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Same(expectedResult, badRequest.Value);
    }

    [Fact]
    public async Task GenerateInventoryRecommendationsAsync_WhenRequestSucceeds_ReturnsOk()
    {
        IReadOnlyCollection<AiInventoryRecommendationDto> response =
        [
            new AiInventoryRecommendationDto
            {
                ProductId = 10,
                ProductName = "Coffee Beans",
                IssueType = "CriticalLowStock",
                UrgencyLevel = "Critical"
            }
        ];
        var controller = new AiController(
            new StubAiDashboardService(
                ServiceResult<AiDashboardSummaryResponse>.Success(new AiDashboardSummaryResponse())),
            new StubAiInventoryForecastService(
                ServiceResult<AiInventoryForecastResponse>.Success(new AiInventoryForecastResponse())),
            new StubAiInventoryInsightService(
                ServiceResult<IReadOnlyCollection<AiInventoryRecommendationDto>>.Success(response)),
            new StubAiPurchaseRecommendationService(
                ServiceResult<IReadOnlyCollection<AiPurchaseRecommendationDto>>.Success([])),
            new StubAiSupplierInsightService(
                ServiceResult<IReadOnlyCollection<AiSupplierInsightDto>>.Success([])));

        var result = await controller.GenerateInventoryRecommendationsAsync(
            new AiInventoryRecommendationRequest(),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var serviceResult = Assert.IsType<ServiceResult<IReadOnlyCollection<AiInventoryRecommendationDto>>>(ok.Value);
        Assert.True(serviceResult.Succeeded);
        Assert.Same(response, serviceResult.Data);
    }

    [Fact]
    public async Task GeneratePurchaseRecommendationsAsync_WhenRequestSucceeds_ReturnsOk()
    {
        IReadOnlyCollection<AiPurchaseRecommendationDto> response =
        [
            new AiPurchaseRecommendationDto
            {
                ProductId = 20,
                ProductName = "Milk",
                RecommendedPurchaseQuantity = 12
            }
        ];
        var controller = new AiController(
            new StubAiDashboardService(
                ServiceResult<AiDashboardSummaryResponse>.Success(new AiDashboardSummaryResponse())),
            new StubAiInventoryForecastService(
                ServiceResult<AiInventoryForecastResponse>.Success(new AiInventoryForecastResponse())),
            new StubAiInventoryInsightService(
                ServiceResult<IReadOnlyCollection<AiInventoryRecommendationDto>>.Success([])),
            new StubAiPurchaseRecommendationService(
                ServiceResult<IReadOnlyCollection<AiPurchaseRecommendationDto>>.Success(response)),
            new StubAiSupplierInsightService(
                ServiceResult<IReadOnlyCollection<AiSupplierInsightDto>>.Success([])));

        var result = await controller.GeneratePurchaseRecommendationsAsync(
            new AiPurchaseRecommendationRequest(),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var serviceResult = Assert.IsType<ServiceResult<IReadOnlyCollection<AiPurchaseRecommendationDto>>>(ok.Value);
        Assert.True(serviceResult.Succeeded);
        Assert.Same(response, serviceResult.Data);
    }

    [Fact]
    public async Task GenerateSupplierPerformanceInsightsAsync_WhenRequestSucceeds_ReturnsOk()
    {
        IReadOnlyCollection<AiSupplierInsightDto> response =
        [
            new AiSupplierInsightDto
            {
                SupplierId = 30,
                SupplierName = "Acme Supplies",
                ReliabilityLevel = "High"
            }
        ];
        var controller = new AiController(
            new StubAiDashboardService(
                ServiceResult<AiDashboardSummaryResponse>.Success(new AiDashboardSummaryResponse())),
            new StubAiInventoryForecastService(
                ServiceResult<AiInventoryForecastResponse>.Success(new AiInventoryForecastResponse())),
            new StubAiInventoryInsightService(
                ServiceResult<IReadOnlyCollection<AiInventoryRecommendationDto>>.Success([])),
            new StubAiPurchaseRecommendationService(
                ServiceResult<IReadOnlyCollection<AiPurchaseRecommendationDto>>.Success([])),
            new StubAiSupplierInsightService(
                ServiceResult<IReadOnlyCollection<AiSupplierInsightDto>>.Success(response)));

        var result = await controller.GenerateSupplierPerformanceInsightsAsync(
            new AiSupplierInsightRequest(),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var serviceResult = Assert.IsType<ServiceResult<IReadOnlyCollection<AiSupplierInsightDto>>>(ok.Value);
        Assert.True(serviceResult.Succeeded);
        Assert.Same(response, serviceResult.Data);
    }

    private sealed class StubAiDashboardService(
        ServiceResult<AiDashboardSummaryResponse> result) : IAiDashboardService
    {
        public Task<ServiceResult<AiDashboardSummaryResponse>> GenerateDashboardSummaryAsync(
            AiDashboardSummaryRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class StubAiInventoryForecastService(
        ServiceResult<AiInventoryForecastResponse> result) : IAiInventoryForecastService
    {
        public Task<ServiceResult<AiInventoryForecastResponse>> GenerateInventoryForecastAsync(
            AiInventoryForecastRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class StubAiInventoryInsightService(
        ServiceResult<IReadOnlyCollection<AiInventoryRecommendationDto>> result) : IAiInventoryInsightService
    {
        public Task<ServiceResult<IReadOnlyCollection<AiInventoryRecommendationDto>>> GenerateInventoryRecommendationsAsync(
            AiInventoryRecommendationRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class StubAiPurchaseRecommendationService(
        ServiceResult<IReadOnlyCollection<AiPurchaseRecommendationDto>> result) : IAiPurchaseRecommendationService
    {
        public Task<ServiceResult<IReadOnlyCollection<AiPurchaseRecommendationDto>>> GeneratePurchaseRecommendationsAsync(
            AiPurchaseRecommendationRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class StubAiSupplierInsightService(
        ServiceResult<IReadOnlyCollection<AiSupplierInsightDto>> result) : IAiSupplierInsightService
    {
        public Task<ServiceResult<IReadOnlyCollection<AiSupplierInsightDto>>> GenerateSupplierPerformanceInsightsAsync(
            AiSupplierInsightRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }
}
