using Pamplemousse.Application.Saved;
using Pamplemousse.Application.Tests.Destinations;
using Pamplemousse.Domain.Exceptions;
using Pamplemousse.Domain.Saved;
using Pamplemousse.Domain.Travellers;

namespace Pamplemousse.Application.Tests.Saved;

/// <summary>Spec 010 FR-020, FR-021: saved hotels, places and events.</summary>
public class SavedHandlerTests
{
    private static readonly TravellerId Fares = new("uid-fares");
    private static readonly TravellerId Maya = new("uid-maya");

    private readonly FakeSavedStore _store = new();
    private readonly Fakes _data = new();

    private sealed class Clock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now = _now.AddMinutes(1);
    }

    private readonly Clock _clock = new(new DateTimeOffset(2026, 11, 11, 10, 0, 0, TimeSpan.Zero));

    private Task<SavedItem> SaveAsync(string kind = "place", string id = "wd:Q10285", TravellerId? owner = null, string name = "Colosseum", string? item = """{"id":"wd:Q10285"}""") =>
        new SaveItemHandler(_store, _data, _clock).HandleAsync(
            new SaveItemCommand(owner ?? Fares, kind, id, "3169070", name, "https://img.test/c.jpg", 41.89, 12.49, item),
            CancellationToken.None);

    [Fact]
    public async Task An_item_is_saved_under_its_city_and_saving_again_keeps_its_first_time()
    {
        var first = await SaveAsync();
        var again = await SaveAsync();

        Assert.Equal(("Rome", first.SavedAt), (again.DestinationName, again.SavedAt));
        Assert.Equal(1, await _store.CountAsync(Fares, CancellationToken.None));
    }

    [Fact]
    public async Task The_list_is_newest_first_and_filters_by_kind_and_city_for_the_owner_only()
    {
        await SaveAsync("hotel", "bk:1", name: "Hotel Artemide");
        await SaveAsync("place", "wd:Q10285");
        await SaveAsync("event", "tm:9", name: "Jazz at the Baths");
        await SaveAsync("place", "wd:Q1", owner: Maya);

        var all = await new ListSavedHandler(_store).HandleAsync(new ListSavedQuery(Fares), CancellationToken.None);
        var places = await new ListSavedHandler(_store).HandleAsync(new ListSavedQuery(Fares, "place", "3169070"), CancellationToken.None);
        var ids = await new SavedIdsHandler(_store).HandleAsync(new SavedIdsQuery(Fares), CancellationToken.None);

        Assert.Equal<string>(["tm:9", "wd:Q10285", "bk:1"], [.. all.Items.Select(i => i.Id)]);
        Assert.Equal((3, SavedItem.MaxItems), (all.Count, all.Max));
        Assert.Equal("wd:Q10285", Assert.Single(places.Items).Id);
        Assert.Equal("bk:1", Assert.Single(ids.Hotel));
        Assert.Equal("wd:Q10285", Assert.Single(ids.Place));
        Assert.Equal("tm:9", Assert.Single(ids.Event));
    }

    [Fact]
    public async Task Removing_takes_it_out_and_removing_twice_is_fine()
    {
        await SaveAsync();
        var remove = new RemoveSavedHandler(_store);

        await remove.HandleAsync(new RemoveSavedCommand(Fares, "place", "wd:Q10285"), CancellationToken.None);
        await remove.HandleAsync(new RemoveSavedCommand(Fares, "place", "wd:Q10285"), CancellationToken.None);

        Assert.Equal(0, await _store.CountAsync(Fares, CancellationToken.None));
    }

    [Theory]
    [InlineData("flight", "kind")]
    [InlineData("car", "kind")]
    [InlineData("museum", "kind")]
    public async Task Only_hotels_places_and_events_are_saved(string kind, string field) =>
        Assert.Equal(field, (await Assert.ThrowsAsync<DomainException>(() => SaveAsync(kind))).Field);

    [Fact]
    public async Task Each_value_is_checked_naming_its_field()
    {
        Assert.Equal("name", (await Assert.ThrowsAsync<DomainException>(() => SaveAsync(name: " "))).Field);
        Assert.Equal("name", (await Assert.ThrowsAsync<DomainException>(() => SaveAsync(name: new string('x', 201)))).Field);
        Assert.Equal("item", (await Assert.ThrowsAsync<DomainException>(() => SaveAsync(item: "\"" + new string('x', 21 * 1024) + "\""))).Field);
        Assert.Equal("id", (await Assert.ThrowsAsync<DomainException>(() => SaveAsync(id: ""))).Field);
        var city = await Assert.ThrowsAsync<DomainException>(() => new SaveItemHandler(_store, _data, _clock).HandleAsync(
            new SaveItemCommand(Fares, "place", "x", "1", "X", null, 1, 1, null), CancellationToken.None));
        Assert.Equal("destinationId", city.Field);
    }

    [Fact]
    public async Task Item_201_is_refused()
    {
        for (var i = 0; i < SavedItem.MaxItems; i++)
        {
            await _store.PutAsync(SavedItem.Create(Fares, SavedKind.Place, "p" + i, "3169070", "Rome", "P", null, 1, 1, _clock.GetUtcNow(), null), CancellationToken.None);
        }

        await Assert.ThrowsAsync<SavedLimitException>(() => SaveAsync());
    }
}
