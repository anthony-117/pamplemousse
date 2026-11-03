using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Pamplemousse.Application.Abstractions;
using StackExchange.Redis;

namespace Pamplemousse.Infrastructure.Caching;

/// <summary>Search results as JSON in the distributed cache (Redis, or memory when Redis is not configured).</summary>
public sealed class DistributedSearchCache(IDistributedCache cache) : ISearchCache
{
    public async Task<T?> GetAsync<T>(string key, CancellationToken ct)
        where T : class
    {
        var cached = await cache.GetAsync(key, ct);
        return cached is null ? null : JsonSerializer.Deserialize<T>(cached, JsonSerializerOptions.Web);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct)
        where T : class => cache.SetAsync(
            key,
            JsonSerializer.SerializeToUtf8Bytes(value, JsonSerializerOptions.Web),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl },
            ct);
}

/// <summary>A Redis lock (<c>lock:{search}</c>, ADR 0017), shared by every copy of the API.</summary>
public sealed class RedisSearchLock(IConnectionMultiplexer redis) : ISearchLock
{
    public async Task<IAsyncDisposable?> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct)
    {
        var db = redis.GetDatabase();
        var name = new RedisKey("lock:" + key);
        var token = new RedisValue(Guid.NewGuid().ToString("N"));
        return await db.LockTakeAsync(name, token, ttl) ? new Release(() => db.LockReleaseAsync(name, token)) : null;
    }

    private sealed class Release(Func<Task<bool>> release) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await release();
    }
}

/// <summary>Stands in for <see cref="RedisSearchLock"/> when Redis is not configured; only covers this process.</summary>
public sealed class InMemorySearchLock(TimeProvider clock) : ISearchLock
{
    private readonly ConcurrentDictionary<string, Holder> _held = new(StringComparer.Ordinal);

    public Task<IAsyncDisposable?> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct)
    {
        var now = clock.GetUtcNow();

        // Each caller gets its own holder, so two callers at the same instant can't both think they won.
        var mine = new Holder(now + ttl);
        var taken = ReferenceEquals(_held.AddOrUpdate(key, mine, (_, current) => current.Until <= now ? mine : current), mine);
        IAsyncDisposable? handle = taken ? new Release(() => _held.TryRemove(new KeyValuePair<string, Holder>(key, mine))) : null;
        return Task.FromResult(handle);
    }

    private sealed record Holder(DateTimeOffset Until);

    private sealed class Release(Action release) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            release();
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>Counts requests to a rate-limited source per calendar month (<c>quota:{yyyy-MM}</c>, ADR 0017).</summary>
public interface IRequestQuota
{
    /// <summary>Counts one request; false when this month already reached <paramref name="limit"/>.</summary>
    Task<bool> TryTakeAsync(string source, long limit, CancellationToken ct);
}

public sealed class RedisRequestQuota(IConnectionMultiplexer redis, TimeProvider clock) : IRequestQuota
{
    public async Task<bool> TryTakeAsync(string source, long limit, CancellationToken ct)
    {
        var db = redis.GetDatabase();
        var key = new RedisKey(QuotaKeys.For(source, clock));
        var used = await db.StringIncrementAsync(key);
        if (used == 1)
        {
            await db.KeyExpireAsync(key, QuotaKeys.Lifetime);
        }

        return used <= limit;
    }
}

/// <summary>Stands in for <see cref="RedisRequestQuota"/> when Redis is not configured; the count restarts with the process.</summary>
public sealed class InMemoryRequestQuota(TimeProvider clock) : IRequestQuota
{
    private readonly ConcurrentDictionary<string, long> _used = new(StringComparer.Ordinal);

    public Task<bool> TryTakeAsync(string source, long limit, CancellationToken ct) =>
        Task.FromResult(_used.AddOrUpdate(QuotaKeys.For(source, clock), 1, (_, n) => n + 1) <= limit);
}

internal static class QuotaKeys
{
    /// <summary>A little over a month, so the count outlives its month and then disappears.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(35);

    public static string For(string source, TimeProvider clock) =>
        string.Create(CultureInfo.InvariantCulture, $"quota:{source}:{clock.GetUtcNow():yyyy-MM}");
}
