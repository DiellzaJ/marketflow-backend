using MarketFlow.Application.Features.AI.DTOs;

namespace MarketFlow.Application.Features.AI.Interfaces;

public interface IAiAnomalyDetectionDataService
{
    Task<AiAnomalyDetectionDataDto> GetAiAnomalyDetectionDataAsync(
        AiAnomalyDetectionRequest request,
        CancellationToken cancellationToken = default);
}
