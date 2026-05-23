using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Markets.DTOs;
using MarketFlow.Application.Features.Markets.Exceptions;
using MarketFlow.Application.Features.Markets.Interfaces;

namespace MarketFlow.Application.Features.Markets.Services;

public class MarketService : IMarketService
{
    private const int MaxNameLength = 150;
    private const int MaxCityLength = 100;
    private const string ActiveStateChangeMessage =
        "Use the market deactivate or activate endpoint to change active state.";

    private readonly IMarketStore _marketStore;

    public MarketService(IMarketStore marketStore)
    {
        _marketStore = marketStore;
    }

    public async Task<ServiceResult<IReadOnlyCollection<MarketDto>>> GetMarketsAsync(
        CancellationToken cancellationToken = default)
    {
        var markets = await _marketStore.GetMarketsAsync(cancellationToken);
        var activeMarkets = markets
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Id)
            .ToList();

        return ServiceResult<IReadOnlyCollection<MarketDto>>.Success(activeMarkets);
    }

    public async Task<ServiceResult<MarketDto>> GetMarketAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var market = await _marketStore.GetMarketAsync(id, cancellationToken: cancellationToken);

        return market is null
            ? ServiceResult<MarketDto>.Failure("Market was not found.", ServiceResultFailureType.NotFound)
            : ServiceResult<MarketDto>.Success(market);
    }

    public async Task<ServiceResult<MarketDto>> CreateMarketAsync(
        CreateMarketRequest request,
        CancellationToken cancellationToken = default)
    {
        var validationError = await ValidateMarketAsync(
            request.Name,
            request.City,
            excludedMarketId: null,
            cancellationToken);

        if (validationError is not null)
        {
            return validationError;
        }

        request.Name = request.Name.Trim();
        request.City = NormalizeOptionalText(request.City);
        request.Address = NormalizeOptionalText(request.Address);

        try
        {
            var market = await _marketStore.CreateMarketAsync(request, cancellationToken);
            return ServiceResult<MarketDto>.Success(market, "Market created.");
        }
        catch (MarketNameConflictException)
        {
            return NameConflict();
        }
    }

    public async Task<ServiceResult<MarketDto>> UpdateMarketAsync(
        int id,
        UpdateMarketRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.IsActive.HasValue)
        {
            return ServiceResult<MarketDto>.Failure(ActiveStateChangeMessage);
        }

        var validationError = ValidateMarketShape(request.Name, request.City);

        if (validationError is not null)
        {
            return validationError;
        }

        request.Name = request.Name.Trim();
        request.City = NormalizeOptionalText(request.City);
        request.Address = NormalizeOptionalText(request.Address);

        MarketDto? market;

        try
        {
            market = await _marketStore.UpdateMarketAsync(id, request, cancellationToken);
        }
        catch (MarketNameConflictException)
        {
            return NameConflict();
        }

        return market is null
            ? ServiceResult<MarketDto>.Failure("Market was not found.", ServiceResultFailureType.NotFound)
            : ServiceResult<MarketDto>.Success(market, "Market updated.");
    }

    public async Task<ServiceResult<MarketDto>> DeactivateMarketAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var currentMarket = await _marketStore.GetMarketAsync(
            id,
            includeInactive: true,
            cancellationToken);

        if (currentMarket is null)
        {
            return ServiceResult<MarketDto>.Failure("Market was not found.", ServiceResultFailureType.NotFound);
        }

        if (!currentMarket.IsActive)
        {
            return ServiceResult<MarketDto>.Failure("Market is already inactive.", ServiceResultFailureType.Conflict);
        }

        var market = await _marketStore.SetMarketActiveStateAsync(
            id,
            isActive: false,
            cancellationToken: cancellationToken);

        if (market is not null)
        {
            return ServiceResult<MarketDto>.Success(market, "Market deactivated.");
        }

        currentMarket = await _marketStore.GetMarketAsync(
            id,
            includeInactive: true,
            cancellationToken);

        if (currentMarket is null)
        {
            return ServiceResult<MarketDto>.Failure("Market was not found.", ServiceResultFailureType.NotFound);
        }

        return !currentMarket.IsActive
            ? ServiceResult<MarketDto>.Failure("Market is already inactive.", ServiceResultFailureType.Conflict)
            : ServiceResult<MarketDto>.Failure("Market active state could not be changed.");
    }

    public async Task<ServiceResult<MarketDto>> ActivateMarketAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var currentMarket = await _marketStore.GetMarketAsync(
            id,
            includeInactive: true,
            cancellationToken);

        if (currentMarket is null)
        {
            return ServiceResult<MarketDto>.Failure("Market was not found.", ServiceResultFailureType.NotFound);
        }

        if (currentMarket.IsActive)
        {
            return ServiceResult<MarketDto>.Failure("Market is already active.", ServiceResultFailureType.Conflict);
        }

        var market = await _marketStore.SetMarketActiveStateAsync(
            id,
            isActive: true,
            cancellationToken: cancellationToken);

        if (market is not null)
        {
            return ServiceResult<MarketDto>.Success(market, "Market activated.");
        }

        currentMarket = await _marketStore.GetMarketAsync(
            id,
            includeInactive: true,
            cancellationToken);

        if (currentMarket is null)
        {
            return ServiceResult<MarketDto>.Failure("Market was not found.", ServiceResultFailureType.NotFound);
        }

        return currentMarket.IsActive
            ? ServiceResult<MarketDto>.Failure("Market is already active.", ServiceResultFailureType.Conflict)
            : ServiceResult<MarketDto>.Failure("Market active state could not be changed.");
    }

    private async Task<ServiceResult<MarketDto>?> ValidateMarketAsync(
        string name,
        string? city,
        int? excludedMarketId,
        CancellationToken cancellationToken)
    {
        var validationError = ValidateMarketShape(name, city);

        if (validationError is not null)
        {
            return validationError;
        }

        if (await _marketStore.MarketNameExistsAsync(name.Trim(), excludedMarketId, cancellationToken))
        {
            return NameConflict();
        }

        return null;
    }

    private static ServiceResult<MarketDto>? ValidateMarketShape(string name, string? city)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return ServiceResult<MarketDto>.Failure("Market name is required.");
        }

        if (name.Trim().Length > MaxNameLength)
        {
            return ServiceResult<MarketDto>.Failure($"Market name cannot exceed {MaxNameLength} characters.");
        }

        if (!string.IsNullOrWhiteSpace(city) && city.Trim().Length > MaxCityLength)
        {
            return ServiceResult<MarketDto>.Failure($"Market city cannot exceed {MaxCityLength} characters.");
        }

        return null;
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static ServiceResult<MarketDto> NameConflict()
    {
        return ServiceResult<MarketDto>.Failure(
            "Market name is already used by another market.",
            ServiceResultFailureType.Conflict);
    }
}
