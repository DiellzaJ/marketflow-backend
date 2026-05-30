namespace MarketFlow.Application.Features.AI.Interfaces;

public interface IAiResultCache
{
    Task<T?> GetAsync<T>(
        string key,
        CancellationToken cancellationToken = default);

    Task SetAsync<T>(
        string key,
        T value,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default);

    Task InvalidateCompanyAsync(
        int companyId,
        CancellationToken cancellationToken = default);
}
