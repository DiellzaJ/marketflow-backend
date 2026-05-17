using MarketFlow.Api.Authorization;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Inventory.DTOs;
using MarketFlow.Application.Features.Inventory.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MarketFlow.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class InventoryController(IInventoryService inventoryService) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.ReadInventory)]
    public async Task<ActionResult<ServiceResult<IReadOnlyCollection<InventoryItemDto>>>> GetAsync(
        CancellationToken cancellationToken)
    {
        var result = await inventoryService.GetInventoryAsync(cancellationToken);
        return Ok(result);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = AuthorizationPolicies.UpdateInventory)]
    public async Task<ActionResult<ServiceResult<InventoryItemDto>>> UpdateAsync(
        int id,
        UpdateInventoryItemRequest request,
        CancellationToken cancellationToken)
    {
        var result = await inventoryService.UpdateInventoryItemAsync(id, request, cancellationToken);

        return result.Succeeded ? Ok(result) : NotFound(result);
    }

    [HttpPatch("{id:int}")]
    [Authorize(Policy = AuthorizationPolicies.UpdateInventory)]
    public async Task<ActionResult<ServiceResult<InventoryItemDto>>> PatchAsync(
        int id,
        PatchInventoryItemRequest request,
        CancellationToken cancellationToken)
    {
        var result = await inventoryService.PatchInventoryItemAsync(id, request, cancellationToken);

        return result.Succeeded ? Ok(result) : NotFound(result);
    }
}
