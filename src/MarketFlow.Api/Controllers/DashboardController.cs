using MarketFlow.Api.Authorization;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Dashboard.DTOs;
using MarketFlow.Application.Features.Dashboard.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MarketFlow.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DashboardController(IDashboardService dashboardService) : ControllerBase
{
    [HttpGet("sales-summary")]
    [Authorize(Policy = AuthorizationPolicies.ReadSales)]
    public async Task<ActionResult<ServiceResult<SalesSummaryDto>>> GetSalesSummaryAsync(
        [FromQuery] SalesSummaryQuery query,
        CancellationToken cancellationToken)
    {
        var result = await dashboardService.GetSalesSummaryAsync(query, cancellationToken);
        return result.Succeeded ? Ok(result) : BadRequest(result);
    }
}
