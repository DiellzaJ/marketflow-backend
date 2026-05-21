using MarketFlow.Application.Common.Interfaces;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Products.DTOs;
using MarketFlow.Application.Features.Products.Interfaces;

namespace MarketFlow.Application.Features.Products.Services;

public class ProductService : IProductService
{
    private const int MaxPageSize = 100;

    private static readonly HashSet<string> AllowedSortFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "id",
        "name",
        "barcode",
        "category",
        "categoryName",
        "unitPrice",
        "costPrice",
        "taxRate",
        "minStockAlert",
        "isActive"
    };

    private readonly ITenantQueryService _tenantQueryService;

    public ProductService(ITenantQueryService tenantQueryService)
    {
        _tenantQueryService = tenantQueryService;
    }

    public async Task<ServiceResult<PagedResult<ProductDto>>> GetProductsAsync(
        ProductListQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.Page < 1)
        {
            return ServiceResult<PagedResult<ProductDto>>.Failure("Page must be greater than zero.");
        }

        if (query.PageSize is < 1 or > MaxPageSize)
        {
            return ServiceResult<PagedResult<ProductDto>>.Failure($"Page size must be between 1 and {MaxPageSize}.");
        }

        if (!string.IsNullOrWhiteSpace(query.SortBy) && !AllowedSortFields.Contains(query.SortBy))
        {
            return ServiceResult<PagedResult<ProductDto>>.Failure("Sort field is not supported.");
        }

        if (!string.IsNullOrWhiteSpace(query.SortDirection) &&
            !string.Equals(query.SortDirection, "asc", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(query.SortDirection, "desc", StringComparison.OrdinalIgnoreCase))
        {
            return ServiceResult<PagedResult<ProductDto>>.Failure("Sort direction must be asc or desc.");
        }

        var products = await _tenantQueryService.GetProductsAsync(query, cancellationToken);
        return ServiceResult<PagedResult<ProductDto>>.Success(products);
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
