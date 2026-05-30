using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Dashboard.DTOs;

namespace MarketFlow.Application.Features.Dashboard.Interfaces;

public interface IDashboardService
{
    Task<ServiceResult<SalesSummaryDto>> GetSalesSummaryAsync(
        SalesSummaryQuery query,
        CancellationToken cancellationToken = default);
}
