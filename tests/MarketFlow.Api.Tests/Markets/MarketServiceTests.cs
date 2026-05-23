using MarketFlow.Application.Features.Markets.DTOs;
using MarketFlow.Application.Features.Markets.Interfaces;
using MarketFlow.Application.Features.Markets.Services;

namespace MarketFlow.Api.Tests.Markets;

public sealed class MarketServiceTests
{
    [Fact]
    public async Task GetMarketsAsync_ReturnsActiveTenantMarketsOrderedByNameThenId()
    {
        var northMarket = new MarketDto { Id = 11, Name = "North Market", City = "Peja", IsActive = true };
        var southMarket = new MarketDto { Id = 9, Name = "South Market", City = "Prizren", IsActive = true };
        var duplicateNameMarket = new MarketDto { Id = 7, Name = "South Market", City = "Gjakova", IsActive = true };
        var inactiveMarket = new MarketDto { Id = 5, Name = "Inactive Market", IsActive = false };
        var service = new MarketService(new StubTenantQueryService(
        [
            southMarket,
            inactiveMarket,
            northMarket,
            duplicateNameMarket
        ]));

        var result = await service.GetMarketsAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(
            [northMarket, duplicateNameMarket, southMarket],
            result.Data);
    }

    private sealed class StubTenantQueryService : IMarketQueryService
    {
        private readonly IReadOnlyCollection<MarketDto> _markets;

        public StubTenantQueryService(IReadOnlyCollection<MarketDto> markets)
        {
            _markets = markets;
        }

        public Task<IReadOnlyCollection<MarketDto>> GetMarketsAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_markets);
        }
    }
}
