using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.AI.DTOs;

namespace MarketFlow.Application.Features.AI.Interfaces;

public interface IAiInventoryInsightService
{
    Task<ServiceResult<IReadOnlyCollection<AiInventoryRecommendationDto>>> GenerateInventoryRecommendationsAsync(
        AiInventoryRecommendationRequest request,
        CancellationToken cancellationToken = default);
}
