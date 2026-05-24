using MarketFlow.Api.Authorization;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Departments.DTOs;
using MarketFlow.Application.Features.Departments.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MarketFlow.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DepartmentsController(IDepartmentService departmentService) : ControllerBase
{
    private const string GetDepartmentByIdRouteName = "GetDepartmentById";

    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.ReadDepartments)]
    public async Task<ActionResult<ServiceResult<IReadOnlyCollection<DepartmentDto>>>> GetAsync(
        [FromQuery] int? marketId,
        CancellationToken cancellationToken)
    {
        var result = await departmentService.GetDepartmentsAsync(marketId, cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:int}", Name = GetDepartmentByIdRouteName)]
    [Authorize(Policy = AuthorizationPolicies.ReadDepartments)]
    public async Task<ActionResult<ServiceResult<DepartmentDto>>> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var result = await departmentService.GetDepartmentAsync(id, cancellationToken);
        return result.Succeeded ? Ok(result) : DepartmentFailure(result);
    }

    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.CreateDepartments)]
    public async Task<ActionResult<ServiceResult<DepartmentDto>>> CreateAsync(
        CreateDepartmentRequest request,
        CancellationToken cancellationToken)
    {
        var result = await departmentService.CreateDepartmentAsync(request, cancellationToken);

        return result.Succeeded
            ? CreatedAtRoute(GetDepartmentByIdRouteName, new { id = result.Data!.Id }, result)
            : DepartmentFailure(result);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = AuthorizationPolicies.UpdateDepartments)]
    public async Task<ActionResult<ServiceResult<DepartmentDto>>> UpdateAsync(
        int id,
        UpdateDepartmentRequest request,
        CancellationToken cancellationToken)
    {
        var result = await departmentService.UpdateDepartmentAsync(id, request, cancellationToken);

        return result.Succeeded ? Ok(result) : DepartmentFailure(result);
    }

    [HttpPatch("{id:int}/deactivate")]
    [Authorize(Policy = AuthorizationPolicies.DeleteDepartments)]
    public async Task<ActionResult<ServiceResult<DepartmentDto>>> DeactivateAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var result = await departmentService.DeactivateDepartmentAsync(id, cancellationToken);

        return result.Succeeded ? Ok(result) : DepartmentFailure(result);
    }

    [HttpPatch("{id:int}/activate")]
    [Authorize(Policy = AuthorizationPolicies.UpdateDepartments)]
    public async Task<ActionResult<ServiceResult<DepartmentDto>>> ActivateAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var result = await departmentService.ActivateDepartmentAsync(id, cancellationToken);

        return result.Succeeded ? Ok(result) : DepartmentFailure(result);
    }

    private ActionResult<ServiceResult<DepartmentDto>> DepartmentFailure(ServiceResult<DepartmentDto> result)
    {
        return result.FailureType switch
        {
            ServiceResultFailureType.NotFound => NotFound(result),
            ServiceResultFailureType.Conflict => Conflict(result),
            _ => BadRequest(result)
        };
    }
}
