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
}
