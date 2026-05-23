using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Markets.DTOs;
using MarketFlow.Application.Features.Markets.Interfaces;

namespace MarketFlow.Application.Features.Markets.Services;

public class MarketService : IMarketService
{
    private readonly IMarketQueryService _marketQueryService;

    public MarketService(IMarketQueryService marketQueryService)
    {
        _marketQueryService = marketQueryService;
    }

    public async Task<ServiceResult<IReadOnlyCollection<MarketDto>>> GetMarketsAsync(
        CancellationToken cancellationToken = default)
    {
        var markets = await _marketQueryService.GetMarketsAsync(cancellationToken);
        var activeMarkets = markets
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Id)
            .ToList();

        return ServiceResult<IReadOnlyCollection<MarketDto>>.Success(activeMarkets);
    }
}
