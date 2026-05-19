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
    [Authorize(Policy = AuthorizationPolicies.RootAdminOnly)]
    public async Task<ActionResult<ServiceResult<IReadOnlyCollection<CompanyDto>>>> GetAsync(
        CancellationToken cancellationToken)
    {
        var result = await companyService.GetCompaniesAsync(cancellationToken);
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.RootAdminOnly)]
    public async Task<ActionResult<ServiceResult<CompanyDto>>> CreateAsync(
        CreateCompanyRequest request,
        CancellationToken cancellationToken)
    {
        var result = await companyService.CreateCompanyAsync(request, cancellationToken);

        return result.Succeeded ? CreatedAtAction(nameof(GetAsync), result) : BadRequest(result);
    }
}
