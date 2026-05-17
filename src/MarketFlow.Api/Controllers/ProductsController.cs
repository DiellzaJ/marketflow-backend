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
    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.ReadProducts)]
    public async Task<ActionResult<ServiceResult<IReadOnlyCollection<ProductDto>>>> GetAsync(
        CancellationToken cancellationToken)
    {
        var result = await productService.GetProductsAsync(cancellationToken);
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.CreateProducts)]
    public async Task<ActionResult<ServiceResult<ProductDto>>> CreateAsync(
        CreateProductRequest request,
        CancellationToken cancellationToken)
    {
        var result = await productService.CreateProductAsync(request, cancellationToken);

        return result.Succeeded ? CreatedAtAction(nameof(GetAsync), result) : BadRequest(result);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = AuthorizationPolicies.UpdateProducts)]
    public async Task<ActionResult<ServiceResult<ProductDto>>> UpdateAsync(
        int id,
        UpdateProductRequest request,
        CancellationToken cancellationToken)
    {
        var result = await productService.UpdateProductAsync(id, request, cancellationToken);

        return result.Succeeded ? Ok(result) : NotFound(result);
    }

    [HttpPatch("{id:int}")]
    [Authorize(Policy = AuthorizationPolicies.UpdateProducts)]
    public async Task<ActionResult<ServiceResult<ProductDto>>> PatchAsync(
        int id,
        PatchProductRequest request,
        CancellationToken cancellationToken)
    {
        var result = await productService.PatchProductAsync(id, request, cancellationToken);

        return result.Succeeded ? Ok(result) : NotFound(result);
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
}
