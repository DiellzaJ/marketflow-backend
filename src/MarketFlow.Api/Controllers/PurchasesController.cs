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

    [HttpGet("{id:int}")]
    [Authorize(Policy = AuthorizationPolicies.ReadPurchases)]
    public async Task<ActionResult<ServiceResult<PurchaseDto>>> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var result = await purchaseService.GetPurchaseAsync(id, cancellationToken);
        return result.Succeeded ? Ok(result) : NotFound(result);
    }

    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.CreatePurchases)]
    public async Task<ActionResult<ServiceResult<PurchaseDto>>> CreateAsync(
        CreatePurchaseRequest request,
        CancellationToken cancellationToken)
    {
        var result = await purchaseService.CreatePurchaseAsync(request, cancellationToken);

        return result.Succeeded ? Ok(result) : BadRequest(result);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = AuthorizationPolicies.UpdatePurchases)]
    public async Task<ActionResult<ServiceResult<PurchaseDto>>> UpdateAsync(
        int id,
        UpdatePurchaseRequest request,
        CancellationToken cancellationToken)
    {
        var result = await purchaseService.UpdatePurchaseAsync(id, request, cancellationToken);

        return MapPurchaseResult(result);
    }

    [HttpPatch("{id:int}")]
    [Authorize(Policy = AuthorizationPolicies.UpdatePurchases)]
    public async Task<ActionResult<ServiceResult<PurchaseDto>>> PatchAsync(
        int id,
        PatchPurchaseRequest request,
        CancellationToken cancellationToken)
    {
        var result = await purchaseService.PatchPurchaseAsync(id, request, cancellationToken);

        return MapPurchaseResult(result);
    }

    [HttpPost("{id:int}/receive")]
    [Authorize(Policy = AuthorizationPolicies.UpdatePurchases)]
    public async Task<ActionResult<ServiceResult<PurchaseDto>>> ReceiveAsync(
        int id,
        ReceivePurchaseRequest request,
        CancellationToken cancellationToken)
    {
        var result = await purchaseService.ReceivePurchaseAsync(id, request, cancellationToken);
        return MapPurchaseResult(result);
    }

    [HttpPost("{id:int}/cancel")]
    [Authorize(Policy = AuthorizationPolicies.DeletePurchases)]
    public async Task<ActionResult<ServiceResult<PurchaseDto>>> CancelAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var result = await purchaseService.CancelPurchaseAsync(id, cancellationToken);
        return MapPurchaseResult(result);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = AuthorizationPolicies.DeletePurchases)]
    public async Task<ActionResult<ServiceResult<bool>>> DeleteAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var result = await purchaseService.DeletePurchaseAsync(id, cancellationToken);

        return result.Succeeded ? Ok(result) : NotFound(result);
    }

    private ActionResult<ServiceResult<PurchaseDto>> MapPurchaseResult(ServiceResult<PurchaseDto> result)
    {
        if (result.Succeeded)
        {
            return Ok(result);
        }

        return result.FailureType switch
        {
            ServiceResultFailureType.NotFound => NotFound(result),
            ServiceResultFailureType.Conflict => Conflict(result),
            _ => BadRequest(result)
        };
    }
}
