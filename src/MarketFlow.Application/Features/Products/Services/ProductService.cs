using MarketFlow.Application.Common.Interfaces;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Products.Configuration;
using MarketFlow.Application.Features.Products.DTOs;
using MarketFlow.Application.Features.Products.Exceptions;
using MarketFlow.Application.Features.Products.Interfaces;

namespace MarketFlow.Application.Features.Products.Services;

public class ProductService : IProductService
{
    private const int MaxPageSize = 100;
    private const int MaxNameLength = 200;

    private readonly ITenantQueryService _tenantQueryService;

    public ProductService(ITenantQueryService tenantQueryService)
    {
        _tenantQueryService = tenantQueryService;
    }

    public async Task<ServiceResult<PagedResult<ProductDto>>> GetProductsAsync(
        ProductListQuery query,
        CancellationToken cancellationToken = default)
    {
        query.SortBy = query.SortBy?.Trim();
        query.SortDirection = query.SortDirection?.Trim();

        if (query.Page < 1)
        {
            return ServiceResult<PagedResult<ProductDto>>.Failure("Page must be greater than zero.");
        }

        if (query.PageSize is < 1 or > MaxPageSize)
        {
            return ServiceResult<PagedResult<ProductDto>>.Failure($"Page size must be between 1 and {MaxPageSize}.");
        }

        if (!ProductSortFields.IsAllowed(query.SortBy))
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
            ? ServiceResult<ProductDto>.Failure("Product was not found.", ServiceResultFailureType.NotFound)
            : ServiceResult<ProductDto>.Success(product);
    }

    public async Task<ServiceResult<ProductDto>> CreateProductAsync(
        CreateProductRequest request,
        CancellationToken cancellationToken = default)
    {
        var validationError = await ValidateProductAsync(
            request.Name,
            request.Barcode,
            request.CategoryId,
            request.UnitPrice,
            request.CostPrice,
            excludedProductId: null,
            cancellationToken);

        if (validationError is not null)
        {
            return validationError;
        }

        request.Name = request.Name.Trim();
        request.Barcode = request.Barcode?.Trim();

        try
        {
            var product = await _tenantQueryService.CreateProductAsync(request, cancellationToken);
            return ServiceResult<ProductDto>.Success(product, "Product created.");
        }
        catch (ProductBarcodeConflictException)
        {
            return BarcodeConflict();
        }
    }

    public async Task<ServiceResult<ProductDto>> UpdateProductAsync(
        int id,
        UpdateProductRequest request,
        CancellationToken cancellationToken = default)
    {
        var validationError = await ValidateProductAsync(
            request.Name,
            request.Barcode,
            request.CategoryId,
            request.UnitPrice,
            request.CostPrice,
            id,
            cancellationToken);

        if (validationError is not null)
        {
            return validationError;
        }

        request.Name = request.Name.Trim();
        request.Barcode = request.Barcode?.Trim();

        ProductDto? product;

        try
        {
            product = await _tenantQueryService.UpdateProductAsync(id, request, cancellationToken);
        }
        catch (ProductBarcodeConflictException)
        {
            return BarcodeConflict();
        }

        return product is null
            ? ServiceResult<ProductDto>.Failure("Product was not found.", ServiceResultFailureType.NotFound)
            : ServiceResult<ProductDto>.Success(product, "Product updated.");
    }

    public async Task<ServiceResult<ProductDto>> PatchProductAsync(
        int id,
        PatchProductRequest request,
        CancellationToken cancellationToken = default)
    {
        var currentProduct = await _tenantQueryService.GetProductAsync(id, cancellationToken);

        if (currentProduct is null)
        {
            return ServiceResult<ProductDto>.Failure("Product was not found.", ServiceResultFailureType.NotFound);
        }

        var validationError = await ValidateProductAsync(
            request.Name ?? currentProduct.Name,
            request.Barcode ?? currentProduct.Barcode,
            request.CategoryId ?? currentProduct.CategoryId,
            request.UnitPrice ?? currentProduct.UnitPrice,
            request.CostPrice ?? currentProduct.CostPrice,
            id,
            cancellationToken);

        if (validationError is not null)
        {
            return validationError;
        }

        request.Name = request.Name?.Trim();
        request.Barcode = request.Barcode?.Trim();

        ProductDto? product;

        try
        {
            product = await _tenantQueryService.PatchProductAsync(id, request, cancellationToken);
        }
        catch (ProductBarcodeConflictException)
        {
            return BarcodeConflict();
        }

        return product is null
            ? ServiceResult<ProductDto>.Failure("Product was not found.", ServiceResultFailureType.NotFound)
            : ServiceResult<ProductDto>.Success(product, "Product updated.");
    }

    public async Task<ServiceResult<bool>> DeleteProductAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var deleted = await _tenantQueryService.DeleteProductAsync(id, cancellationToken);

        return deleted
            ? ServiceResult<bool>.Success(true, "Product deleted.")
            : ServiceResult<bool>.Failure("Product was not found.", ServiceResultFailureType.NotFound);
    }

    private async Task<ServiceResult<ProductDto>?> ValidateProductAsync(
        string name,
        string? barcode,
        int? categoryId,
        decimal unitPrice,
        decimal costPrice,
        int? excludedProductId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return ServiceResult<ProductDto>.Failure("Product name is required.");
        }

        if (name.Trim().Length > MaxNameLength)
        {
            return ServiceResult<ProductDto>.Failure($"Product name cannot exceed {MaxNameLength} characters.");
        }

        if (unitPrice < 0)
        {
            return ServiceResult<ProductDto>.Failure("Unit price cannot be negative.");
        }

        if (costPrice < 0)
        {
            return ServiceResult<ProductDto>.Failure("Cost price cannot be negative.");
        }

        if (string.IsNullOrWhiteSpace(barcode))
        {
            return ServiceResult<ProductDto>.Failure("Barcode is required.");
        }

        if (await _tenantQueryService.ProductBarcodeExistsAsync(barcode.Trim(), excludedProductId, cancellationToken))
        {
            return ServiceResult<ProductDto>.Failure(
                "Barcode is already used by another product.",
                ServiceResultFailureType.Conflict);
        }

        if (categoryId.HasValue &&
            !await _tenantQueryService.CategoryExistsAsync(categoryId.Value, cancellationToken))
        {
            return ServiceResult<ProductDto>.Failure("Category was not found.", ServiceResultFailureType.NotFound);
        }

        return null;
    }

    private static ServiceResult<ProductDto> BarcodeConflict()
    {
        return ServiceResult<ProductDto>.Failure(
            "Barcode is already used by another product.",
            ServiceResultFailureType.Conflict);
    }
}
