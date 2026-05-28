using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.AI.DTOs;

namespace MarketFlow.Application.Features.AI.Interfaces;

public interface IAiBusinessDataService
{
    Task<ServiceResult<AiBusinessDataDto>> GetBusinessDataAsync(
        AiBusinessDataQuery query,
        CancellationToken cancellationToken = default);
}
