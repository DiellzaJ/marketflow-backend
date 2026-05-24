using MarketFlow.Application.Common.Exceptions;
using MarketFlow.Application.Common.Interfaces;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Dashboard.DTOs;
using MarketFlow.Application.Features.Dashboard.Interfaces;

namespace MarketFlow.Application.Features.Dashboard.Services;

public class DashboardService(ITenantQueryService tenantQueryService) : IDashboardService
{
    public async Task<ServiceResult<SalesSummaryDto>> GetSalesSummaryAsync(
        SalesSummaryQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.From.HasValue && query.To.HasValue && query.From > query.To)
        {
            return ServiceResult<SalesSummaryDto>.Failure("From date must be on or before to date.");
        }

        try
        {
            var summary = await tenantQueryService.GetSalesSummaryAsync(query, cancellationToken);
            return ServiceResult<SalesSummaryDto>.Success(summary);
        }
        catch (SaleReferenceNumberRepairException)
        {
            return ServiceResult<SalesSummaryDto>.Failure(
                "Sales summary could not be loaded because the tenant sales schema could not be repaired. See server logs for database diagnostics.");
        }
    }
}
