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
}
