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

    Task<ServiceResult<MarketDto>> GetMarketAsync(
        int id,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<MarketDto>> CreateMarketAsync(
        CreateMarketRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<MarketDto>> UpdateMarketAsync(
        int id,
        UpdateMarketRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<MarketDto>> DeactivateMarketAsync(
        int id,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<MarketDto>> ActivateMarketAsync(
        int id,
        CancellationToken cancellationToken = default);
}
