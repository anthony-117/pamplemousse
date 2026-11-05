using Pamplemousse.Domain.Cars;
using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Hotels;
using Pamplemousse.Infrastructure.Storage;

namespace Pamplemousse.Infrastructure.Tests.Storage;

public class FirestoreCarDataTests(FirestoreEmulator emulator) : IClassFixture<FirestoreEmulator>
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);

    private readonly FirestoreCarData _data = new(emulator.Db);

    // Each test uses its own ids, so tests sharing the emulator never see each other's documents.
    private readonly string _run = Guid.NewGuid().ToString("N")[..8];

    private static CarOffer Car(int rank, Money? deposit = null) => new(
        $"bk:{rank}",
        "Fiat 600",
        "or similar large car",
        "Intermediate",
        "https://photo/1.png",
        Gearbox.Automatic,
        5,
        4,
        rank == 1 ? 3 : null,
        true,
        "Unlimited km",
        true,
        "Like for like",
        new RentalCompany("Budget", "https://logo/1.png", 7.0, 372),
        new PickUpPoint("Rome - via Sardegna", PickUpType.Downtown, "38 A, Via Sardegna", new GeoPoint(41.9095, 12.491223), 0.9),
        new CarPrice(76.76m, 19.19m, "USD", true),
        true,
        deposit,
        new Money(3294, "EUR"),
        Sponsored: true,
        rank,
        "https://cars.booking.com/DriverExtras.do");

    private CarSnapshot Snapshot(DateTimeOffset at, params CarOffer[] cars) => new(
        $"{_run}:centre:20261204:20261208:30:USD", "3169070", "centre", new DateOnly(2026, 12, 4), new DateOnly(2026, 12, 8), 30, "USD", at, true, cars, [1, 2, 3]);

    [Fact]
    public async Task Airports_are_merged_into_the_city_and_keep_its_hotel_fields()
    {
        var city = $"{_run}city";
        await new FirestoreHotelData(emulator.Db).SaveAsync(new CityMapping(city, "loc-rome", -126693, "Rome", Now), CancellationToken.None);

        await _data.SaveAsync(
            new CarAirports(city, [PickUpPlace.Airport("FCO", "Fiumicino Airport", new GeoPoint(41.7927, 12.2523))], Now),
            CancellationToken.None);

        var airports = await _data.GetAsync(city, CancellationToken.None);
        var mapping = await new FirestoreHotelData(emulator.Db).GetAsync(city, CancellationToken.None);
        Assert.NotNull(airports);
        var fco = Assert.Single(airports.Airports);
        Assert.Equal(("FCO", "Fiumicino Airport", PickUpKind.Airport, 41.7927), (fco.Id, fco.Name, fco.Kind, fco.Location.Lat));
        Assert.Equal(Now, airports.CheckedAt);
        Assert.Equal("loc-rome", mapping?.LocationId);
    }

    [Fact]
    public async Task A_city_with_no_airport_look_up_yet_has_none()
    {
        var city = $"{_run}hotel-only";
        await new FirestoreHotelData(emulator.Db).SaveAsync(new CityMapping(city, "loc", null, "Rome", Now), CancellationToken.None);

        Assert.Null(await _data.GetAsync(city, CancellationToken.None));
        Assert.Null(await _data.GetAsync($"{_run}missing", CancellationToken.None));
    }

    [Fact]
    public async Task An_empty_look_up_is_still_remembered()
    {
        var city = $"{_run}none";

        await _data.SaveAsync(new CarAirports(city, [], Now), CancellationToken.None);

        var airports = await _data.GetAsync(city, CancellationToken.None);
        Assert.NotNull(airports);
        Assert.Empty(airports.Airports);
    }

    [Fact]
    public async Task A_snapshot_reads_back_with_every_car_and_the_raw_answer()
    {
        var saved = Snapshot(Now, Car(1, new Money(200, "EUR")), Car(2));

        await _data.AddAsync(saved, CancellationToken.None);

        var read = await _data.GetLatestAsync(saved.SearchKey, CancellationToken.None);
        Assert.NotNull(read);
        Assert.Equal(
            (saved.DestinationId, saved.PlaceId, saved.From, saved.To, saved.DriverAge, saved.Currency, saved.FetchedAt, saved.MayHaveMore),
            (read.DestinationId, read.PlaceId, read.From, read.To, read.DriverAge, read.Currency, read.FetchedAt, read.MayHaveMore));
        Assert.Equal(saved.Cars, read.Cars);
        Assert.Equal(saved.Raw, read.Raw);
    }

    [Fact]
    public async Task The_latest_snapshot_wins()
    {
        await _data.AddAsync(Snapshot(Now.AddDays(-1), Car(1)), CancellationToken.None);
        await _data.AddAsync(Snapshot(Now, Car(1), Car(2)), CancellationToken.None);

        var read = await _data.GetLatestAsync($"{_run}:centre:20261204:20261208:30:USD", CancellationToken.None);

        Assert.Equal((Now, 2), (read?.FetchedAt, read?.Cars.Count));
        Assert.Null(await _data.GetLatestAsync($"{_run}:other", CancellationToken.None));
    }
}
