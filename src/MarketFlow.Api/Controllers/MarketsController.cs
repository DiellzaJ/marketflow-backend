using MarketFlow.Api.Authorization;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Markets.DTOs;
using MarketFlow.Application.Features.Markets.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MarketFlow.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MarketsController(IMarketService marketService) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.ReadMarkets)]
    public async Task<ActionResult<ServiceResult<IReadOnlyCollection<MarketDto>>>> GetAsync(
        CancellationToken cancellationToken)
    {
        var result = await marketService.GetMarketsAsync(cancellationToken);
        return Ok(result);
    }
}
