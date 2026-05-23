using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Markets.DTOs;

namespace MarketFlow.Application.Features.Markets.Interfaces;

public interface IMarketService
{
    /// <summary>
    /// Returns active markets for the authenticated user's resolved tenant, ordered by name and then id.
    /// </summary>
    Task<ServiceResult<IReadOnlyCollection<MarketDto>>> GetMarketsAsync(
        CancellationToken cancellationToken = default);
}
