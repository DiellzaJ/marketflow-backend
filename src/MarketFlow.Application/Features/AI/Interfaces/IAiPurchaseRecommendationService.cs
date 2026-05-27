using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.AI.DTOs;

namespace MarketFlow.Application.Features.AI.Interfaces;

public interface IAiPurchaseRecommendationService
{
    Task<ServiceResult<IReadOnlyCollection<AiPurchaseRecommendationDto>>> GeneratePurchaseRecommendationsAsync(
        AiPurchaseRecommendationRequest request,
        CancellationToken cancellationToken = default);
}
