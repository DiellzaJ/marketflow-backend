using MarketFlow.Application.Features.AI.DTOs;

namespace MarketFlow.Application.Features.AI.Interfaces;

public interface IAiInventoryInsightDataService
{
    Task<IReadOnlyCollection<AiInventoryInsightDataDto>> GetAiInventoryInsightDataAsync(
        AiInventoryRecommendationRequest request,
        CancellationToken cancellationToken = default);
}
