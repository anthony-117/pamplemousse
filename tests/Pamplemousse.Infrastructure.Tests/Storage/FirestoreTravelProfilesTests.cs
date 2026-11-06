using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Pamplemousse.Domain.Itinerary;
using Pamplemousse.Domain.Ranking;
using Pamplemousse.Domain.Travellers;
using Pamplemousse.Infrastructure.Accounts;
using Pamplemousse.Infrastructure.Caching;
using Pamplemousse.Infrastructure.Storage;

namespace Pamplemousse.Infrastructure.Tests.Storage;

public class FirestoreTravelProfilesTests(FirestoreEmulator emulator) : IClassFixture<FirestoreEmulator>
{
    private static readonly DateTimeOffset Created = new(2026, 11, 6, 9, 0, 0, TimeSpan.Zero);

    private readonly FirestoreTravelProfiles _store = new(emulator.Db);

    // Each test uses its own ids, so tests sharing the emulator never see each other's documents.
    private readonly TravellerId _id = new("uid-" + Guid.NewGuid().ToString("N")[..10]);

    private TravelProfile Profile() => TravelProfile.New(_id, "Fares Haddad", "fares@example.com", Created) with
    {
        Passport = "IT",
        HomeCurrency = "EUR",
        HomeCityId = "3169070",
        Preferences = new Weights(90, 80, 50, 20, 10, 0),
        Pace = Pace.Packed,
        UpdatedAt = Created.AddMinutes(5),
    };

    [Fact]
    public async Task A_profile_reads_back_as_saved()
    {
        await _store.SaveAsync(Profile(), CancellationToken.None);

        Assert.Equal(Profile(), await _store.GetAsync(_id, CancellationToken.None));
    }

    [Fact]
    public async Task No_home_city_reads_back_as_none()
    {
        await _store.SaveAsync(Profile() with { HomeCityId = null }, CancellationToken.None);

        Assert.Null((await _store.GetAsync(_id, CancellationToken.None))?.HomeCityId);
    }

    [Fact]
    public async Task An_unknown_traveller_has_no_profile() =>
        Assert.Null(await _store.GetAsync(_id, CancellationToken.None));

    [Fact]
    public async Task Deleting_removes_it_and_deleting_again_is_fine()
    {
        await _store.SaveAsync(Profile(), CancellationToken.None);

        await _store.DeleteAsync(_id, CancellationToken.None);
        await _store.DeleteAsync(_id, CancellationToken.None);

        Assert.Null(await _store.GetAsync(_id, CancellationToken.None));
    }

    [Fact]
    public async Task The_document_is_keyed_by_the_user_id_with_named_fields()
    {
        await _store.SaveAsync(Profile(), CancellationToken.None);

        var doc = await emulator.Db.Collection("travellers").Document(_id.Value).GetSnapshotAsync();

        Assert.Equal("EUR", doc.GetValue<string>("homeCurrency"));
        Assert.Equal(90, doc.GetValue<int>("preferences.budget"));
    }
}

public class InMemoryTravelProfilesTests
{
    [Fact]
    public async Task Saves_reads_and_deletes()
    {
        var store = new InMemoryTravelProfiles();
        var id = new TravellerId("uid-1");
        var profile = TravelProfile.New(id, "Maya", null, DateTimeOffset.UnixEpoch);

        await store.SaveAsync(profile, CancellationToken.None);
        var read = await store.GetAsync(id, CancellationToken.None);
        await store.DeleteAsync(id, CancellationToken.None);

        Assert.Equal(profile, read);
        Assert.Null(await store.GetAsync(id, CancellationToken.None));
    }
}

public class DeletedTravellersTests
{
    [Fact]
    public async Task Remembers_a_deleted_traveller_for_an_hour_under_a_hashed_key()
    {
        var memory = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var deleted = new DeletedTravellers(new DistributedSearchCache(memory));
        var fares = new TravellerId("uid-fares");

        await deleted.AddAsync(fares, CancellationToken.None);

        Assert.True(await deleted.ContainsAsync(fares, CancellationToken.None));
        Assert.False(await deleted.ContainsAsync(new TravellerId("uid-maya"), CancellationToken.None));
        Assert.Null(await memory.GetAsync("deleted-traveller:uid-fares"));
        Assert.Equal(TimeSpan.FromHours(1), DeletedTravellers.Remembered);
    }
}
