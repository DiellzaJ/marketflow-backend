using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.AI.DTOs;

namespace MarketFlow.Application.Features.AI.Interfaces;

public interface IAiInventoryForecastService
{
    Task<ServiceResult<AiInventoryForecastResponse>> GenerateInventoryForecastAsync(
        AiInventoryForecastRequest request,
        CancellationToken cancellationToken = default);
}
