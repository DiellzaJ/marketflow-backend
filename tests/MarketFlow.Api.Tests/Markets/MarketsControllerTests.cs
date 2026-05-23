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
        var market = CreateMarket();
        var controller = new MarketsController(new StubMarketService(market));

        var response = await controller.GetAsync(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        var result = Assert.IsType<ServiceResult<IReadOnlyCollection<MarketDto>>>(ok.Value);
        Assert.True(result.Succeeded);
        Assert.Same(market, Assert.Single(result.Data!));
    }

    [Fact]
    public async Task GetByIdAsync_WhenMarketExists_ReturnsOk()
    {
        var market = CreateMarket();
        var controller = new MarketsController(new StubMarketService(market));

        var response = await controller.GetByIdAsync(market.Id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        Assert.Same(market, Assert.IsType<ServiceResult<MarketDto>>(ok.Value).Data);
    }

    [Fact]
    public async Task CreateAsync_ReturnsCreatedAtNamedMarketRoute()
    {
        var market = CreateMarket();
        var controller = new MarketsController(new StubMarketService(market));

        var response = await controller.CreateAsync(new CreateMarketRequest { Name = market.Name }, CancellationToken.None);

        var created = Assert.IsType<CreatedAtRouteResult>(response.Result);
        Assert.Equal("GetMarketById", created.RouteName);
        Assert.Equal(market.Id, created.RouteValues?["id"]);
        Assert.Same(market, Assert.IsType<ServiceResult<MarketDto>>(created.Value).Data);
    }

    [Fact]
    public async Task CreateAsync_WhenMarketNameIsDuplicate_ReturnsConflict()
    {
        var result = ServiceResult<MarketDto>.Failure(
            "Market name is already used by another market.",
            ServiceResultFailureType.Conflict);
        var controller = new MarketsController(new StubMarketService(result));

        var response = await controller.CreateAsync(new CreateMarketRequest(), CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(response.Result);
        Assert.Same(result, conflict.Value);
    }

    [Fact]
    public async Task UpdateAsync_WhenMarketIsMissing_ReturnsNotFound()
    {
        var result = ServiceResult<MarketDto>.Failure(
            "Market was not found.",
            ServiceResultFailureType.NotFound);
        var controller = new MarketsController(new StubMarketService(result));

        var response = await controller.UpdateAsync(10, new UpdateMarketRequest(), CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(response.Result);
        Assert.Same(result, notFound.Value);
    }

    [Fact]
    public async Task DeactivateAsync_WhenMarketIsAlreadyInactive_ReturnsConflict()
    {
        var result = ServiceResult<MarketDto>.Failure(
            "Market is already inactive.",
            ServiceResultFailureType.Conflict);
        var controller = new MarketsController(new StubMarketService(result));

        var response = await controller.DeactivateAsync(10, CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(response.Result);
        Assert.Same(result, conflict.Value);
    }

    [Fact]
    public async Task ActivateAsync_WhenMarketIsActivated_ReturnsOk()
    {
        var market = CreateMarket();
        var result = ServiceResult<MarketDto>.Success(market, "Market activated.");
        var controller = new MarketsController(new StubMarketService(result));

        var response = await controller.ActivateAsync(10, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        Assert.Same(result, ok.Value);
    }

    private static MarketDto CreateMarket()
    {
        return new MarketDto
        {
            Id = 7,
            Name = "Central Market",
            City = "Prishtina",
            Address = "Main Street",
            IsActive = true
        };
    }

    private sealed class StubMarketService : IMarketService
    {
        private readonly ServiceResult<MarketDto> _result;

        public StubMarketService(MarketDto market)
        {
            _result = ServiceResult<MarketDto>.Success(market, "Market created.");
        }

        public StubMarketService(ServiceResult<MarketDto> result)
        {
            _result = result;
        }

        public Task<ServiceResult<IReadOnlyCollection<MarketDto>>> GetMarketsAsync(
            CancellationToken cancellationToken = default)
        {
            IReadOnlyCollection<MarketDto> markets = _result.Data is null ? [] : [_result.Data];
            return Task.FromResult(ServiceResult<IReadOnlyCollection<MarketDto>>.Success(markets));
        }

        public Task<ServiceResult<MarketDto>> GetMarketAsync(
            int id,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_result);
        }

        public Task<ServiceResult<MarketDto>> CreateMarketAsync(
            CreateMarketRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_result);
        }

        public Task<ServiceResult<MarketDto>> UpdateMarketAsync(
            int id,
            UpdateMarketRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_result);
        }

        public Task<ServiceResult<MarketDto>> DeactivateMarketAsync(
            int id,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_result);
        }

        public Task<ServiceResult<MarketDto>> ActivateMarketAsync(
            int id,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_result);
        }
    }
}
