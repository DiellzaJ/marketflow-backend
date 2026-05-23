using MarketFlow.Api.Controllers;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Markets.DTOs;
using MarketFlow.Application.Features.Markets.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace MarketFlow.Api.Tests.Markets;

public sealed class MarketsControllerTests
{
    [Fact]
    public async Task GetAsync_ReturnsOkWithMarkets()
    {
        var market = new MarketDto
        {
            Id = 7,
            Name = "Central Market",
            City = "Prishtina",
            Address = "Main Street",
            IsActive = true
        };
        var controller = new MarketsController(new StubMarketService([market]));

        var response = await controller.GetAsync(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        var result = Assert.IsType<ServiceResult<IReadOnlyCollection<MarketDto>>>(ok.Value);
        Assert.True(result.Succeeded);
        Assert.Same(market, Assert.Single(result.Data!));
    }

    private sealed class StubMarketService : IMarketService
    {
        private readonly IReadOnlyCollection<MarketDto> _markets;

        public StubMarketService(IReadOnlyCollection<MarketDto> markets)
        {
            _markets = markets;
        }

        public Task<ServiceResult<IReadOnlyCollection<MarketDto>>> GetMarketsAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ServiceResult<IReadOnlyCollection<MarketDto>>.Success(_markets));
        }
    }
}
