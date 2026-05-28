using MarketFlow.Application.Features.AI.DTOs;

namespace MarketFlow.Application.Features.AI.Interfaces;

public interface IAiPurchaseRecommendationDataService
{
    Task<IReadOnlyCollection<AiPurchaseRecommendationDataDto>> GetAiPurchaseRecommendationDataAsync(
        AiPurchaseRecommendationRequest request,
        CancellationToken cancellationToken = default);
}
