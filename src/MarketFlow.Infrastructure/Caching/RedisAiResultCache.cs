using MarketFlow.Application.Features.AI.Interfaces;
using MarketFlow.Application.Features.AI.Services;

namespace MarketFlow.Infrastructure.Caching;

public sealed class RedisAiResultCache(RedisCacheService cacheService) : IAiResultCache
{
    public Task<T?> GetAsync<T>(
        string key,
        CancellationToken cancellationToken = default) =>
        cacheService.GetAsync<T>(key, cancellationToken);

    public Task SetAsync<T>(
        string key,
        T value,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default) =>
        cacheService.SetAsync(key, value, expiration, cancellationToken);

    public async Task InvalidateCompanyAsync(
        int companyId,
        CancellationToken cancellationToken = default)
    {
        foreach (var prefix in AiCacheKeys.CompanyPrefixes(companyId))
        {
            await cacheService.RemoveByPrefixAsync(prefix, cancellationToken);
        }
    }
}
