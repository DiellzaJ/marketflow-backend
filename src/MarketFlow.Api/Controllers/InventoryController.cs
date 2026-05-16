using MarketFlow.Api.Authorization;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Inventory.DTOs;
using MarketFlow.Application.Features.Inventory.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MarketFlow.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = AuthorizationPolicies.ReadInventory)]
public class InventoryController(IInventoryService inventoryService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ServiceResult<IReadOnlyCollection<InventoryItemDto>>>> GetAsync(
        CancellationToken cancellationToken)
    {
        var result = await inventoryService.GetInventoryAsync(cancellationToken);
        return Ok(result);
    }
}
