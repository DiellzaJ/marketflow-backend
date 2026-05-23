using MarketFlow.Application.Features.Markets.DTOs;

namespace MarketFlow.Application.Features.Markets.Interfaces;

public interface IMarketQueryService
{
    Task<IReadOnlyCollection<MarketDto>> GetMarketsAsync(
        CancellationToken cancellationToken = default);
}
