namespace MarketFlow.Infrastructure.Caching;

public class RedisCacheService
{
    public Task SetAsync<T>(
        string key,
        T value,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<T?> GetAsync<T>(
        string key,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(default(T));
    }
}
