using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Products.DTOs;

namespace MarketFlow.Application.Features.Products.Interfaces;

public interface IProductService
{
    Task<ServiceResult<IReadOnlyCollection<ProductDto>>> GetProductsAsync(
        CancellationToken cancellationToken = default);

    Task<ServiceResult<ProductDto>> CreateProductAsync(
        CreateProductRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<ProductDto>> UpdateProductAsync(
        int id,
        UpdateProductRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<ProductDto>> PatchProductAsync(
        int id,
        PatchProductRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<bool>> DeleteProductAsync(
        int id,
        CancellationToken cancellationToken = default);
}
