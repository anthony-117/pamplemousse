using Microsoft.Extensions.Logging.Abstractions;
using Pamplemousse.Domain.Assistant;
using Pamplemousse.Domain.Travellers;
using Pamplemousse.Infrastructure.Assistant;
using Pamplemousse.Infrastructure.Storage;

namespace Pamplemousse.Infrastructure.Tests.Assistant;

/// <summary>Spec 010 FR-026: a chat goes 90 days after its last message, with its messages; newer ones stay.</summary>
public class ChatSweeperTests
{
    private static readonly DateTimeOffset Now = new(2027, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static async Task AddAsync(InMemoryChatStore store, string owner, string id, DateTimeOffset last)
    {
        var first = ChatEntry.FromTraveller(1, "Hi", null, last);
        await store.CreateAsync(Chat.Start(id, new TravellerId(owner), "Hi", last).With(first), first, CancellationToken.None);
    }

    [Fact]
    public async Task Chats_past_90_days_are_deleted_for_every_account()
    {
        var store = new InMemoryChatStore();
        await AddAsync(store, "uid-fares", "old", Now.AddDays(-91));
        await AddAsync(store, "uid-maya", "older", Now.AddDays(-200));
        await AddAsync(store, "uid-fares", "recent", Now.AddDays(-89));
        var sweeper = new ChatSweeper(store, new Clock(), NullLogger<ChatSweeper>.Instance);

        Assert.Equal(2, await sweeper.SweepAsync(CancellationToken.None));

        Assert.Null(await store.GetAsync(new TravellerId("uid-fares"), "old", CancellationToken.None));
        Assert.Null(await store.GetAsync(new TravellerId("uid-maya"), "older", CancellationToken.None));
        Assert.NotNull(await store.GetAsync(new TravellerId("uid-fares"), "recent", CancellationToken.None));
        Assert.Empty(await store.MessagesAsync(new TravellerId("uid-fares"), "old", CancellationToken.None));
    }
}
