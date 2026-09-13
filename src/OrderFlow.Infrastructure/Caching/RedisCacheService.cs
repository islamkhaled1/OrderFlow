using System.Text.Json;
using Microsoft.Extensions.Logging;
using OrderFlow.Application.Interfaces;
using StackExchange.Redis;

namespace OrderFlow.Infrastructure.Caching;

public class RedisCacheService : ICacheService
{
    private readonly IConnectionMultiplexer? _redis;
    private readonly ILogger<RedisCacheService> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public RedisCacheService(
        ILogger<RedisCacheService> logger,
        IConnectionMultiplexer? redis = null)
    {
        _logger = logger;
        _redis = redis;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            return default;

        try
        {
            if (_redis == null || !_redis.IsConnected)
            {
                _logger.LogWarning("Redis connection is unavailable. Skipping cache read for key '{Key}'.", key);
                return default;
            }

            var db = _redis.GetDatabase();
            var value = await db.StringGetAsync(key);

            if (!value.HasValue)
            {
                return default;
            }

            return JsonSerializer.Deserialize<T>(value.ToString(), JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis GET operation failed for key '{Key}'. Gracefully falling back to database.", key);
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

        try
        {
            if (_redis == null || !_redis.IsConnected)
            {
                _logger.LogWarning("Redis connection is unavailable. Skipping cache write for key '{Key}'.", key);
                return;
            }

            var db = _redis.GetDatabase();
            var json = JsonSerializer.Serialize(value, JsonOptions);
            await db.StringSetAsync(key, json, expiration);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis SET operation failed for key '{Key}'. Response will still succeed from database.", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;

        try
        {
            if (_redis == null || !_redis.IsConnected)
            {
                _logger.LogWarning("Redis connection is unavailable. Skipping cache invalidation for key '{Key}'.", key);
                return;
            }

            var db = _redis.GetDatabase();
            await db.KeyDeleteAsync(key);
            _logger.LogInformation("Cache key '{Key}' removed successfully.", key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis REMOVE operation failed for key '{Key}'. Continuing gracefully.", key);
        }
    }
}
