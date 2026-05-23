using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Markets.DTOs;
using MarketFlow.Application.Features.Markets.Exceptions;
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
        var service = new MarketService(new RecordingMarketStore(
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

    [Fact]
    public async Task GetMarketAsync_WhenMarketIsInactive_ReturnsNotFoundByDefault()
    {
        var store = new RecordingMarketStore
        {
            CurrentMarket = new MarketDto { Id = 10, Name = "Central", IsActive = false }
        };
        var service = new MarketService(store);

        var result = await service.GetMarketAsync(10);

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceResultFailureType.NotFound, result.FailureType);
        Assert.Equal("Market was not found.", result.Message);
    }

    [Fact]
    public async Task CreateMarketAsync_RejectsMissingName()
    {
        var store = new RecordingMarketStore();
        var service = new MarketService(store);

        var result = await service.CreateMarketAsync(new CreateMarketRequest { Name = " " });

        Assert.False(result.Succeeded);
        Assert.Equal("Market name is required.", result.Message);
        Assert.False(store.CreateMarketWasCalled);
    }

    [Fact]
    public async Task CreateMarketAsync_RejectsDuplicateName()
    {
        var store = new RecordingMarketStore();
        store.ExistingMarketNames["Central"] = 7;
        var service = new MarketService(store);

        var result = await service.CreateMarketAsync(new CreateMarketRequest { Name = " Central " });

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceResultFailureType.Conflict, result.FailureType);
        Assert.Equal("Market name is already used by another market.", result.Message);
        Assert.False(store.CreateMarketWasCalled);
    }

    [Fact]
    public async Task CreateMarketAsync_TrimsPayload()
    {
        var store = new RecordingMarketStore();
        var service = new MarketService(store);

        var result = await service.CreateMarketAsync(new CreateMarketRequest
        {
            Name = " Central ",
            City = " Prishtina ",
            Address = " Main Street "
        });

        Assert.True(result.Succeeded);
        Assert.Equal("Market created.", result.Message);
        Assert.True(store.CreateMarketWasCalled);
        Assert.Equal("Central", result.Data?.Name);
        Assert.Equal("Prishtina", result.Data?.City);
        Assert.Equal("Main Street", result.Data?.Address);
    }

    [Fact]
    public async Task CreateMarketAsync_WhenStoreReportsDuplicateName_ReturnsConflict()
    {
        var store = new RecordingMarketStore { ThrowNameConflictOnCreate = true };
        var service = new MarketService(store);

        var result = await service.CreateMarketAsync(new CreateMarketRequest { Name = "Central" });

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceResultFailureType.Conflict, result.FailureType);
        Assert.Equal("Market name is already used by another market.", result.Message);
        Assert.True(store.CreateMarketWasCalled);
    }

    [Fact]
    public async Task UpdateMarketAsync_WhenMarketIsMissing_ReturnsNotFound()
    {
        var store = new RecordingMarketStore { CurrentMarket = null };
        var service = new MarketService(store);

        var result = await service.UpdateMarketAsync(10, new UpdateMarketRequest { Name = "Central" });

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceResultFailureType.NotFound, result.FailureType);
        Assert.Equal("Market was not found.", result.Message);
        Assert.False(store.UpdateMarketWasCalled);
    }

    [Fact]
    public async Task UpdateMarketAsync_RejectsActiveStateChange()
    {
        var store = new RecordingMarketStore();
        var service = new MarketService(store);

        var result = await service.UpdateMarketAsync(
            10,
            new UpdateMarketRequest { Name = "Central", IsActive = false });

        Assert.False(result.Succeeded);
        Assert.Equal("Use the market deactivate or activate endpoint to change active state.", result.Message);
        Assert.False(store.UpdateMarketWasCalled);
    }

    [Fact]
    public async Task UpdateMarketAsync_AllowsCurrentMarketName()
    {
        var store = new RecordingMarketStore();
        store.ExistingMarketNames["Central"] = 10;
        var service = new MarketService(store);

        var result = await service.UpdateMarketAsync(10, new UpdateMarketRequest { Name = "Central" });

        Assert.True(result.Succeeded);
        Assert.Equal("Market updated.", result.Message);
        Assert.True(store.UpdateMarketWasCalled);
    }

    [Fact]
    public async Task DeactivateMarketAsync_WhenMarketIsActive_DeactivatesMarket()
    {
        var store = new RecordingMarketStore();
        var service = new MarketService(store);

        var result = await service.DeactivateMarketAsync(10);

        Assert.True(result.Succeeded);
        Assert.Equal("Market deactivated.", result.Message);
        Assert.True(store.SetMarketActiveStateWasCalled);
        Assert.False(store.LastActiveState);
        Assert.False(result.Data?.IsActive);
    }

    [Fact]
    public async Task DeactivateMarketAsync_WhenMarketIsAlreadyInactive_ReturnsConflict()
    {
        var store = new RecordingMarketStore
        {
            CurrentMarket = new MarketDto { Id = 10, Name = "Central", IsActive = false }
        };
        var service = new MarketService(store);

        var result = await service.DeactivateMarketAsync(10);

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceResultFailureType.Conflict, result.FailureType);
        Assert.Equal("Market is already inactive.", result.Message);
        Assert.False(store.SetMarketActiveStateWasCalled);
    }

    [Fact]
    public async Task ActivateMarketAsync_WhenMarketIsInactive_ActivatesMarket()
    {
        var store = new RecordingMarketStore
        {
            CurrentMarket = new MarketDto { Id = 10, Name = "Central", IsActive = false }
        };
        var service = new MarketService(store);

        var result = await service.ActivateMarketAsync(10);

        Assert.True(result.Succeeded);
        Assert.Equal("Market activated.", result.Message);
        Assert.True(store.SetMarketActiveStateWasCalled);
        Assert.True(store.LastActiveState);
        Assert.True(result.Data?.IsActive);
    }

    [Fact]
    public async Task ActivateMarketAsync_WhenMarketIsAlreadyActive_ReturnsConflict()
    {
        var store = new RecordingMarketStore();
        var service = new MarketService(store);

        var result = await service.ActivateMarketAsync(10);

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceResultFailureType.Conflict, result.FailureType);
        Assert.Equal("Market is already active.", result.Message);
        Assert.False(store.SetMarketActiveStateWasCalled);
    }

    private sealed class RecordingMarketStore : IMarketStore
    {
        private readonly IReadOnlyCollection<MarketDto> _markets;

        public RecordingMarketStore()
            : this([])
        {
        }

        public RecordingMarketStore(IReadOnlyCollection<MarketDto> markets)
        {
            _markets = markets;
        }

        public bool CreateMarketWasCalled { get; private set; }

        public bool UpdateMarketWasCalled { get; private set; }

        public bool SetMarketActiveStateWasCalled { get; private set; }

        public bool LastActiveState { get; private set; }

        public bool ThrowNameConflictOnCreate { get; init; }

        public MarketDto? CurrentMarket { get; set; } = new()
        {
            Id = 10,
            Name = "Central",
            City = "Prishtina",
            IsActive = true
        };

        public Dictionary<string, int> ExistingMarketNames { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public Task<IReadOnlyCollection<MarketDto>> GetMarketsAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_markets);
        }

        public Task<MarketDto?> GetMarketAsync(
            int id,
            bool includeInactive = false,
            CancellationToken cancellationToken = default)
        {
            if (CurrentMarket?.Id != id)
            {
                return Task.FromResult<MarketDto?>(null);
            }

            if (!includeInactive && !CurrentMarket.IsActive)
            {
                return Task.FromResult<MarketDto?>(null);
            }

            return Task.FromResult<MarketDto?>(CurrentMarket);
        }

        public Task<bool> MarketNameExistsAsync(
            string name,
            int? excludedMarketId = null,
            CancellationToken cancellationToken = default)
        {
            var exists = ExistingMarketNames.TryGetValue(name, out var ownerId) &&
                (!excludedMarketId.HasValue || ownerId != excludedMarketId.Value);

            return Task.FromResult(exists);
        }

        public Task<MarketDto> CreateMarketAsync(
            CreateMarketRequest request,
            CancellationToken cancellationToken = default)
        {
            CreateMarketWasCalled = true;

            if (ThrowNameConflictOnCreate)
            {
                throw new MarketNameConflictException();
            }

            return Task.FromResult(new MarketDto
            {
                Id = 10,
                Name = request.Name,
                City = request.City,
                Address = request.Address,
                IsActive = true
            });
        }

        public Task<MarketDto?> UpdateMarketAsync(
            int id,
            UpdateMarketRequest request,
            CancellationToken cancellationToken = default)
        {
            UpdateMarketWasCalled = true;

            if (CurrentMarket is null || CurrentMarket.Id != id)
            {
                return Task.FromResult<MarketDto?>(null);
            }

            CurrentMarket.Name = request.Name;
            CurrentMarket.City = request.City;
            CurrentMarket.Address = request.Address;

            return Task.FromResult<MarketDto?>(CurrentMarket);
        }

        public Task<MarketDto?> SetMarketActiveStateAsync(
            int id,
            bool isActive,
            CancellationToken cancellationToken = default)
        {
            SetMarketActiveStateWasCalled = true;
            LastActiveState = isActive;

            if (CurrentMarket is null || CurrentMarket.Id != id)
            {
                return Task.FromResult<MarketDto?>(null);
            }

            CurrentMarket.IsActive = isActive;
            return Task.FromResult<MarketDto?>(CurrentMarket);
        }
    }
}
