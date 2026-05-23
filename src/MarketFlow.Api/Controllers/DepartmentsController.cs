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
    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.ReadDepartments)]
    public async Task<ActionResult<ServiceResult<IReadOnlyCollection<DepartmentDto>>>> GetAsync(
        [FromQuery] int? marketId,
        CancellationToken cancellationToken)
    {
        var result = await departmentService.GetDepartmentsAsync(marketId, cancellationToken);
        return Ok(result);
    }
}
