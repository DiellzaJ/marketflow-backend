using MarketFlow.Application.Common.Interfaces;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Categories.DTOs;
using MarketFlow.Application.Features.Categories.Interfaces;

namespace MarketFlow.Application.Features.Categories.Services;

public class CategoryService : ICategoryService
{
    private readonly ITenantQueryService _tenantQueryService;

    public CategoryService(ITenantQueryService tenantQueryService)
    {
        _tenantQueryService = tenantQueryService;
    }

    public async Task<ServiceResult<IReadOnlyCollection<CategoryDto>>> GetCategoriesAsync(
        CancellationToken cancellationToken = default)
    {
        var categories = await _tenantQueryService.GetCategoriesAsync(cancellationToken);
        return ServiceResult<IReadOnlyCollection<CategoryDto>>.Success(categories);
    }
}
