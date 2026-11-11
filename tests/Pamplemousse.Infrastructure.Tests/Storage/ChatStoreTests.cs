using Pamplemousse.Application.Abstractions;
using Pamplemousse.Domain.Assistant;
using Pamplemousse.Domain.Itinerary;
using Pamplemousse.Domain.Ranking;
using Pamplemousse.Domain.Travellers;
using Pamplemousse.Infrastructure.Storage;

namespace Pamplemousse.Infrastructure.Tests.Storage;

/// <summary>The same checks on both chat stores (spec 010 FR-022 to FR-026, research R-6 to R-8).</summary>
public abstract class ChatStoreContract
{
    // Recent, so another class's sweep never takes these chats.
    private static readonly DateTimeOffset Start = DateTimeOffset.UtcNow.AddDays(-1);

    protected TravellerId Owner { get; } = new("uid-" + Guid.NewGuid().ToString("N")[..10]);

    protected abstract IChatStore Store { get; }

    protected async Task<Chat> StartAsync(string id, string text, DateTimeOffset? at = null, TravellerId? owner = null)
    {
        var when = at ?? Start;
        var first = ChatEntry.FromTraveller(1, text, null, when);
        var chat = (Chat.Start(id, owner ?? Owner, text, when) with
        {
            Trip = new TripContext("3169070", new DateOnly(2027, 4, 10), new DateOnly(2027, 4, 14), 2, null, "LB", "USD", 1200),
            Preferences = new Weights(90, 90, 50, 20, 20, 50),
            Pace = Pace.Packed,
        }).With(first);
        await Store.CreateAsync(chat, first, CancellationToken.None);
        return chat;
    }

    private static ChatEntry Answer(int sequence, DateTimeOffset at) => new(
        ChatRules.MessageId(sequence),
        Speaker.Assistant,
        "Here you go",
        at,
        null,
        [new ToolStep("search_hotels", "Searched hotels in Rome · 48 found", true, 48, ["""{"kind":"hotel","id":"bk:1"}"""])],
        ["""{"kind":"hotel","id":"bk:1"}"""],
        ["Find flights too"],
        [],
        "p1");

    [Fact]
    public async Task A_chat_reads_back_with_its_trip_preferences_and_messages_in_order()
    {
        var chat = await StartAsync("c1", "Hotels in Rome");
        var answer = Answer(2, Start.AddMinutes(1));
        await Store.SaveAsync(chat.With(answer), answer, CancellationToken.None);

        var read = await Store.GetAsync(Owner, "c1", CancellationToken.None);
        var messages = await Store.MessagesAsync(Owner, "c1", CancellationToken.None);
        var last = await Store.LastMessagesAsync(Owner, "c1", 1, CancellationToken.None);

        Assert.NotNull(read);
        Assert.Equal(("Hotels in Rome", 2, Pace.Packed), (read.Title, read.MessageCount, read.Pace));
        Assert.Equal(new DateOnly(2027, 4, 10), read.Trip?.From);
        Assert.Equal(1200, read.Trip?.TripBudget);
        Assert.Equal(90, read.Preferences?.Culture);
        Assert.Equal<string>(["0001", "0002"], [.. messages.Select(m => m.Id)]);
        Assert.Equal("search_hotels", messages[1].Steps[0].Tool);
        Assert.Equal("""{"kind":"hotel","id":"bk:1"}""", messages[1].Cards[0]);
        Assert.Equal("p1", messages[1].PlanChanged);
        Assert.Equal("0002", Assert.Single(last).Id);
    }

    [Fact]
    public async Task Pages_come_latest_first_and_search_finds_whole_words()
    {
        await StartAsync("a", "Jazz in Lisbon", Start);
        await StartAsync("b", "Museums in Rome", Start.AddMinutes(1));
        await StartAsync("c", "Cafés in Paris", Start.AddMinutes(2));

        var first = await Store.PageAsync(Owner, 2, null, CancellationToken.None);
        var rest = await Store.PageAsync(Owner, 2, first.Next, CancellationToken.None);

        Assert.Equal<string>(["c", "b"], [.. first.Items.Select(c => c.Id)]);
        Assert.Equal<string>(["a"], [.. rest.Items.Select(c => c.Id)]);
        Assert.Null(rest.Next);
        Assert.Equal("a", Assert.Single(await Store.SearchAsync(Owner, "jazz", CancellationToken.None)).Id);
        Assert.Equal("c", Assert.Single(await Store.SearchAsync(Owner, "cafes", CancellationToken.None)).Id);
        Assert.Equal("a", (await Store.OldestAsync(Owner, CancellationToken.None))?.Id);
        Assert.Equal(3, await Store.CountAsync(Owner, CancellationToken.None));
    }

    [Fact]
    public async Task The_lease_holds_one_answer_at_a_time_and_frees_itself()
    {
        await StartAsync("c1", "Hi");
        var now = DateTimeOffset.UtcNow;

        Assert.True(await Store.TryTakeLeaseAsync(Owner, "c1", now, now.AddSeconds(70), CancellationToken.None));
        Assert.False(await Store.TryTakeLeaseAsync(Owner, "c1", now.AddSeconds(5), now.AddSeconds(75), CancellationToken.None));
        Assert.True(await Store.TryTakeLeaseAsync(Owner, "c1", now.AddSeconds(71), now.AddSeconds(141), CancellationToken.None));
        Assert.False(await Store.TryTakeLeaseAsync(Owner, "nope", now, now.AddSeconds(70), CancellationToken.None));
    }

    [Fact]
    public async Task Deleting_takes_the_messages_and_only_the_owner_s_chats()
    {
        var maya = new TravellerId("uid-maya-" + Guid.NewGuid().ToString("N")[..6]);
        await StartAsync("c1", "Hi");
        await StartAsync("c2", "Hi again");
        await StartAsync("m1", "Hi", owner: maya);

        Assert.True(await Store.DeleteAsync(Owner, "c1", CancellationToken.None));
        Assert.False(await Store.DeleteAsync(Owner, "c1", CancellationToken.None));
        Assert.Empty(await Store.MessagesAsync(Owner, "c1", CancellationToken.None));
        Assert.Null(await Store.GetAsync(maya, "c2", CancellationToken.None));

        await Store.DeleteAllAsync(Owner, CancellationToken.None);
        Assert.Equal(0, await Store.CountAsync(Owner, CancellationToken.None));
        Assert.Equal(1, await Store.CountAsync(maya, CancellationToken.None));
    }

    [Fact]
    public async Task The_sweep_deletes_chats_past_their_last_message_only()
    {
        var old = DateTimeOffset.UtcNow.AddDays(-120);
        await StartAsync("old", "Hi", old);
        await StartAsync("new", "Hi");

        Assert.True(await Store.DeleteExpiredAsync(DateTimeOffset.UtcNow.AddDays(-ChatRules.KeepDays), CancellationToken.None) >= 1);

        Assert.Null(await Store.GetAsync(Owner, "old", CancellationToken.None));
        Assert.NotNull(await Store.GetAsync(Owner, "new", CancellationToken.None));
    }
}

public sealed class InMemoryChatStoreTests : ChatStoreContract
{
    protected override IChatStore Store { get; } = new InMemoryChatStore();
}

public sealed class FirestoreChatStoreTests(FirestoreEmulator emulator) : ChatStoreContract, IClassFixture<FirestoreEmulator>
{
    private IChatStore? _store;

    protected override IChatStore Store => _store ??= new FirestoreChatStore(emulator.Db);
}
