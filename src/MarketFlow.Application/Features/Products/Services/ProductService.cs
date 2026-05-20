using MarketFlow.Application.Common.Interfaces;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Products.DTOs;
using MarketFlow.Application.Features.Products.Interfaces;

namespace MarketFlow.Application.Features.Products.Services;

public class ProductService : IProductService
{
    private readonly ITenantQueryService _tenantQueryService;

    public ProductService(ITenantQueryService tenantQueryService)
    {
        _tenantQueryService = tenantQueryService;
    }

    public async Task<ServiceResult<IReadOnlyCollection<ProductDto>>> GetProductsAsync(
        CancellationToken cancellationToken = default)
    {
        var products = await _tenantQueryService.GetProductsAsync(cancellationToken);
        return ServiceResult<IReadOnlyCollection<ProductDto>>.Success(products);
    }

    public async Task<ServiceResult<ProductDto>> GetProductAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var product = await _tenantQueryService.GetProductAsync(id, cancellationToken);

        return product is null
            ? ServiceResult<ProductDto>.Failure("Product was not found.")
            : ServiceResult<ProductDto>.Success(product);
    }

    public async Task<ServiceResult<ProductDto>> CreateProductAsync(
        CreateProductRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return ServiceResult<ProductDto>.Failure("Product name is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Barcode))
        {
            return ServiceResult<ProductDto>.Failure("Barcode is required.");
        }

        if (!request.CategoryId.HasValue)
        {
            return ServiceResult<ProductDto>.Failure("Category is required.");
        }

        var product = await _tenantQueryService.CreateProductAsync(request, cancellationToken);
        return ServiceResult<ProductDto>.Success(product, "Product created.");
    }

    public async Task<ServiceResult<ProductDto>> UpdateProductAsync(
        int id,
        UpdateProductRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return ServiceResult<ProductDto>.Failure("Product name is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Barcode))
        {
            return ServiceResult<ProductDto>.Failure("Barcode is required.");
        }

        if (!request.CategoryId.HasValue)
        {
            return ServiceResult<ProductDto>.Failure("Category is required.");
        }

        var product = await _tenantQueryService.UpdateProductAsync(id, request, cancellationToken);

        return product is null
            ? ServiceResult<ProductDto>.Failure("Product was not found.")
            : ServiceResult<ProductDto>.Success(product, "Product updated.");
    }

    public async Task<ServiceResult<ProductDto>> PatchProductAsync(
        int id,
        PatchProductRequest request,
        CancellationToken cancellationToken = default)
    {
        var product = await _tenantQueryService.PatchProductAsync(id, request, cancellationToken);

        return product is null
            ? ServiceResult<ProductDto>.Failure("Product was not found.")
            : ServiceResult<ProductDto>.Success(product, "Product updated.");
    }

    public async Task<ServiceResult<bool>> DeleteProductAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var deleted = await _tenantQueryService.DeleteProductAsync(id, cancellationToken);

        return deleted
            ? ServiceResult<bool>.Success(true, "Product deleted.")
            : ServiceResult<bool>.Failure("Product was not found.");
    }
}
