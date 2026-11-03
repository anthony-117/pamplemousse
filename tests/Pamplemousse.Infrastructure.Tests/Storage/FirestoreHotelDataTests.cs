using Pamplemousse.Application.Abstractions;
using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Hotels;
using Pamplemousse.Infrastructure.Storage;

namespace Pamplemousse.Infrastructure.Tests.Storage;

public class FirestoreHotelDataTests(FirestoreEmulator emulator) : IClassFixture<FirestoreEmulator>
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 9, 0, 0, TimeSpan.Zero);

    private readonly FirestoreHotelData _data = new(emulator.Db);

    // Each test uses its own ids, so tests sharing the emulator never see each other's documents.
    private readonly string _run = Guid.NewGuid().ToString("N")[..8];

    private Hotel FullHotel() => new()
    {
        Id = $"bk:{_run}1",
        Name = "Hôtel Mirific",
        Location = new GeoPoint(48.88, 2.32),
        StayType = "Hotels",
        Address = "119 Avenue De Clichy, 75017 Paris",
        District = "17th arr.",
        CityUfi = -1456928,
        CountryCode = "FR",
        TimeZone = "Europe/Paris",
        DistanceToCentreKm = 4.3,
        IsCityCentre = false,
        Stars = 4,
        StarsEstimated = false,
        ReviewScore = 8.6,
        ReviewCount = 1520,
        ReviewWord = "Excellent",
        BreakfastScore = 8.1,
        WifiScore = 8.5,
        PhotoUrls = ["https://photo/1.jpg", "https://photo/2.jpg"],
        CheckInFrom = "15:00",
        CheckInUntil = "23:00",
        CheckOutFrom = "07:00",
        CheckOutUntil = "11:00",
        BreakfastIncluded = true,
        FamilyFriendly = false,
        FreeCancellation = true,
        Facilities = ["Free Wifi"],
        Languages = ["fr", "en-gb"],
        BookingPageUrl = "https://www.booking.com/hotel/fr/mirific.html",
        LastPrice = new HotelPrice(812.40m, 203.10m, "USD", new DateOnly(2027, 4, 10), new DateOnly(2027, 4, 14), 2, Now),
        RawSearch = """{"id":1,"name":"Mirific"}""",
        RawDetails = """{"hotel_id":1}""",
        SearchUpdatedAt = Now,
        DetailsUpdatedAt = Now.AddMinutes(5),
    };

    private PriceSnapshot Snapshot(int page, DateTimeOffset at, params (string Id, decimal Total)[] prices) => new(
        $"{_run}:20270410:20270414:2:USD", "3169070", new DateOnly(2027, 4, 10), new DateOnly(2027, 4, 14), 2, "USD", at, page,
        [.. prices.Select((p, i) => new SnapshotPrice(p.Id, p.Total, p.Total / 4, i + 1, i == 0 ? true : null))]);

    [Fact]
    public async Task A_hotel_reads_back_with_every_field()
    {
        var hotel = FullHotel();

        await _data.SaveManyAsync([hotel], CancellationToken.None);
        var read = await ((IHotelStore)_data).GetAsync(hotel.Id, CancellationToken.None);

        Assert.NotNull(read);
        Assert.Equal(hotel with { PhotoUrls = read.PhotoUrls, Facilities = read.Facilities, Languages = read.Languages }, read);
        Assert.Equal(hotel.PhotoUrls, read.PhotoUrls, StringComparer.Ordinal);
        Assert.Equal(hotel.Facilities, read.Facilities, StringComparer.Ordinal);
        Assert.Equal(hotel.Languages, read.Languages, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Empty_fields_stay_empty()
    {
        var bare = new Hotel { Id = $"bk:{_run}2", Name = "Bare", Location = new GeoPoint(1, 2) };

        await _data.SaveManyAsync([bare], CancellationToken.None);
        var read = await ((IHotelStore)_data).GetAsync(bare.Id, CancellationToken.None);

        Assert.Equal(bare with { PhotoUrls = read!.PhotoUrls, Facilities = read.Facilities, Languages = read.Languages }, read);
        Assert.Null(read.Stars);
        Assert.Null(read.LastPrice);
    }

    [Fact]
    public async Task Hotels_are_stored_under_the_agreed_document_id()
    {
        await _data.SaveManyAsync([FullHotel()], CancellationToken.None);

        var doc = await emulator.Db.Collection("hotels").Document($"bk_{_run}1").GetSnapshotAsync();

        Assert.True(doc.Exists);
        Assert.Equal($"{_run}1", doc.GetValue<string>("sourceId"));
    }

    [Fact]
    public async Task Many_hotels_are_read_at_once_and_unknown_ids_skipped()
    {
        var first = FullHotel();
        var second = FullHotel() with { Id = $"bk:{_run}3", Name = "Second" };
        await _data.SaveManyAsync([first, second], CancellationToken.None);

        var read = await _data.GetManyAsync([first.Id, $"bk:{_run}missing", second.Id], CancellationToken.None);

        Assert.Equal([first.Id, second.Id], read.Select(h => h.Id).Order(StringComparer.Ordinal), StringComparer.Ordinal);
    }

    [Fact]
    public async Task A_city_mapping_reads_back()
    {
        var city = new CityMapping($"{_run}city", "loc-rome", -126693, "Rome", Now);

        await _data.SaveAsync(city, CancellationToken.None);

        Assert.Equal(city, await _data.GetAsync(city.DestinationId, CancellationToken.None));
        Assert.Null(await _data.GetAsync($"{_run}none", CancellationToken.None));
    }

    [Fact]
    public async Task The_newest_snapshot_of_each_page_is_returned_page_first()
    {
        await _data.AddAsync(Snapshot(1, Now, ("bk:1", 500m), ("bk:2", 300m)), CancellationToken.None);
        await _data.AddAsync(Snapshot(1, Now.AddHours(13), ("bk:1", 520m)), CancellationToken.None);
        await _data.AddAsync(Snapshot(2, Now.AddHours(13).AddMinutes(1), ("bk:21", 700m)), CancellationToken.None);

        var latest = await _data.GetLatestAsync($"{_run}:20270410:20270414:2:USD", CancellationToken.None);

        Assert.Equal([1, 2], latest.Select(s => s.Page));
        Assert.Equal(Now.AddHours(13), latest[0].FetchedAt);
        Assert.Equal(Snapshot(1, Now.AddHours(13), ("bk:1", 520m)) with { Prices = latest[0].Prices }, latest[0]);
        Assert.Equal([new SnapshotPrice("bk:1", 520m, 130m, 1, true)], latest[0].Prices);
        Assert.Empty(await _data.GetLatestAsync($"{_run}:other", CancellationToken.None));
    }

    [Fact]
    public async Task Each_snapshot_also_adds_one_price_to_each_hotels_history()
    {
        var hotelId = $"bk:{_run}9";
        await _data.AddAsync(Snapshot(1, Now, (hotelId, 500m)), CancellationToken.None);
        await _data.AddAsync(Snapshot(1, Now.AddHours(13), (hotelId, 520m)), CancellationToken.None);

        var history = await emulator.Db.Collection("hotels").Document($"bk_{_run}9").Collection("prices").GetSnapshotAsync();

        Assert.Equal([500d, 520d], history.Documents.Select(d => d.GetValue<double>("total")));
        Assert.Equal("20261103T090000000Z", history.Documents[0].Id);
    }
}
