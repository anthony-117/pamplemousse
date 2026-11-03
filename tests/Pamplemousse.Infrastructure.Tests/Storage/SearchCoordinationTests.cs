using Pamplemousse.Application.Hotels;
using Pamplemousse.Domain.Hotels;
using Pamplemousse.Infrastructure.Caching;

namespace Pamplemousse.Infrastructure.Tests.Storage;

public class RedisCoordinationTests(RedisServer redis) : IClassFixture<RedisServer>
{
    private static readonly FixedClock Clock = new(new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.Zero));

    private readonly string _run = Guid.NewGuid().ToString("N")[..8];

    [Fact]
    public async Task Only_one_request_holds_a_search_lock_until_it_is_released()
    {
        var locks = new RedisSearchLock(redis.Connection);

        var first = await locks.TryAcquireAsync(_run, TimeSpan.FromSeconds(30), CancellationToken.None);
        var second = await locks.TryAcquireAsync(_run, TimeSpan.FromSeconds(30), CancellationToken.None);
        await first!.DisposeAsync();
        var third = await locks.TryAcquireAsync(_run, TimeSpan.FromSeconds(30), CancellationToken.None);

        Assert.Null(second);
        Assert.NotNull(third);
        Assert.True(await redis.Connection.GetDatabase().KeyExistsAsync($"lock:{_run}"));
    }

    [Fact]
    public async Task The_monthly_count_stops_at_the_limit_and_expires()
    {
        var quota = new RedisRequestQuota(redis.Connection, Clock);

        var answers = new List<bool>();
        for (var i = 0; i < 4; i++)
        {
            answers.Add(await quota.TryTakeAsync(_run, 3, CancellationToken.None));
        }

        Assert.Equal([true, true, true, false], answers);
        var ttl = await redis.Connection.GetDatabase().KeyTimeToLiveAsync($"quota:{_run}:2026-11");
        Assert.InRange(ttl!.Value.TotalDays, 34, 35);
    }
}

public class InMemoryCoordinationTests
{
    private static readonly FixedClock Clock = new(new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task The_memory_lock_works_like_the_redis_one()
    {
        var locks = new InMemorySearchLock(Clock);

        var first = await locks.TryAcquireAsync("k", TimeSpan.FromSeconds(30), CancellationToken.None);
        var second = await locks.TryAcquireAsync("k", TimeSpan.FromSeconds(30), CancellationToken.None);
        await first!.DisposeAsync();

        Assert.Null(second);
        Assert.NotNull(await locks.TryAcquireAsync("k", TimeSpan.FromSeconds(30), CancellationToken.None));
    }

    [Fact]
    public async Task An_expired_memory_lock_can_be_taken_again()
    {
        var locks = new InMemorySearchLock(Clock);

        _ = await locks.TryAcquireAsync("k", TimeSpan.Zero, CancellationToken.None);

        Assert.NotNull(await locks.TryAcquireAsync("k", TimeSpan.FromSeconds(30), CancellationToken.None));
    }

    [Fact]
    public async Task The_memory_count_stops_at_the_limit()
    {
        var quota = new InMemoryRequestQuota(Clock);

        Assert.True(await quota.TryTakeAsync("bookingcom", 1, CancellationToken.None));
        Assert.False(await quota.TryTakeAsync("bookingcom", 1, CancellationToken.None));
        Assert.True(await quota.TryTakeAsync("other", 1, CancellationToken.None));
    }

    [Fact]
    public async Task Cached_search_results_survive_the_round_trip()
    {
        var cache = new DistributedSearchCache(TestDoubles.MemoryCache());
        var value = new CachedHotelSearch(Clock.GetUtcNow(), 1, 5, "Rome", [new SnapshotPrice("bk:1", 812.40m, 203.10m, 1, null)]);

        await cache.SetAsync("hotels:k", value, TimeSpan.FromHours(12), CancellationToken.None);
        var read = await cache.GetAsync<CachedHotelSearch>("hotels:k", CancellationToken.None);

        Assert.Equal(value with { Prices = read!.Prices }, read);
        Assert.Equal(value.Prices, read.Prices);
        Assert.Null(await cache.GetAsync<CachedHotelSearch>("hotels:none", CancellationToken.None));
    }
}
