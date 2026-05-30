using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace MarketFlow.Infrastructure.Caching;

public sealed class RedisCacheService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string? _configuration;
    private readonly ILogger<RedisCacheService> _logger;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private IConnectionMultiplexer? _connection;

    public RedisCacheService(
        IConfiguration configuration,
        ILogger<RedisCacheService> logger)
    {
        _configuration = configuration["Redis:Configuration"];
        _logger = logger;
    }

    public Task SetAsync<T>(
        string key,
        T value,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            async database =>
            {
                var json = JsonSerializer.Serialize(value, JsonOptions);
                await database.StringSetAsync(key, json, expiration);
            },
            cancellationToken);
    }

    public Task<T?> GetAsync<T>(
        string key,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            async database =>
            {
                var value = await database.StringGetAsync(key);

                if (!value.HasValue)
                {
                    return default;
                }

                return JsonSerializer.Deserialize<T>(value.ToString(), JsonOptions);
            },
            cancellationToken);
    }

    public Task RemoveAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            async database => await database.KeyDeleteAsync(key),
            cancellationToken);
    }

    public Task RemoveByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            async database =>
            {
                if (string.IsNullOrWhiteSpace(prefix))
                {
                    return;
                }

                var connection = await GetConnectionAsync(cancellationToken);

                if (connection is null)
                {
                    return;
                }

                foreach (var endpoint in connection.GetEndPoints())
                {
                    var server = connection.GetServer(endpoint);

                    foreach (var key in server.Keys(pattern: $"{prefix}*"))
                    {
                        await database.KeyDeleteAsync(key);
                    }
                }
            },
            cancellationToken);
    }

    private async Task ExecuteAsync(
        Func<IDatabase, Task> action,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(
            async database =>
            {
                await action(database);
                return true;
            },
            cancellationToken);
    }

    private async Task<T?> ExecuteAsync<T>(
        Func<IDatabase, Task<T>> action,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_configuration))
        {
            return default;
        }

        try
        {
            var connection = await GetConnectionAsync(cancellationToken);
            return connection is null ? default : await action(connection.GetDatabase());
        }
        catch (RedisException exception)
        {
            _logger.LogWarning(exception, "Redis cache operation failed.");
            return default;
        }
    }

    private async Task<IConnectionMultiplexer?> GetConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection is { IsConnected: true })
        {
            return _connection;
        }

        await _connectionLock.WaitAsync(cancellationToken);

        try
        {
            if (_connection is { IsConnected: true })
            {
                return _connection;
            }

            _connection = await ConnectionMultiplexer.ConnectAsync(_configuration!);
            return _connection;
        }
        catch (RedisConnectionException exception)
        {
            _logger.LogWarning(exception, "Redis cache is unavailable.");
            return null;
        }
        finally
        {
            _connectionLock.Release();
        }
    }
}
