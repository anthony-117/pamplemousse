using Pamplemousse.Application.Abstractions;
using Pamplemousse.Domain.Saved;
using Pamplemousse.Domain.Travellers;
using Pamplemousse.Infrastructure.Storage;

namespace Pamplemousse.Infrastructure.Tests.Storage;

/// <summary>The same checks on both saved-item stores (spec 010 FR-020, FR-021).</summary>
public abstract class SavedStoreContract
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 10, 0, 0, TimeSpan.Zero);

    protected TravellerId Owner { get; } = new("uid-" + Guid.NewGuid().ToString("N")[..10]);

    protected abstract ISavedStore Store { get; }

    private SavedItem Item(SavedKind kind, string id, int minutes, string? item = """{"id":"x"}""") =>
        SavedItem.Create(Owner, kind, id, "3169070", "Rome", "Name " + id, "https://img.test/a.jpg", 41.9, 12.49, Now.AddMinutes(minutes), item);

    [Fact]
    public async Task Items_read_back_newest_first_with_every_field()
    {
        await Store.PutAsync(Item(SavedKind.Hotel, "bk:1", 0), CancellationToken.None);
        await Store.PutAsync(Item(SavedKind.Place, "wd:Q10285/with-slash", 1, null), CancellationToken.None);

        var all = await Store.ListAsync(Owner, CancellationToken.None);
        var place = await Store.GetAsync(Owner, SavedKind.Place, "wd:Q10285/with-slash", CancellationToken.None);

        Assert.Equal<string>(["wd:Q10285/with-slash", "bk:1"], [.. all.Select(i => i.Id)]);
        Assert.Equal(2, await Store.CountAsync(Owner, CancellationToken.None));
        Assert.NotNull(place);
        Assert.Equal(("Rome", 41.9, null), (place.DestinationName, place.Location.Lat, place.ItemJson));
        Assert.Equal("""{"id":"x"}""", all[1].ItemJson);
        Assert.Equal(Now, all[1].SavedAt);
    }

    [Fact]
    public async Task Removing_and_deleting_all_touch_only_the_owner()
    {
        var maya = new TravellerId("uid-maya-" + Guid.NewGuid().ToString("N")[..6]);
        await Store.PutAsync(Item(SavedKind.Event, "tm:9", 0), CancellationToken.None);
        await Store.PutAsync(Item(SavedKind.Hotel, "bk:1", 1), CancellationToken.None);
        await Store.PutAsync(SavedItem.Create(maya, SavedKind.Hotel, "bk:1", "3169070", "Rome", "H", null, 1, 1, Now, null), CancellationToken.None);

        await Store.DeleteAsync(Owner, SavedKind.Event, "tm:9", CancellationToken.None);
        Assert.Null(await Store.GetAsync(Owner, SavedKind.Event, "tm:9", CancellationToken.None));

        await Store.DeleteAllAsync(Owner, CancellationToken.None);
        Assert.Equal(0, await Store.CountAsync(Owner, CancellationToken.None));
        Assert.Equal(1, await Store.CountAsync(maya, CancellationToken.None));
    }
}

public sealed class InMemorySavedStoreTests : SavedStoreContract
{
    protected override ISavedStore Store { get; } = new InMemorySavedStore();
}

public sealed class FirestoreSavedStoreTests(FirestoreEmulator emulator) : SavedStoreContract, IClassFixture<FirestoreEmulator>
{
    private ISavedStore? _store;

    protected override ISavedStore Store => _store ??= new FirestoreSavedStore(emulator.Db);
}
