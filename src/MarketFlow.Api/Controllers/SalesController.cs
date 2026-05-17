using MarketFlow.Api.Authorization;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Sales.DTOs;
using MarketFlow.Application.Features.Sales.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MarketFlow.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SalesController(ISalesService salesService) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.ReadSales)]
    public async Task<ActionResult<ServiceResult<IReadOnlyCollection<SaleDto>>>> GetAsync(
        CancellationToken cancellationToken)
    {
        var result = await salesService.GetSalesAsync(cancellationToken);
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.CreateSales)]
    public async Task<ActionResult<ServiceResult<SaleDto>>> CreateAsync(
        CreateSaleRequest request,
        CancellationToken cancellationToken)
    {
        var result = await salesService.CreateSaleAsync(request, cancellationToken);

        return result.Succeeded ? CreatedAtAction(nameof(GetAsync), result) : BadRequest(result);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = AuthorizationPolicies.UpdateSales)]
    public async Task<ActionResult<ServiceResult<SaleDto>>> UpdateAsync(
        int id,
        UpdateSaleRequest request,
        CancellationToken cancellationToken)
    {
        var result = await salesService.UpdateSaleAsync(id, request, cancellationToken);

        return result.Succeeded ? Ok(result) : NotFound(result);
    }

    [HttpPatch("{id:int}")]
    [Authorize(Policy = AuthorizationPolicies.UpdateSales)]
    public async Task<ActionResult<ServiceResult<SaleDto>>> PatchAsync(
        int id,
        PatchSaleRequest request,
        CancellationToken cancellationToken)
    {
        var result = await salesService.PatchSaleAsync(id, request, cancellationToken);

        return result.Succeeded ? Ok(result) : NotFound(result);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = AuthorizationPolicies.DeleteSales)]
    public async Task<ActionResult<ServiceResult<bool>>> DeleteAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var result = await salesService.DeleteSaleAsync(id, cancellationToken);

        return result.Succeeded ? Ok(result) : NotFound(result);
    }
}
