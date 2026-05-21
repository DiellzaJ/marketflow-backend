using MarketFlow.Api.Authorization;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Products.DTOs;
using MarketFlow.Application.Features.Products.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MarketFlow.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductsController(IProductService productService) : ControllerBase
{
    private const string GetProductByIdRouteName = "GetProductById";

    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.ReadProducts)]
    public async Task<ActionResult<ServiceResult<PagedResult<ProductDto>>>> GetAsync(
        [FromQuery] ProductListQuery query,
        CancellationToken cancellationToken)
    {
        var result = await productService.GetProductsAsync(query, cancellationToken);
        return result.Succeeded ? Ok(result) : BadRequest(result);
    }

    [HttpGet("{id:int}", Name = GetProductByIdRouteName)]
    [Authorize(Policy = AuthorizationPolicies.ReadProducts)]
    public async Task<ActionResult<ServiceResult<ProductDto>>> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var result = await productService.GetProductAsync(id, cancellationToken);
        return result.Succeeded ? Ok(result) : NotFound(result);
    }

    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.CreateProducts)]
    public async Task<ActionResult<ServiceResult<ProductDto>>> CreateAsync(
        CreateProductRequest request,
        CancellationToken cancellationToken)
    {
        var result = await productService.CreateProductAsync(request, cancellationToken);

        if (!result.Succeeded)
        {
            return ProductFailure(result);
        }

        return CreatedAtRoute(GetProductByIdRouteName, new { id = result.Data!.Id }, result);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = AuthorizationPolicies.UpdateProducts)]
    public async Task<ActionResult<ServiceResult<ProductDto>>> UpdateAsync(
        int id,
        UpdateProductRequest request,
        CancellationToken cancellationToken)
    {
        var result = await productService.UpdateProductAsync(id, request, cancellationToken);

        return result.Succeeded ? Ok(result) : ProductFailure(result);
    }

    [HttpPatch("{id:int}")]
    [Authorize(Policy = AuthorizationPolicies.UpdateProducts)]
    public async Task<ActionResult<ServiceResult<ProductDto>>> PatchAsync(
        int id,
        PatchProductRequest request,
        CancellationToken cancellationToken)
    {
        var result = await productService.PatchProductAsync(id, request, cancellationToken);

        return result.Succeeded ? Ok(result) : ProductFailure(result);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = AuthorizationPolicies.DeleteProducts)]
    public async Task<ActionResult<ServiceResult<bool>>> DeleteAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var result = await productService.DeleteProductAsync(id, cancellationToken);

        return result.Succeeded ? Ok(result) : NotFound(result);
    }

    private ActionResult<ServiceResult<ProductDto>> ProductFailure(ServiceResult<ProductDto> result)
    {
        return result.FailureType switch
        {
            ServiceResultFailureType.NotFound => NotFound(result),
            ServiceResultFailureType.Conflict => Conflict(result),
            _ => BadRequest(result)
        };
    }
}
