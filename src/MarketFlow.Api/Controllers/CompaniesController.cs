using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Companies.DTOs;
using MarketFlow.Application.Features.Companies.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace MarketFlow.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CompaniesController(ICompanyService companyService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ServiceResult<IReadOnlyCollection<CompanyDto>>>> GetAsync(
        CancellationToken cancellationToken)
    {
        var result = await companyService.GetCompaniesAsync(cancellationToken);
        return Ok(result);
    }
}
