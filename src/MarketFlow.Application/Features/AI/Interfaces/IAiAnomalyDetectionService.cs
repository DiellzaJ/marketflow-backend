using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.AI.DTOs;

namespace MarketFlow.Application.Features.AI.Interfaces;

public interface IAiAnomalyDetectionService
{
    Task<ServiceResult<IReadOnlyCollection<AiAnomalyDto>>> DetectAnomaliesAsync(
        AiAnomalyDetectionRequest request,
        CancellationToken cancellationToken = default);
}
