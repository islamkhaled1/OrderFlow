using System.Collections.Concurrent;
using System.Text.Json;
using OrderFlow.Application.Interfaces;

namespace OrderFlow.IntegrationTests.Infrastructure;

/// <summary>
/// In-memory ICacheService following the same contract as the Redis implementation:
/// while <see cref="IsAvailable"/> is false, reads miss, writes are dropped and removals report failure
/// (this simulates a Redis outage without needing a Redis server).
/// </summary>
public class TestMemoryCacheService : ICacheService
{
    private readonly ConcurrentDictionary<string, string> _store = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public volatile bool IsAvailable = true;
    private int _hits;
    private int _misses;
    private int _removes;

    public int Removes => _removes;

    public int Hits => _hits;
    public int Misses => _misses;
    public bool Contains(string key) => _store.ContainsKey(key);

    public void ResetCounters()
    {
        Interlocked.Exchange(ref _hits, 0);
        Interlocked.Exchange(ref _misses, 0);
    }

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        if (IsAvailable && _store.TryGetValue(key, out var json))
        {
            Interlocked.Increment(ref _hits);
            return Task.FromResult(JsonSerializer.Deserialize<T>(json, JsonOptions));
        }

        Interlocked.Increment(ref _misses);
        return Task.FromResult<T?>(default);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan expiration, CancellationToken cancellationToken = default)
    {
        if (IsAvailable)
            _store[key] = JsonSerializer.Serialize(value, JsonOptions);
        return Task.CompletedTask;
    }

    public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
            return Task.FromResult(false);

        _store.TryRemove(key, out _);
        Interlocked.Increment(ref _removes);
        return Task.FromResult(true);
    }
}
