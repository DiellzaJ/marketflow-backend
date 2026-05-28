using MarketFlow.Application.Common.Interfaces;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.AI.DTOs;
using MarketFlow.Application.Features.AI.Interfaces;

namespace MarketFlow.Application.Features.AI.Services;

public class AiBusinessDataService(ITenantQueryService tenantQueryService) : IAiBusinessDataService
{
    public async Task<ServiceResult<AiBusinessDataDto>> GetBusinessDataAsync(
        AiBusinessDataQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.From.HasValue && query.To.HasValue && query.From > query.To)
        {
            return ServiceResult<AiBusinessDataDto>.Failure("From date must be on or before to date.");
        }

        if ((query.MarketId.HasValue && query.MarketId <= 0) ||
            (query.DepartmentId.HasValue && query.DepartmentId <= 0))
        {
            return ServiceResult<AiBusinessDataDto>.Failure("Market and department filters must be positive values.");
        }

        var normalizedQuery = new AiBusinessDataQuery
        {
            From = query.From,
            To = query.To,
            MarketId = query.MarketId,
            DepartmentId = query.DepartmentId,
            TopProductsLimit = Math.Clamp(query.TopProductsLimit, 1, 50),
            LowStockLimit = Math.Clamp(query.LowStockLimit, 1, 100)
        };

        var data = await tenantQueryService.GetAiBusinessDataAsync(normalizedQuery, cancellationToken);
        return ServiceResult<AiBusinessDataDto>.Success(data);
    }
}
