using MarketFlow.Api.Authorization;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Purchases.DTOs;
using MarketFlow.Application.Features.Purchases.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MarketFlow.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = AuthorizationPolicies.ManagePurchases)]
public class PurchasesController(IPurchaseService purchaseService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ServiceResult<IReadOnlyCollection<PurchaseDto>>>> GetAsync(
        CancellationToken cancellationToken)
    {
        var result = await purchaseService.GetPurchasesAsync(cancellationToken);
        return Ok(result);
    }
}
