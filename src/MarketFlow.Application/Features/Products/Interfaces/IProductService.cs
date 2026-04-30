using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Products.DTOs;

namespace MarketFlow.Application.Features.Products.Interfaces;

public interface IProductService
{
    Task<ServiceResult<IReadOnlyCollection<ProductDto>>> GetProductsAsync(
        CancellationToken cancellationToken = default);
}
