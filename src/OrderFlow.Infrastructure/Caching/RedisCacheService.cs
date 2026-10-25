using System.Text.Json;
using Microsoft.Extensions.Logging;
using OrderFlow.Application.Interfaces;
using OrderFlow.Infrastructure.Observability;
using StackExchange.Redis;

namespace OrderFlow.Infrastructure.Caching;

/// <summary>
/// Redis cache-aside implementation. The cache is an optimisation, never a source of truth, so EXPECTED
/// failures (Redis unreachable, command timeout, corrupt cached JSON) degrade to a cache miss with a
/// warning log and the <c>orderflow.cache.errors</c> metric. Cancellation and programming errors are NOT
/// swallowed. Latency of a failing Redis is bounded by the multiplexer's AsyncTimeout (see DI).
/// </summary>
public class RedisCacheService : ICacheService
{
    private readonly IConnectionMultiplexer? _redis;
    private readonly InfrastructureMetrics _metrics;
    private readonly ILogger<RedisCacheService> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public RedisCacheService(
        ILogger<RedisCacheService> logger,
        InfrastructureMetrics metrics,
        IConnectionMultiplexer? redis = null)
    {
        _logger = logger;
        _metrics = metrics;
        _redis = redis;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            return default;

        if (!TryGetDatabase("get", key, out var db))
            return default;

        RedisValue value;
        try
        {
            value = await db.StringGetAsync(key).WaitAsync(cancellationToken);
        }
        catch (Exception ex) when (IsExpectedRedisFailure(ex))
        {
            LogFailure("get", key, ex);
            return default;
        }

        if (!value.HasValue)
        {
            _metrics.CacheMiss();
            return default;
        }

        try
        {
            var result = JsonSerializer.Deserialize<T>(value.ToString(), JsonOptions);
            _metrics.CacheHit();
            return result;
        }
        catch (JsonException ex)
        {
            // Entry written by an incompatible version / corrupted: drop it and treat as a miss.
            LogFailure("deserialize", key, ex);
            await RemoveAsync(key, cancellationToken);
            return default;
        }
    }

    public async Task SetAsync<T>(
        string key,
        T value,
        TimeSpan expiration,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key) || value == null)
            return;

        if (!TryGetDatabase("set", key, out var db))
            return;

        try
        {
            var json = JsonSerializer.Serialize(value, JsonOptions);
            await db.StringSetAsync(key, json, expiration).WaitAsync(cancellationToken);
        }
        catch (Exception ex) when (IsExpectedRedisFailure(ex))
        {
            LogFailure("set", key, ex);
        }
    }

    public async Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            return true;

        if (!TryGetDatabase("remove", key, out var db))
            return false;

        try
        {
            await db.KeyDeleteAsync(key).WaitAsync(cancellationToken);
            return true;
        }
        catch (Exception ex) when (IsExpectedRedisFailure(ex))
        {
            LogFailure("remove", key, ex);
            return false;
        }
    }

    private bool TryGetDatabase(string operation, string key, out IDatabase db)
    {
        if (_redis is { IsConnected: true })
        {
            db = _redis.GetDatabase();
            return true;
        }

        _metrics.CacheError(operation);
        _logger.LogWarning("Redis connection is unavailable. Skipping cache {Operation} for key '{Key}'.", operation, key);
        db = null!;
        return false;
    }

    private static bool IsExpectedRedisFailure(Exception ex) =>
        ex is RedisException or TimeoutException;

    private void LogFailure(string operation, string key, Exception ex)
    {
        _metrics.CacheError(operation);
        _logger.LogWarning(ex, "Redis {Operation} failed for key '{Key}'; degrading to database.", operation, key);
    }
}
