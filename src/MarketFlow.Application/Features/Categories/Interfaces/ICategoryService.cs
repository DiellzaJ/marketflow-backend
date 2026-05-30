using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Categories.DTOs;

namespace MarketFlow.Application.Features.Categories.Interfaces;

public interface ICategoryService
{
    Task<ServiceResult<IReadOnlyCollection<CategoryDto>>> GetCategoriesAsync(
        CancellationToken cancellationToken = default);
}
