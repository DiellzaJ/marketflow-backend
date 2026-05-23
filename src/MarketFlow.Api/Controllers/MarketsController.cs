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
    private const string GetMarketByIdRouteName = "GetMarketById";

    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.ReadMarkets)]
    public async Task<ActionResult<ServiceResult<IReadOnlyCollection<MarketDto>>>> GetAsync(
        CancellationToken cancellationToken)
    {
        var result = await marketService.GetMarketsAsync(cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:int}", Name = GetMarketByIdRouteName)]
    [Authorize(Policy = AuthorizationPolicies.ReadMarkets)]
    public async Task<ActionResult<ServiceResult<MarketDto>>> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var result = await marketService.GetMarketAsync(id, cancellationToken);
        return result.Succeeded ? Ok(result) : MarketFailure(result);
    }

    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.CreateMarkets)]
    public async Task<ActionResult<ServiceResult<MarketDto>>> CreateAsync(
        CreateMarketRequest request,
        CancellationToken cancellationToken)
    {
        var result = await marketService.CreateMarketAsync(request, cancellationToken);

        return result.Succeeded
            ? CreatedAtRoute(GetMarketByIdRouteName, new { id = result.Data!.Id }, result)
            : MarketFailure(result);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = AuthorizationPolicies.UpdateMarkets)]
    public async Task<ActionResult<ServiceResult<MarketDto>>> UpdateAsync(
        int id,
        UpdateMarketRequest request,
        CancellationToken cancellationToken)
    {
        var result = await marketService.UpdateMarketAsync(id, request, cancellationToken);

        return result.Succeeded ? Ok(result) : MarketFailure(result);
    }

    [HttpPatch("{id:int}/deactivate")]
    [Authorize(Policy = AuthorizationPolicies.DeleteMarkets)]
    public async Task<ActionResult<ServiceResult<MarketDto>>> DeactivateAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var result = await marketService.DeactivateMarketAsync(id, cancellationToken);

        return result.Succeeded ? Ok(result) : MarketFailure(result);
    }

    [HttpPatch("{id:int}/activate")]
    [Authorize(Policy = AuthorizationPolicies.UpdateMarkets)]
    public async Task<ActionResult<ServiceResult<MarketDto>>> ActivateAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var result = await marketService.ActivateMarketAsync(id, cancellationToken);

        return result.Succeeded ? Ok(result) : MarketFailure(result);
    }

    private ActionResult<ServiceResult<MarketDto>> MarketFailure(ServiceResult<MarketDto> result)
    {
        return result.FailureType switch
        {
            ServiceResultFailureType.NotFound => NotFound(result),
            ServiceResultFailureType.Conflict => Conflict(result),
            _ => BadRequest(result)
        };
    }
}
