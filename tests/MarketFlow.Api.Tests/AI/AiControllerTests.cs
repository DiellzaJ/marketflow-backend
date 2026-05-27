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
                ServiceResult<IReadOnlyCollection<AiInventoryRecommendationDto>>.Success([])));

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
                ServiceResult<IReadOnlyCollection<AiInventoryRecommendationDto>>.Success([])));

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
                ServiceResult<IReadOnlyCollection<AiInventoryRecommendationDto>>.Success(response)));

        var result = await controller.GenerateInventoryRecommendationsAsync(
            new AiInventoryRecommendationRequest(),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var serviceResult = Assert.IsType<ServiceResult<IReadOnlyCollection<AiInventoryRecommendationDto>>>(ok.Value);
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
}
