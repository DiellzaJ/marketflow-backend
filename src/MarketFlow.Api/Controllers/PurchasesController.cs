using MarketFlow.Api.Authorization;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Purchases.DTOs;
using MarketFlow.Application.Features.Purchases.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MarketFlow.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PurchasesController(IPurchaseService purchaseService) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.ReadPurchases)]
    public async Task<ActionResult<ServiceResult<IReadOnlyCollection<PurchaseDto>>>> GetAsync(
        CancellationToken cancellationToken)
    {
        var result = await purchaseService.GetPurchasesAsync(cancellationToken);
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.CreatePurchases)]
    public async Task<ActionResult<ServiceResult<PurchaseDto>>> CreateAsync(
        CreatePurchaseRequest request,
        CancellationToken cancellationToken)
    {
        var result = await purchaseService.CreatePurchaseAsync(request, cancellationToken);

        return result.Succeeded ? CreatedAtAction(nameof(GetAsync), result) : BadRequest(result);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = AuthorizationPolicies.UpdatePurchases)]
    public async Task<ActionResult<ServiceResult<PurchaseDto>>> UpdateAsync(
        int id,
        UpdatePurchaseRequest request,
        CancellationToken cancellationToken)
    {
        var result = await purchaseService.UpdatePurchaseAsync(id, request, cancellationToken);

        return result.Succeeded ? Ok(result) : NotFound(result);
    }

    [HttpPatch("{id:int}")]
    [Authorize(Policy = AuthorizationPolicies.UpdatePurchases)]
    public async Task<ActionResult<ServiceResult<PurchaseDto>>> PatchAsync(
        int id,
        PatchPurchaseRequest request,
        CancellationToken cancellationToken)
    {
        var result = await purchaseService.PatchPurchaseAsync(id, request, cancellationToken);

        return result.Succeeded ? Ok(result) : NotFound(result);
    }
}
