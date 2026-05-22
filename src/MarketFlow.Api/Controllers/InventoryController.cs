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
    private const string GetInventoryItemByIdRouteName = "GetInventoryItemById";

    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.ReadInventory)]
    public async Task<ActionResult<ServiceResult<PagedResult<InventoryItemDto>>>> GetAsync(
        [FromQuery] InventoryListQuery query,
        CancellationToken cancellationToken)
    {
        var result = await inventoryService.GetInventoryAsync(query, cancellationToken);
        return result.Succeeded ? Ok(result) : BadRequest(result);
    }

    [HttpGet("low-stock")]
    [Authorize(Policy = AuthorizationPolicies.ReadInventory)]
    public async Task<ActionResult<ServiceResult<IReadOnlyCollection<InventoryItemDto>>>> GetLowStockAsync(
        CancellationToken cancellationToken)
    {
        var result = await inventoryService.GetLowStockInventoryAsync(cancellationToken);
        return Ok(result);
    }

    [HttpGet("movements")]
    [Authorize(Policy = AuthorizationPolicies.ReadInventoryMovements)]
    public async Task<ActionResult<ServiceResult<PagedResult<InventoryMovementDto>>>> GetMovementsAsync(
        [FromQuery] InventoryMovementListQuery query,
        CancellationToken cancellationToken)
    {
        var result = await inventoryService.GetInventoryMovementsAsync(query, cancellationToken);
        return result.Succeeded ? Ok(result) : BadRequest(result);
    }

    [HttpGet("{id:int}", Name = GetInventoryItemByIdRouteName)]
    [Authorize(Policy = AuthorizationPolicies.ReadInventory)]
    public async Task<ActionResult<ServiceResult<InventoryItemDto>>> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var result = await inventoryService.GetInventoryItemAsync(id, cancellationToken);
        return result.Succeeded ? Ok(result) : NotFound(result);
    }

    [HttpGet("{id:int}/movements")]
    [Authorize(Policy = AuthorizationPolicies.ReadInventoryMovements)]
    public async Task<ActionResult<ServiceResult<PagedResult<InventoryMovementDto>>>> GetMovementsForInventoryAsync(
        int id,
        [FromQuery] InventoryMovementListQuery query,
        CancellationToken cancellationToken)
    {
        var result = await inventoryService.GetInventoryMovementsForInventoryAsync(id, query, cancellationToken);

        return result.Succeeded
            ? Ok(result)
            : result.Message == "Inventory item was not found."
                ? NotFound(result)
                : BadRequest(result);
    }

    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.CreateInventory)]
    public async Task<ActionResult<ServiceResult<InventoryItemDto>>> CreateAsync(
        CreateInventoryItemRequest request,
        CancellationToken cancellationToken)
    {
        var result = await inventoryService.CreateInventoryItemAsync(request, cancellationToken);

        return result.Succeeded
            ? CreatedAtRoute(GetInventoryItemByIdRouteName, new { id = result.Data!.Id }, result)
            : BadRequest(result);
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
    [Authorize(Policy = AuthorizationPolicies.AdjustInventory)]
    public async Task<ActionResult<ServiceResult<InventoryItemDto>>> PatchAsync(
        int id,
        PatchInventoryItemRequest request,
        CancellationToken cancellationToken)
    {
        var result = await inventoryService.PatchInventoryItemAsync(id, request, cancellationToken);

        return result.Succeeded ? Ok(result) : NotFound(result);
    }

    [HttpPost("{id:int}/adjust")]
    [Authorize(Policy = AuthorizationPolicies.AdjustInventory)]
    public async Task<ActionResult<ServiceResult<InventoryItemDto>>> AdjustAsync(
        int id,
        AdjustInventoryRequest request,
        CancellationToken cancellationToken)
    {
        var result = await inventoryService.AdjustInventoryItemAsync(id, request, cancellationToken);

        return result.Succeeded
            ? Ok(result)
            : result.FailureType == ServiceResultFailureType.NotFound
                ? NotFound(result)
                : BadRequest(result);
    }

    [HttpPost("transfer")]
    [Authorize(Policy = AuthorizationPolicies.TransferInventory)]
    public async Task<ActionResult<ServiceResult<bool>>> TransferAsync(
        TransferInventoryRequest request,
        CancellationToken cancellationToken)
    {
        var result = await inventoryService.TransferInventoryAsync(request, cancellationToken);

        return result.Succeeded ? Ok(result) : BadRequest(result);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = AuthorizationPolicies.DeleteInventory)]
    public async Task<ActionResult<ServiceResult<bool>>> DeleteAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var result = await inventoryService.DeleteInventoryItemAsync(id, cancellationToken);

        return result.Succeeded ? Ok(result) : NotFound(result);
    }
}
