using MarketFlow.Api.Authorization;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Companies.DTOs;
using MarketFlow.Application.Features.Companies.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MarketFlow.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = AuthorizationPolicies.RootAdminOnly)]
public class CompaniesController(ICompanyService companyService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ServiceResult<IReadOnlyCollection<CompanyDto>>>> GetAsync(
        CancellationToken cancellationToken)
    {
        var result = await companyService.GetCompaniesAsync(cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ServiceResult<CompanyDto>>> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var result = await companyService.GetCompanyByIdAsync(id, cancellationToken);

        return result.Succeeded ? Ok(result) : NotFound(result);
    }

    [HttpPost]
    [ProducesResponseType(typeof(ServiceResult<CompanyOnboardingDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ServiceResult<CompanyOnboardingDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ServiceResult<CompanyOnboardingDto>>> CreateAsync(
        CreateCompanyRequest request,
        CancellationToken cancellationToken)
    {
        var result = await companyService.CreateCompanyAsync(request, cancellationToken);

        return result.Succeeded
            ? CreatedAtAction(nameof(GetByIdAsync), new { id = result.Data!.Company.Id }, result)
            : BadRequest(result);
    }
}
