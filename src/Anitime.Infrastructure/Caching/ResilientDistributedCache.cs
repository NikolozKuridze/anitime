using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace Anitime.Infrastructure.Caching;

/// <summary>The cache is an optimization: an outage must read as a miss, never fail a request that already saved data.</summary>
public sealed class ResilientDistributedCache(IDistributedCache inner, ILogger<ResilientDistributedCache> logger) : IDistributedCache
{
    public byte[]? Get(string key) => Guard(() => inner.Get(key), key);
    public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => GuardAsync(() => inner.GetAsync(key, token), key);

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options) =>
        Guard<object?>(() => { inner.Set(key, value, options); return null; }, key);

    public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) =>
        GuardAsync<object?>(async () => { await inner.SetAsync(key, value, options, token); return null; }, key);

    public void Refresh(string key) => Guard<object?>(() => { inner.Refresh(key); return null; }, key);

    public Task RefreshAsync(string key, CancellationToken token = default) =>
        GuardAsync<object?>(async () => { await inner.RefreshAsync(key, token); return null; }, key);

    public void Remove(string key) => Guard<object?>(() => { inner.Remove(key); return null; }, key);

    public Task RemoveAsync(string key, CancellationToken token = default) =>
        GuardAsync<object?>(async () => { await inner.RemoveAsync(key, token); return null; }, key);

    private T? Guard<T>(Func<T?> action, string key)
    {
        try
        {
            return action();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Cache operation failed for {CacheKey}", key);
            return default;
        }
    }

    private async Task<T?> GuardAsync<T>(Func<Task<T?>> action, string key)
    {
        try
        {
            return await action();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Cache operation failed for {CacheKey}", key);
            return default;
        }
    }
}
