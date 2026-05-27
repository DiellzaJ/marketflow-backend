using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.AI.DTOs;

namespace MarketFlow.Application.Features.AI.Interfaces;

public interface IAiSupplierInsightService
{
    Task<ServiceResult<IReadOnlyCollection<AiSupplierInsightDto>>> GenerateSupplierPerformanceInsightsAsync(
        AiSupplierInsightRequest request,
        CancellationToken cancellationToken = default);
}
