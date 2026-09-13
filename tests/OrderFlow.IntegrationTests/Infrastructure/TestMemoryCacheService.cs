using System.Collections.Concurrent;
using System.Text.Json;
using OrderFlow.Application.Interfaces;

namespace OrderFlow.IntegrationTests.Infrastructure;

public class TestMemoryCacheService : ICacheService
{
    private readonly ConcurrentDictionary<string, string> _store = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        if (_store.TryGetValue(key, out var json))
        {
            var value = JsonSerializer.Deserialize<T>(json, JsonOptions);
            return Task.FromResult(value);
        }
        return Task.FromResult<T?>(default);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan expiration, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        _store[key] = json;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        _store.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}
