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
    IAiInventoryForecastService aiInventoryForecastService) : ControllerBase
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
}
