namespace OrderFlow.Application.Interfaces;

public interface ICacheService
{
    /// <summary>Returns the cached value, or default on a miss or an expected cache outage.</summary>
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    Task SetAsync<T>(
        string key,
        T value,
        TimeSpan expiration,
        CancellationToken cancellationToken = default);

    /// <summary>Returns false when the cache was unreachable and the key may still be present.</summary>
    Task<bool> RemoveAsync(
        string key,
        CancellationToken cancellationToken = default);
}
