using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Products.DTOs;
using MarketFlow.Application.Features.Products.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace MarketFlow.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductsController(IProductService productService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ServiceResult<IReadOnlyCollection<ProductDto>>>> GetAsync(
        CancellationToken cancellationToken)
    {
        var result = await productService.GetProductsAsync(cancellationToken);
        return Ok(result);
    }
}
