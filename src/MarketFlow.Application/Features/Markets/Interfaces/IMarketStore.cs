using MarketFlow.Application.Features.Markets.DTOs;

namespace MarketFlow.Application.Features.Markets.Interfaces;

public interface IMarketStore : IMarketQueryService
{
    Task<MarketDto?> GetMarketAsync(
        int id,
        bool includeInactive = false,
        CancellationToken cancellationToken = default);

    Task<bool> MarketNameExistsAsync(
        string name,
        int? excludedMarketId = null,
        CancellationToken cancellationToken = default);

    Task<MarketDto> CreateMarketAsync(
        CreateMarketRequest request,
        CancellationToken cancellationToken = default);

    Task<MarketDto?> UpdateMarketAsync(
        int id,
        UpdateMarketRequest request,
        CancellationToken cancellationToken = default);

    Task<MarketDto?> SetMarketActiveStateAsync(
        int id,
        bool isActive,
        CancellationToken cancellationToken = default);
}
