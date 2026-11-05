using Microsoft.Extensions.Logging.Abstractions;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Application.Cars;
using Pamplemousse.Application.Cars.Queries.GetCarPlaces;
using Pamplemousse.Application.Cars.Queries.SearchCars;
using Pamplemousse.Application.Tests.Destinations;
using Pamplemousse.Application.Tests.Hotels;
using Pamplemousse.Domain.Cars;
using Pamplemousse.Domain.Exceptions;

namespace Pamplemousse.Application.Tests.Cars;

public class SearchCarsHandlerTests
{
    private readonly FakeCarProvider _provider = new();
    private readonly FakeCarData _store = new();
    private readonly FakeSearchCache _cache = new();
    private readonly FakeSearchLock _lock = new();
    private readonly MovableClock _clock = new();

    private SearchCarsHandler Handler() => new(
        new Fakes(), _provider, _store, _store, _cache, _lock, new CarSettings(), _clock, NullLogger<SearchCarsHandler>.Instance);

    private GetCarPlacesHandler Places() => new(
        new Fakes(), _provider, _store, _store, new CarSettings(), _clock, NullLogger<GetCarPlacesHandler>.Instance);

    private static SearchCarsQuery Query(string? place = null, int? age = null, string? sort = null, int? page = null, string destination = "3169070") =>
        new(destination, new DateOnly(2027, 4, 10), new DateOnly(2027, 4, 14), place, age, null, sort, Page: page);

    private void TwoCars() => _provider.Cars.AddRange([CarFakes.Car(1, 300, rating: 7.0), CarFakes.Car(2, 200, model: "Fiat Panda", rating: 9.0, km: 5)]);

    [Fact]
    public async Task Returns_cars_with_their_total_and_per_day_price()
    {
        TwoCars();

        var result = await Handler().HandleAsync(Query(), CancellationToken.None);

        Assert.Equal(["bk:1", "bk:2"], result.Items.Select(i => i.Id), StringComparer.Ordinal);
        Assert.Equal((300m, 75m, "USD"), (result.Items[0].Price.Total, result.Items[0].Price.PerDay, result.Items[0].Price.Currency));
        Assert.Equal((4, 30, "USD", 2, false), (result.Days, result.DriverAge, result.Currency, result.TotalItems, result.IsSavedPrice));
        Assert.Equal((PickUpPlace.CentreId, "Booking.com"), (result.Place.Id, result.Source));
        Assert.Equal(HotelFakes.Now, result.PricesFetchedAt);
        Assert.Equal("https://cars/search?place=centre&age=30", result.BookingUrl);
        Assert.Null(result.Message);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task Pick_up_distance_is_from_the_destination_centre()
    {
        TwoCars();

        var result = await Handler().HandleAsync(Query(), CancellationToken.None);

        Assert.InRange(result.Items[1].PickUp.DistanceKm, 4.5, 5.5);
    }

    [Fact]
    public async Task Keeps_the_cheapest_of_the_same_car_at_the_same_desk()
    {
        _provider.Cars.AddRange([CarFakes.Car(1, 300), CarFakes.Car(2, 250), CarFakes.Car(3, 400, company: "Hertz")]);

        var result = await Handler().HandleAsync(Query(), CancellationToken.None);

        Assert.Equal(["bk:2", "bk:3"], result.Items.Select(i => i.Id), StringComparer.Ordinal);
    }

    [Fact]
    public async Task Saves_one_snapshot_with_the_raw_answer()
    {
        TwoCars();
        _provider.MayHaveMore = true;

        var result = await Handler().HandleAsync(Query(), CancellationToken.None);

        var snapshot = Assert.Single(_store.Snapshots);
        Assert.Equal(("3169070:centre:20270410:20270414:30:USD", 2, true), (snapshot.SearchKey, snapshot.Cars.Count, snapshot.MayHaveMore));
        Assert.Equal([42], snapshot.Raw);
        Assert.True(result.MayHaveMore);
    }

    [Fact]
    public async Task Sorting_filtering_and_paging_reuse_the_cached_search()
    {
        TwoCars();
        await Handler().HandleAsync(Query(), CancellationToken.None);

        var cheapest = await Handler().HandleAsync(Query(sort: "price"), CancellationToken.None);
        var best = await Handler().HandleAsync(Query(sort: "rating"), CancellationToken.None);

        Assert.Equal(["bk:2", "bk:1"], cheapest.Items.Select(i => i.Id), StringComparer.Ordinal);
        Assert.Equal(["bk:2", "bk:1"], best.Items.Select(i => i.Id), StringComparer.Ordinal);
        Assert.Single(_provider.Searches);
        Assert.Single(_store.Snapshots);
    }

    [Fact]
    public async Task The_cache_lasts_one_day()
    {
        TwoCars();
        await Handler().HandleAsync(Query(), CancellationToken.None);

        Assert.Contains("cars:3169070:centre:20270410:20270414:30:USD", _cache.Values.Keys, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Another_driver_age_is_another_search()
    {
        TwoCars();
        await Handler().HandleAsync(Query(), CancellationToken.None);

        var young = await Handler().HandleAsync(Query(age: 22), CancellationToken.None);

        Assert.Equal(2, _provider.Searches.Count);
        Assert.Equal(22, _provider.Searches[1].DriverAge);
        Assert.Contains("young driver", young.DriverNote, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_source_failure_falls_back_to_the_last_snapshot()
    {
        TwoCars();
        await Handler().HandleAsync(Query(), CancellationToken.None);
        _cache.Values.Clear();
        _provider.Error = new HttpRequestException("down");
        _clock.Now = HotelFakes.Now.AddDays(2);

        var result = await Handler().HandleAsync(Query(), CancellationToken.None);

        Assert.True(result.IsSavedPrice);
        Assert.Equal(HotelFakes.Now, result.PricesFetchedAt);
        Assert.Equal(2, result.Items.Count);
        Assert.Contains(result.Warnings, w => w.Contains("Showing cars from 2026-11-03 09:00 UTC", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_timeout_with_nothing_saved_gives_no_cars_and_a_warning()
    {
        _provider.Error = new TaskCanceledException("slow");

        var result = await Handler().HandleAsync(Query(), CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.NotNull(result.Message);
        Assert.Equal(result.BookingUrl, result.SearchUrl);
        Assert.Contains(result.Warnings, w => w.Contains("No saved cars", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_missing_key_or_used_quota_is_a_warning()
    {
        _provider.Error = new ProviderNotConfiguredException("Cars", "RAPIDAPI_KEY");
        var noKey = await Handler().HandleAsync(Query(), CancellationToken.None);
        _provider.Error = new QuotaExceededException("Booking COM API");
        var noQuota = await Handler().HandleAsync(Query(), CancellationToken.None);

        Assert.Contains(noKey.Warnings, w => w.Contains("RAPIDAPI_KEY", StringComparison.Ordinal));
        Assert.Contains(noQuota.Warnings, w => w.Contains("limit", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_storage_failure_still_returns_cars_with_a_warning()
    {
        TwoCars();
        _store.Error = new InvalidOperationException("firestore down");

        var result = await Handler().HandleAsync(Query(), CancellationToken.None);

        Assert.Equal(2, result.Items.Count);
        Assert.Contains("Saved car data is unavailable right now.", result.Warnings, StringComparer.Ordinal);
    }

    [Fact]
    public async Task When_another_request_is_searching_it_waits_for_its_answer()
    {
        TwoCars();
        await Handler().HandleAsync(Query(), CancellationToken.None);
        _lock.Taken = true;

        var result = await Handler().HandleAsync(Query(), CancellationToken.None);

        Assert.Equal(2, result.Items.Count);
        Assert.Single(_provider.Searches);
    }

    [Fact]
    public async Task An_airport_pick_up_searches_from_that_airport()
    {
        TwoCars();
        _provider.Airports.Add(CarFakes.Fiumicino);

        var result = await Handler().HandleAsync(Query(place: "fco"), CancellationToken.None);

        Assert.Equal(("FCO", PickUpKind.Airport), (result.Place.Id, result.Place.Kind));
        Assert.Equal(CarFakes.Fiumicino.Location, _provider.Searches[0].Place.Location);
    }

    [Fact]
    public async Task An_unknown_place_is_rejected_on_place()
    {
        _provider.Airports.Add(CarFakes.Fiumicino);

        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().HandleAsync(Query(place: "JFK"), CancellationToken.None));

        Assert.Equal("place", ex.Field);
        Assert.Contains("FCO", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_destination_is_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => Handler().HandleAsync(Query(destination: "0"), CancellationToken.None));
    }

    [Fact]
    public async Task Places_are_the_centre_then_airports_within_50_km_nearest_first()
    {
        _provider.Airports.AddRange([CarFakes.Moncton, CarFakes.Fiumicino, CarFakes.Ciampino]);

        var result = await Places().HandleAsync(new GetCarPlacesQuery("3169070"), CancellationToken.None);

        Assert.Equal(["centre", "CIA", "FCO"], result.Places.Select(p => p.Id), StringComparer.Ordinal);
        Assert.Equal(["CIA", "FCO"], _store.Airports["3169070"].Airports.Select(a => a.Id), StringComparer.Ordinal);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task Stored_airports_are_reused_for_30_days()
    {
        _provider.Airports.Add(CarFakes.Fiumicino);
        await Places().HandleAsync(new GetCarPlacesQuery("3169070"), CancellationToken.None);
        _clock.Now = HotelFakes.Now.AddDays(29);
        await Places().HandleAsync(new GetCarPlacesQuery("3169070"), CancellationToken.None);
        Assert.Equal(1, _provider.AirportCalls);

        _clock.Now = HotelFakes.Now.AddDays(31);
        await Places().HandleAsync(new GetCarPlacesQuery("3169070"), CancellationToken.None);
        Assert.Equal(2, _provider.AirportCalls);
    }

    [Fact]
    public async Task A_failed_look_up_uses_older_airports_or_only_the_centre()
    {
        _provider.AirportError = new HttpRequestException("down");

        var none = await Places().HandleAsync(new GetCarPlacesQuery("3169070"), CancellationToken.None);

        Assert.Equal(["centre"], none.Places.Select(p => p.Id), StringComparer.Ordinal);
        Assert.Contains(none.Warnings, w => w.Contains("Only the city centre", StringComparison.Ordinal));

        _store.Airports["3169070"] = new CarAirports("3169070", [CarFakes.Fiumicino], HotelFakes.Now.AddDays(-90));
        var old = await Places().HandleAsync(new GetCarPlacesQuery("3169070"), CancellationToken.None);

        Assert.Equal(["centre", "FCO"], old.Places.Select(p => p.Id), StringComparer.Ordinal);
        Assert.Empty(old.Warnings);
    }
}
