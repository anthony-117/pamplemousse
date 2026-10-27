using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace Pamplemousse.Infrastructure.Caching;

/// <summary>Stores provider responses as JSON in the distributed cache (Redis, or memory when Redis is not configured).</summary>
public sealed class JsonCache(IDistributedCache cache)
{
    public async Task<T> GetOrCreateAsync<T>(string key, TimeSpan ttl, Func<CancellationToken, Task<T>> factory, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(factory);

        var cached = await cache.GetAsync(key, ct);
        if (cached is not null)
        {
            var value = JsonSerializer.Deserialize<T>(cached, JsonSerializerOptions.Web);
            if (value is not null)
            {
                return value;
            }
        }

        var fresh = await factory(ct);
        if (fresh is not null)
        {
            await cache.SetAsync(
                key,
                JsonSerializer.SerializeToUtf8Bytes(fresh, JsonSerializerOptions.Web),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl },
                ct);
        }

        return fresh;
    }
}
