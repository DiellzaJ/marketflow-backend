using MarketFlow.Api.Authorization;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Suppliers.DTOs;
using MarketFlow.Application.Features.Suppliers.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MarketFlow.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SuppliersController(ISupplierService supplierService) : ControllerBase
{
    private const string GetSupplierByIdRouteName = "GetSupplierById";

    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.ReadSuppliers)]
    public async Task<ActionResult<ServiceResult<IReadOnlyCollection<SupplierDto>>>> GetAsync(
        [FromQuery] bool includeInactive,
        CancellationToken cancellationToken)
    {
        var result = await supplierService.GetSuppliersAsync(includeInactive, cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:int}", Name = GetSupplierByIdRouteName)]
    [Authorize(Policy = AuthorizationPolicies.ReadSuppliers)]
    public async Task<ActionResult<ServiceResult<SupplierDto>>> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var result = await supplierService.GetSupplierAsync(id, cancellationToken);
        return result.Succeeded ? Ok(result) : SupplierFailure(result);
    }

    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.CreateSuppliers)]
    public async Task<ActionResult<ServiceResult<SupplierDto>>> CreateAsync(
        CreateSupplierRequest request,
        CancellationToken cancellationToken)
    {
        var result = await supplierService.CreateSupplierAsync(request, cancellationToken);

        return result.Succeeded
            ? CreatedAtRoute(GetSupplierByIdRouteName, new { id = result.Data!.Id }, result)
            : SupplierFailure(result);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = AuthorizationPolicies.UpdateSuppliers)]
    public async Task<ActionResult<ServiceResult<SupplierDto>>> UpdateAsync(
        int id,
        UpdateSupplierRequest request,
        CancellationToken cancellationToken)
    {
        var result = await supplierService.UpdateSupplierAsync(id, request, cancellationToken);

        return result.Succeeded ? Ok(result) : SupplierFailure(result);
    }

    [HttpPatch("{id:int}/status")]
    [Authorize(Policy = AuthorizationPolicies.UpdateSuppliers)]
    public async Task<ActionResult<ServiceResult<SupplierDto>>> UpdateStatusAsync(
        int id,
        PatchSupplierStatusRequest request,
        CancellationToken cancellationToken)
    {
        var result = await supplierService.SetSupplierActiveStateAsync(
            id,
            request.IsActive,
            cancellationToken);

        return result.Succeeded ? Ok(result) : SupplierFailure(result);
    }

    [HttpPatch("{id:int}/deactivate")]
    [Authorize(Policy = AuthorizationPolicies.DeleteSuppliers)]
    public async Task<ActionResult<ServiceResult<SupplierDto>>> DeactivateAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var result = await supplierService.SetSupplierActiveStateAsync(
            id,
            isActive: false,
            cancellationToken);

        return result.Succeeded ? Ok(result) : SupplierFailure(result);
    }

    private ActionResult<ServiceResult<SupplierDto>> SupplierFailure(ServiceResult<SupplierDto> result)
    {
        return result.FailureType switch
        {
            ServiceResultFailureType.NotFound => NotFound(result),
            ServiceResultFailureType.Conflict => Conflict(result),
            _ => BadRequest(result)
        };
    }
}
