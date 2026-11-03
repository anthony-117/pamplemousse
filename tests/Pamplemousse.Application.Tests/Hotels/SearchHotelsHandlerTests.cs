using Microsoft.Extensions.Logging.Abstractions;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Application.Hotels;
using Pamplemousse.Application.Hotels.Queries.SearchHotels;
using Pamplemousse.Application.Tests.Destinations;
using Pamplemousse.Domain.Exceptions;
using Pamplemousse.Domain.Hotels;

namespace Pamplemousse.Application.Tests.Hotels;

public class SearchHotelsHandlerTests
{
    private readonly FakeHotelProvider _provider = new();
    private readonly FakeHotelData _store = new();
    private readonly FakeSearchCache _cache = new();
    private readonly FakeSearchLock _lock = new();
    private readonly MovableClock _clock = new();

    private SearchHotelsHandler Handler(HotelSettings? settings = null) => new(
        new Fakes(), _provider, _store, _store, _store, _cache, _lock, settings ?? new HotelSettings(), _clock, NullLogger<SearchHotelsHandler>.Instance);

    private static SearchHotelsQuery Query(string? sort = null, int? minReview = null, int? page = null, int? adults = null, string destination = "3169070") =>
        new(destination, new DateOnly(2027, 4, 10), new DateOnly(2027, 4, 14), adults, null, sort, MinReviewScore: minReview, Page: page);

    private void TwoHotels() => _provider.Hotels.AddRange([HotelFakes.Found(1, 800, stars: 3, review: 7.5), HotelFakes.Found(2, 500, stars: 5, review: 9.0, km: 12)]);

    [Fact]
    public async Task Returns_hotels_with_their_total_and_per_night_price()
    {
        TwoHotels();

        var result = await Handler().HandleAsync(Query(), CancellationToken.None);

        Assert.Equal(["bk:1", "bk:2"], result.Items.Select(i => i.Hotel.Id), StringComparer.Ordinal);
        Assert.Equal((800m, 200m, "USD"), (result.Items[0].Price.Total, result.Items[0].Price.PerNight, result.Items[0].Price.Currency));
        Assert.Equal((4, 2, 2, false), (result.Nights, result.Adults, result.TotalItems, result.IsSavedPrice));
        Assert.Equal(HotelFakes.Now, result.PricesFetchedAt);
        Assert.Equal("Booking.com", result.Source);
        Assert.Null(result.Destination.SourceName); // the source's "Rome" is our Rome
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task Hotels_beyond_the_radius_are_marked_near_the_city()
    {
        TwoHotels();

        var result = await Handler().HandleAsync(Query(), CancellationToken.None);

        Assert.Equal([false, true], result.Items.Select(i => i.NearCity));
        Assert.InRange(result.Items[1].DistanceKm, 11.5, 12.5);
    }

    [Fact]
    public async Task Saves_the_city_the_hotels_and_one_snapshot_per_call()
    {
        TwoHotels();

        await Handler().HandleAsync(Query(), CancellationToken.None);

        Assert.Equal("loc-rome", _store.Cities["3169070"].LocationId);
        Assert.Equal(["bk:1", "bk:2"], _store.Hotels.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal);
        var snapshot = Assert.Single(_store.Snapshots);
        Assert.Equal(("3169070:20270410:20270414:2:USD", 1), (snapshot.SearchKey, snapshot.Page));
        Assert.Equal([800m, 500m], snapshot.Prices.Select(p => p.Total));
    }

    [Fact]
    public async Task An_identical_search_within_twelve_hours_makes_no_new_call()
    {
        TwoHotels();
        await Handler().HandleAsync(Query(), CancellationToken.None);
        _clock.Now = HotelFakes.Now.AddHours(11);

        var again = await Handler().HandleAsync(Query(), CancellationToken.None);

        Assert.Equal([1], _provider.SearchedPages);
        Assert.Equal(1, _provider.CityCalls);
        Assert.Equal(2, again.Items.Count);
        Assert.Equal(HotelFakes.Now, again.PricesFetchedAt);
    }

    [Fact]
    public async Task Sorting_and_filtering_use_the_stored_data_without_a_new_call()
    {
        TwoHotels();
        await Handler().HandleAsync(Query(), CancellationToken.None);

        var cheapest = await Handler().HandleAsync(Query(sort: "price"), CancellationToken.None);
        var wellRated = await Handler().HandleAsync(Query(minReview: 8), CancellationToken.None);

        Assert.Equal(["bk:2", "bk:1"], cheapest.Items.Select(i => i.Hotel.Id), StringComparer.Ordinal);
        Assert.Equal(["bk:2"], wellRated.Items.Select(i => i.Hotel.Id), StringComparer.Ordinal);
        Assert.Equal([1], _provider.SearchedPages);
    }

    [Fact]
    public async Task The_city_is_looked_up_once_and_then_read_from_storage()
    {
        TwoHotels();
        await Handler().HandleAsync(Query(), CancellationToken.None);

        await Handler().HandleAsync(Query(adults: 3), CancellationToken.None);

        Assert.Equal(1, _provider.CityCalls);
        Assert.Equal([1, 1], _provider.SearchedPages);
    }

    [Fact]
    public async Task A_later_page_fetches_only_the_missing_source_page()
    {
        _provider.Hotels.AddRange(Enumerable.Range(1, 25).Select(r => HotelFakes.Found(r, 100 + r)));
        _provider.TotalPages = 2;
        await Handler().HandleAsync(Query(), CancellationToken.None);

        var second = await Handler().HandleAsync(Query(page: 2), CancellationToken.None);

        Assert.Equal([1, 2], _provider.SearchedPages);
        Assert.Equal(Enumerable.Range(21, 5).Select(r => $"bk:{r}"), second.Items.Select(i => i.Hotel.Id), StringComparer.Ordinal);
        Assert.Equal((25, 2), (second.TotalItems, second.Page));
        Assert.Equal(2, _store.Snapshots.Count);
    }

    [Fact]
    public async Task A_page_past_the_last_makes_no_call()
    {
        TwoHotels();
        await Handler().HandleAsync(Query(), CancellationToken.None);

        var past = await Handler().HandleAsync(Query(page: 3), CancellationToken.None);

        Assert.Empty(past.Items);
        Assert.Equal([1], _provider.SearchedPages);
    }

    [Fact]
    public async Task When_the_source_fails_the_last_saved_prices_are_returned_with_their_time()
    {
        TwoHotels();
        await Handler().HandleAsync(Query(), CancellationToken.None);
        _cache.Values.Clear(); // 12 hours later
        _clock.Now = HotelFakes.Now.AddHours(13);
        _provider.Error = new HttpRequestException("down");

        var result = await Handler().HandleAsync(Query(), CancellationToken.None);

        Assert.True(result.IsSavedPrice);
        Assert.Equal(HotelFakes.Now, result.PricesFetchedAt);
        Assert.Equal(2, result.Items.Count);
        Assert.Contains(result.Warnings, w => w.Contains("could not be refreshed", StringComparison.Ordinal) && w.Contains("2026-11-03 09:00", StringComparison.Ordinal));
    }

    [Fact]
    public async Task With_no_saved_prices_a_failure_gives_an_empty_list_a_warning_and_a_link()
    {
        _provider.Error = new HttpRequestException("down");

        var result = await Handler().HandleAsync(Query(), CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.Equal("https://book/search", result.SearchUrl);
        Assert.NotNull(result.Message);
        Assert.Contains(result.Warnings, w => w.Contains("No saved prices", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_missing_key_is_a_warning_naming_the_variable()
    {
        _provider.Error = new ProviderNotConfiguredException("Hotels", "RAPIDAPI_KEY");

        var result = await Handler().HandleAsync(Query(), CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.Contains(result.Warnings, w => w.Contains("RAPIDAPI_KEY", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_used_up_limit_falls_back_to_saved_prices()
    {
        TwoHotels();
        await Handler().HandleAsync(Query(), CancellationToken.None);
        _cache.Values.Clear();
        _provider.Error = new QuotaExceededException("Booking COM API");

        var result = await Handler().HandleAsync(Query(), CancellationToken.None);

        Assert.True(result.IsSavedPrice);
        Assert.Contains(result.Warnings, w => w.Contains("request limit", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_storage_failure_is_a_warning_and_the_fresh_hotels_are_still_shown()
    {
        TwoHotels();
        _store.Error = new InvalidOperationException("Firestore down");

        var result = await Handler().HandleAsync(Query(), CancellationToken.None);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(["Saved hotel data is unavailable right now."], result.Warnings, StringComparer.Ordinal);
    }

    [Fact]
    public async Task No_hotels_gives_a_message_and_a_link_not_an_error()
    {
        var result = await Handler().HandleAsync(Query(), CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.Contains("No hotels are available", result.Message, StringComparison.Ordinal);
        Assert.Equal("https://book/search", result.SearchUrl);
    }

    [Fact]
    public async Task Filters_that_match_nothing_say_so()
    {
        TwoHotels();

        var result = await Handler().HandleAsync(Query(minReview: 10), CancellationToken.None);

        Assert.Contains("match these filters", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task While_another_request_fetches_the_same_search_this_one_waits_for_its_answer()
    {
        TwoHotels();
        await Handler().HandleAsync(Query(), CancellationToken.None);
        var cached = _cache.Values.Single();
        _cache.Values.Clear();
        _lock.Taken = true;

        var waiting = Handler().HandleAsync(Query(), CancellationToken.None);
        _cache.Values[cached.Key] = cached.Value; // the other request finishes
        var result = await waiting;

        Assert.Equal(2, result.Items.Count);
        Assert.Equal([1], _provider.SearchedPages);
    }

    [Fact]
    public async Task Details_for_every_hotel_is_a_setting()
    {
        TwoHotels();
        _provider.Details = new Hotel { Id = "", Name = "Hotel", Location = default, StayType = "Hotels", FreeCancellation = true, DetailsUpdatedAt = HotelFakes.Now };

        var result = await Handler(new HotelSettings { DetailsForEveryHotel = true }).HandleAsync(
            Query() with { FreeCancellation = true }, CancellationToken.None);

        Assert.Equal(2, _provider.DetailCalls);
        Assert.Equal((2, 2), (result.Items.Count, result.FiltersCheckedOn));
        Assert.True(_store.Snapshots[0].Prices[0].FreeCancellation);
    }

    [Fact]
    public async Task Filters_that_need_details_say_how_many_hotels_they_could_check()
    {
        TwoHotels();

        var result = await Handler().HandleAsync(Query() with { StayTypes = ["Hotels"] }, CancellationToken.None);

        Assert.Equal(0, result.FiltersCheckedOn);
        Assert.Equal(0, _provider.DetailCalls);
    }

    [Fact]
    public async Task An_unknown_destination_is_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => Handler().HandleAsync(Query(destination: "nowhere"), CancellationToken.None));
    }

    [Fact]
    public async Task Invalid_adults_are_rejected_before_any_call()
    {
        var error = await Assert.ThrowsAsync<DomainException>(() => Handler().HandleAsync(Query(adults: 9), CancellationToken.None));

        Assert.Equal("adults", error.Field);
        Assert.Empty(_provider.SearchedPages);
    }
}
