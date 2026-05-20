using MarketFlow.Api.Authorization;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Categories.DTOs;
using MarketFlow.Application.Features.Categories.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MarketFlow.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CategoriesController(ICategoryService categoryService) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.ReadProducts)]
    public async Task<ActionResult<ServiceResult<IReadOnlyCollection<CategoryDto>>>> GetAsync(
        CancellationToken cancellationToken)
    {
        var result = await categoryService.GetCategoriesAsync(cancellationToken);
        return Ok(result);
    }
}
