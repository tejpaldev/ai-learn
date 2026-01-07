using Microsoft.Extensions.Caching.Memory;
using System.Security.Cryptography;
using System.Text;

namespace RagPipeline.Services;

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key);
    Task SetAsync<T>(string key, T value, TimeSpan? expiration = null);
    Task RemoveAsync(string key);
    Task ClearAsync();
    Task<CacheStats> GetStatsAsync();
}

public class CacheService : ICacheService
{
    private readonly IMemoryCache _cache;
    private readonly int _maxCacheSize;
    private int _currentSize;
    private readonly object _sizeLock = new();
    private long _hits;
    private long _misses;

    public CacheService(IMemoryCache cache, int maxCacheSize = 1000)
    {
        _cache = cache;
        _maxCacheSize = maxCacheSize;
        _currentSize = 0;
    }

    public Task<T?> GetAsync<T>(string key)
    {
        if (_cache.TryGetValue(key, out T? value))
        {
            Interlocked.Increment(ref _hits);
            return Task.FromResult(value);
        }

        Interlocked.Increment(ref _misses);
        return Task.FromResult<T?>(default);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? expiration = null)
    {
        lock (_sizeLock)
        {
            if (_currentSize >= _maxCacheSize)
            {
                // Simple eviction: remove this entry if at capacity
                return Task.CompletedTask;
            }

            var options = new MemoryCacheEntryOptions();

            if (expiration.HasValue)
            {
                options.AbsoluteExpirationRelativeToNow = expiration;
            }
            else
            {
                options.SlidingExpiration = TimeSpan.FromMinutes(30);
            }

            options.RegisterPostEvictionCallback((k, v, reason, state) =>
            {
                lock (_sizeLock)
                {
                    _currentSize--;
                }
            });

            _cache.Set(key, value, options);
            _currentSize++;
        }

        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key)
    {
        _cache.Remove(key);
        return Task.CompletedTask;
    }

    public Task ClearAsync()
    {
        if (_cache is MemoryCache memoryCache)
        {
            memoryCache.Compact(1.0); // Compact 100%
        }

        lock (_sizeLock)
        {
            _currentSize = 0;
        }

        return Task.CompletedTask;
    }

    public Task<CacheStats> GetStatsAsync()
    {
        var stats = new CacheStats
        {
            CurrentSize = _currentSize,
            MaxSize = _maxCacheSize,
            Hits = _hits,
            Misses = _misses,
            HitRate = _hits + _misses > 0 ? (double)_hits / (_hits + _misses) : 0
        };

        return Task.FromResult(stats);
    }

    public static string GenerateCacheKey(string prefix, params object[] components)
    {
        var combined = string.Join("|", components.Select(c => c?.ToString() ?? "null"));
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(combined));
        var hash = Convert.ToBase64String(hashBytes).Replace("/", "_").Replace("+", "-").TrimEnd('=');
        return $"{prefix}:{hash}";
    }
}

public class CacheStats
{
    public int CurrentSize { get; set; }
    public int MaxSize { get; set; }
    public long Hits { get; set; }
    public long Misses { get; set; }
    public double HitRate { get; set; }
}
