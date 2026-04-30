using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Products.DTOs;
using MarketFlow.Application.Features.Products.Interfaces;

namespace MarketFlow.Application.Features.Products.Services;

public class ProductService : IProductService
{
    public Task<ServiceResult<IReadOnlyCollection<ProductDto>>> GetProductsAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<ProductDto> products = Array.Empty<ProductDto>();
        return Task.FromResult(ServiceResult<IReadOnlyCollection<ProductDto>>.Success(products));
    }
}
