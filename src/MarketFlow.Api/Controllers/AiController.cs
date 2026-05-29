using MarketFlow.Api.Authorization;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.AI.DTOs;
using MarketFlow.Application.Features.AI.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MarketFlow.Api.Controllers;

[ApiController]
[Route("api/ai")]
[ServiceFilter(typeof(AiTenantScopeAuthorizationFilter))]
public class AiController(
    IAiDashboardService aiDashboardService,
    IAiInventoryForecastService aiInventoryForecastService,
    IAiInventoryInsightService aiInventoryInsightService,
    IAiPurchaseRecommendationService aiPurchaseRecommendationService,
    IAiSupplierInsightService aiSupplierInsightService,
    IAiAnomalyDetectionService aiAnomalyDetectionService,
    IAiReportQueryService aiReportQueryService,
    IAiChatService aiChatService) : ControllerBase
{
    [HttpPost("chat")]
    [Authorize(Policy = AuthorizationPolicies.CanUseAiAssistant)]
    [ProducesResponseType(typeof(ServiceResult<AiChatResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceResult<AiChatResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ServiceResult<AiChatResponse>>> ChatAsync(
        AiChatRequest request,
        CancellationToken cancellationToken)
    {
        var result = await aiChatService.ChatAsync(request, cancellationToken);
        return result.Succeeded ? Ok(result) : BadRequest(result);
    }

    [HttpPost("reports/query")]
    [Authorize(Policy = AuthorizationPolicies.CanUseAiAssistant)]
    [ProducesResponseType(typeof(ServiceResult<NaturalLanguageReportResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceResult<NaturalLanguageReportResponseDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ServiceResult<NaturalLanguageReportResponseDto>>> QueryReportAsync(
        NaturalLanguageReportRequest request,
        CancellationToken cancellationToken)
    {
        var result = await aiReportQueryService.QueryAsync(request, cancellationToken);
        return result.Succeeded ? Ok(result) : BadRequest(result);
    }

    [HttpPost("dashboard-summary")]
    [Authorize(Policy = AuthorizationPolicies.CanViewAiDashboard)]
    [ProducesResponseType(typeof(ServiceResult<AiDashboardSummaryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceResult<AiDashboardSummaryResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ServiceResult<AiDashboardSummaryResponse>>> GenerateDashboardSummaryAsync(
        AiDashboardSummaryRequest request,
        CancellationToken cancellationToken)
    {
        var result = await aiDashboardService.GenerateDashboardSummaryAsync(request, cancellationToken);
        return result.Succeeded ? Ok(result) : BadRequest(result);
    }

    [HttpPost("inventory-forecast")]
    [Authorize(Policy = AuthorizationPolicies.CanViewInventoryAiRecommendations)]
    [ProducesResponseType(typeof(ServiceResult<AiInventoryForecastResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceResult<AiInventoryForecastResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ServiceResult<AiInventoryForecastResponse>>> GenerateInventoryForecastAsync(
        AiInventoryForecastRequest request,
        CancellationToken cancellationToken)
    {
        var result = await aiInventoryForecastService.GenerateInventoryForecastAsync(request, cancellationToken);
        return result.Succeeded ? Ok(result) : BadRequest(result);
    }

    [HttpPost("inventory/recommendations")]
    [Authorize(Policy = AuthorizationPolicies.CanViewInventoryAiRecommendations)]
    [ProducesResponseType(typeof(ServiceResult<IReadOnlyCollection<AiInventoryRecommendationDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceResult<IReadOnlyCollection<AiInventoryRecommendationDto>>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ServiceResult<IReadOnlyCollection<AiInventoryRecommendationDto>>>> GenerateInventoryRecommendationsAsync(
        AiInventoryRecommendationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await aiInventoryInsightService.GenerateInventoryRecommendationsAsync(request, cancellationToken);
        return result.Succeeded ? Ok(result) : BadRequest(result);
    }

    [HttpPost("purchases/recommendations")]
    [Authorize(Policy = AuthorizationPolicies.CanViewInventoryAiRecommendations)]
    [ProducesResponseType(typeof(ServiceResult<IReadOnlyCollection<AiPurchaseRecommendationDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceResult<IReadOnlyCollection<AiPurchaseRecommendationDto>>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ServiceResult<IReadOnlyCollection<AiPurchaseRecommendationDto>>>> GeneratePurchaseRecommendationsAsync(
        AiPurchaseRecommendationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await aiPurchaseRecommendationService.GeneratePurchaseRecommendationsAsync(request, cancellationToken);
        return result.Succeeded ? Ok(result) : BadRequest(result);
    }

    [HttpPost("suppliers/performance")]
    [Authorize(Policy = AuthorizationPolicies.CanViewSupplierAiInsights)]
    [ProducesResponseType(typeof(ServiceResult<IReadOnlyCollection<AiSupplierInsightDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceResult<IReadOnlyCollection<AiSupplierInsightDto>>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ServiceResult<IReadOnlyCollection<AiSupplierInsightDto>>>> GenerateSupplierPerformanceInsightsAsync(
        AiSupplierInsightRequest request,
        CancellationToken cancellationToken)
    {
        var result = await aiSupplierInsightService.GenerateSupplierPerformanceInsightsAsync(request, cancellationToken);
        return result.Succeeded ? Ok(result) : BadRequest(result);
    }

    [HttpPost("anomalies/detect")]
    [Authorize(Policy = AuthorizationPolicies.CanViewAnomalyInsights)]
    [ProducesResponseType(typeof(ServiceResult<IReadOnlyCollection<AiAnomalyDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceResult<IReadOnlyCollection<AiAnomalyDto>>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ServiceResult<IReadOnlyCollection<AiAnomalyDto>>>> DetectAnomaliesAsync(
        AiAnomalyDetectionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await aiAnomalyDetectionService.DetectAnomaliesAsync(request, cancellationToken);
        return result.Succeeded ? Ok(result) : BadRequest(result);
    }
}
