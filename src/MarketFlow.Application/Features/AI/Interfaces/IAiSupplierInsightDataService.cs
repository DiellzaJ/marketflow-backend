using MarketFlow.Application.Features.AI.DTOs;

namespace MarketFlow.Application.Features.AI.Interfaces;

public interface IAiSupplierInsightDataService
{
    Task<IReadOnlyCollection<AiSupplierInsightDataDto>> GetAiSupplierInsightDataAsync(
        AiSupplierInsightRequest request,
        CancellationToken cancellationToken = default);
}
