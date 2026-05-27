using MarketFlow.Application.Features.AI.DTOs;

namespace MarketFlow.Application.Features.AI.Interfaces;

public interface IAiInventoryForecastDataService
{
    Task<IReadOnlyCollection<AiInventoryForecastDataDto>> GetAiInventoryForecastDataAsync(
        AiInventoryForecastRequest request,
        CancellationToken cancellationToken = default);
}
