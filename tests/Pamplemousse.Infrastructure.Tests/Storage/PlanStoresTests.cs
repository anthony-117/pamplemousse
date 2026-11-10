using System.Text.Json;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Domain.Itinerary;
using Pamplemousse.Domain.Travellers;
using Pamplemousse.Infrastructure.Storage;
using static Pamplemousse.Domain.Tests.Itinerary.PlanFixtures;

namespace Pamplemousse.Infrastructure.Tests.Storage;

/// <summary>The same checks on both plan stores (spec 008, FR-012 to FR-015).</summary>
public abstract class PlanStoreContract
{
    private static readonly DateTimeOffset Created = new(2026, 11, 6, 9, 0, 0, TimeSpan.Zero);

    // Each test uses its own ids, so tests sharing the emulator never see each other's documents.
    protected TravellerId Owner { get; } = new("uid-" + Guid.NewGuid().ToString("N")[..10]);

    protected abstract IPlanStore Store { get; }

    /// <summary>A plan with every kind of copy: hotels, flights, cars with one chosen, an event, weather and a past trip date.</summary>
    protected Plan NewPlan(string id = "p1", DateTimeOffset? at = null)
    {
        var alternatives = new PlanAlternatives(
            [Hotel("bk:1", 0.9, 500), Hotel("bk:2", 0.7, 300)],
            [Flight("tp:1", 0.8), Flight("tp:2", 0.6, 150, minutes: null)],
            null,
            Created,
            Created,
            null,
            "Booking.com");
        var pools = Pools(
            events: [Event("tm:1", From.AddDays(1), "20:30", 45)],
            weather: [new Pamplemousse.Domain.Destinations.WeatherDay(From, 22, 12, 10, 1, "Sunny")]);
        return Plan.Create(id, Owner, Trip(), alternatives, pools, at ?? Created)
            .WithCars([Car("car:1"), Car("car:2", 0.5, 90)], Created, "Booking.com", "https://cars.example.test")
            .Swap(OfferKind.Car, "car:2", pools, at ?? Created);
    }

    /// <summary>Records compare their lists by reference, so plans are compared by their JSON.</summary>
    protected static void AssertSame(Plan expected, Plan? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
    }

    [Fact]
    public async Task A_plan_reads_back_as_saved_with_every_copy()
    {
        var plan = NewPlan();
        await Store.AddAsync(plan, CancellationToken.None);

        var read = await Store.GetAsync(Owner, plan.Id, CancellationToken.None);

        AssertSame(plan, read);
        Assert.Equal(("bk:1", "tp:1", "car:2"), (read!.HotelId, read.FlightId, read.CarId));
        Assert.Equal(90, read.Built.Total.Car);
    }

    [Fact]
    public async Task Another_traveller_never_finds_the_plan()
    {
        var plan = NewPlan();
        await Store.AddAsync(plan, CancellationToken.None);
        var maya = new TravellerId("uid-other-" + Guid.NewGuid().ToString("N")[..6]);

        Assert.Null(await Store.GetAsync(maya, plan.Id, CancellationToken.None));
        Assert.False(await Store.DeleteAsync(maya, plan.Id, CancellationToken.None));
        Assert.Equal(0, await Store.CountAsync(maya, CancellationToken.None));
        Assert.NotNull(await Store.GetAsync(Owner, plan.Id, CancellationToken.None));
    }

    [Fact]
    public async Task The_list_is_newest_change_first_with_the_summary_fields()
    {
        await Store.AddAsync(NewPlan("old", Created), CancellationToken.None);
        await Store.AddAsync(NewPlan("new", Created.AddHours(1)), CancellationToken.None);

        var list = await Store.ListAsync(Owner, CancellationToken.None);

        Assert.Equal("new,old", string.Join(",", list.Select(s => s.Id)));
        var first = list[0];
        Assert.Equal(("Rome, 5 days", PlanStatus.Draft, "Rome", From, From.AddDays(4)), (first.Name, first.Status, first.DestinationName, first.From, first.To));
        Assert.Equal((1080m, "USD"), (first.Total, first.Currency));
        Assert.NotNull(first.MatchPercent);
        Assert.Equal(2, await Store.CountAsync(Owner, CancellationToken.None));
    }

    [Fact]
    public async Task A_save_replaces_the_plan()
    {
        var plan = NewPlan();
        await Store.AddAsync(plan, CancellationToken.None);

        await Store.SaveAsync(plan.Rename("Rome with Maya", Created.AddHours(2)).WithStatus("planned", Created.AddHours(2)), CancellationToken.None);

        var read = await Store.GetAsync(Owner, plan.Id, CancellationToken.None);
        Assert.Equal(("Rome with Maya", PlanStatus.Planned), (read?.Name, read?.Status));
        Assert.Equal("Rome with Maya", (await Store.ListAsync(Owner, CancellationToken.None))[0].Name);
    }

    [Fact]
    public async Task Deleting_one_then_all()
    {
        await Store.AddAsync(NewPlan("a"), CancellationToken.None);
        await Store.AddAsync(NewPlan("b"), CancellationToken.None);

        Assert.True(await Store.DeleteAsync(Owner, "a", CancellationToken.None));
        Assert.False(await Store.DeleteAsync(Owner, "a", CancellationToken.None));
        await Store.DeleteAllAsync(Owner, CancellationToken.None);
        await Store.DeleteAllAsync(Owner, CancellationToken.None);

        Assert.Equal(0, await Store.CountAsync(Owner, CancellationToken.None));
        Assert.Empty(await Store.ListAsync(Owner, CancellationToken.None));
    }
}

public sealed class InMemoryPlanStoreTests : PlanStoreContract
{
    protected override IPlanStore Store { get; } = new InMemoryPlanStore();

    [Fact]
    public void A_plan_packs_small()
    {
        // Research R-8: a 5-day plan with its alternatives stays far under Firestore's 1 MB a document.
        Assert.InRange(PlanJson.Pack(NewPlan()).Length, 1, 100_000);
    }
}

public sealed class FirestorePlanStoreTests(FirestoreEmulator emulator) : PlanStoreContract, IClassFixture<FirestoreEmulator>
{
    private IPlanStore? _store;

    protected override IPlanStore Store => _store ??= new FirestorePlanStore(emulator.Db);

    [Fact]
    public async Task The_document_sits_under_the_traveller_with_list_fields_and_a_packed_body()
    {
        await Store.AddAsync(NewPlan(), CancellationToken.None);

        var doc = await emulator.Db.Collection("travellers").Document(Owner.Value).Collection("plans").Document("p1").GetSnapshotAsync();

        Assert.Equal(("Rome, 5 days", "draft", "2027-04-10"), (doc.GetValue<string>("name"), doc.GetValue<string>("status"), doc.GetValue<string>("from")));
        Assert.True(doc.ContainsField("body"));
    }
}
