using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.AI.DTOs;

namespace MarketFlow.Application.Features.AI.Interfaces;

public interface IAiDashboardService
{
    Task<ServiceResult<AiDashboardSummaryResponse>> GenerateDashboardSummaryAsync(
        AiDashboardSummaryRequest request,
        CancellationToken cancellationToken = default);
}
