using Pamplemousse.Infrastructure.Caching;

namespace Pamplemousse.Infrastructure.Tests.Caching;

public class JsonCacheTests
{
    private sealed record Item(string Name, int Count);

    [Fact]
    public async Task Second_call_is_served_from_the_cache()
    {
        var cache = new JsonCache(TestDoubles.MemoryCache());
        var calls = 0;
        Task<Item> Factory(CancellationToken _) => Task.FromResult(new Item("rome", ++calls));

        var first = await cache.GetOrCreateAsync("k", TimeSpan.FromMinutes(5), Factory, CancellationToken.None);
        var second = await cache.GetOrCreateAsync("k", TimeSpan.FromMinutes(5), Factory, CancellationToken.None);

        Assert.Equal(1, calls);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Null_results_are_not_cached()
    {
        var cache = new JsonCache(TestDoubles.MemoryCache());
        var calls = 0;
        Task<Item?> Factory(CancellationToken _)
        {
            calls++;
            return Task.FromResult<Item?>(null);
        }

        await cache.GetOrCreateAsync("k", TimeSpan.FromMinutes(5), Factory, CancellationToken.None);
        await cache.GetOrCreateAsync("k", TimeSpan.FromMinutes(5), Factory, CancellationToken.None);

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Different_keys_do_not_share_entries()
    {
        var cache = new JsonCache(TestDoubles.MemoryCache());

        var a = await cache.GetOrCreateAsync("a", TimeSpan.FromMinutes(5), _ => Task.FromResult(new Item("a", 1)), CancellationToken.None);
        var b = await cache.GetOrCreateAsync("b", TimeSpan.FromMinutes(5), _ => Task.FromResult(new Item("b", 2)), CancellationToken.None);

        Assert.NotEqual(a, b);
    }
}
