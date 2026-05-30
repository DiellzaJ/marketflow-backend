using MarketFlow.Api.Authorization;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.AI.DTOs;
using MarketFlow.Application.Features.AI.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MarketFlow.Api.Controllers;

[ApiController]
[Route("api/ai")]
[Tags("AI")]
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
    [EndpointSummary("Chat with the tenant-safe AI assistant")]
    [EndpointDescription("Answers a business question using scoped tenant report data. The frontend calls this endpoint only; it never calls OpenAI or Ollama directly.")]
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
    [EndpointSummary("Query tenant reports with natural language")]
    [EndpointDescription("Classifies a business question and returns the matching scoped report data for the authenticated tenant and assignment scope.")]
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
    [EndpointSummary("Generate an AI dashboard summary")]
    [EndpointDescription("Summarizes calculated dashboard KPIs and recommends actions from tenant-safe aggregated sales, stock, and supplier data.")]
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
    [EndpointSummary("Generate inventory demand forecasts")]
    [EndpointDescription("Calculates average daily sales, forecast demand, and days of stock remaining for products in the current tenant scope.")]
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
    [EndpointSummary("Generate inventory recommendations")]
    [EndpointDescription("Detects low stock, critical low stock, and overstock conditions from backend-calculated inventory data, then asks the configured AI provider only for concise explanations.")]
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
    [Authorize(Policy = AuthorizationPolicies.CanViewPurchaseAiRecommendations)]
    [EndpointSummary("Generate purchase recommendations")]
    [EndpointDescription("Calculates recommended purchase quantities from forecast demand, current stock, pending purchases, and preferred suppliers. AI output cannot change calculated quantities.")]
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
    [EndpointSummary("Generate supplier performance insights")]
    [EndpointDescription("Calculates supplier cancellation rate, delivery speed, reliability level, and flags, then asks the configured AI provider for recommendation wording.")]
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
    [EndpointSummary("Detect sales and stock anomalies")]
    [EndpointDescription("Runs backend rules for high discounts, below-cost sales, large or unmatched stock movements, and sales spikes. AI explanations are sanitized and framed as items needing review.")]
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
