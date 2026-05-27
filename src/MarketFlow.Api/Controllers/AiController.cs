using MarketFlow.Api.Authorization;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.AI.DTOs;
using MarketFlow.Application.Features.AI.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MarketFlow.Api.Controllers;

[ApiController]
[Route("api/ai")]
public class AiController(
    IAiDashboardService aiDashboardService,
    IAiInventoryForecastService aiInventoryForecastService,
    IAiInventoryInsightService aiInventoryInsightService,
    IAiPurchaseRecommendationService aiPurchaseRecommendationService) : ControllerBase
{
    [HttpPost("dashboard-summary")]
    [Authorize(Policy = AuthorizationPolicies.CompanyAdminOnly)]
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
    [Authorize(Policy = AuthorizationPolicies.CompanyAdminOnly)]
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
    [Authorize(Policy = AuthorizationPolicies.CompanyAdminOnly)]
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
    [Authorize(Policy = AuthorizationPolicies.CompanyAdminOrMainOperator)]
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
}
