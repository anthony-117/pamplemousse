using Microsoft.Extensions.Logging.Abstractions;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Application.Flights.Queries.SearchFlights;
using Pamplemousse.Application.Tests.Destinations;
using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Exceptions;
using Pamplemousse.Domain.Flights;

namespace Pamplemousse.Application.Tests.Flights;

public class SearchFlightsHandlerTests
{
    private static readonly DateOnly From = new(2027, 4, 10);
    private static readonly DateOnly To = new(2027, 4, 14);
    private static readonly FlightCity RomeCity = new("ROM", "Rome", "IT", new GeoPoint(41.89, 12.51));
    private static readonly FlightCity ParisCity = new("PAR", "Paris", "FR", new GeoPoint(48.85, 2.35));

    private sealed class FakeCities(Func<GeoPoint, CityMatch?> find) : IFlightCities
    {
        public CityMatch? FindNearest(GeoPoint location) => find(location);
    }

    private sealed class FakeProvider : IFlightProvider
    {
        public IReadOnlyList<FlightOffer> Offers { get; init; } = [];
        public Exception? Error { get; init; }
        public FlightSearch? Search { get; private set; }

        public Task<FlightResults> SearchAsync(FlightSearch search, CancellationToken ct)
        {
            Search = search;
            return Error is null
                ? Task.FromResult(new FlightResults(Offers, "https://book/search"))
                : Task.FromException<FlightResults>(Error);
        }
    }

    private static CityMatch? ToRomeOrParis(GeoPoint p) => new(p == Fakes.Rome.Location ? RomeCity : ParisCity, 1);

    private static FlightOffer Offer(decimal price, DateMatch match = DateMatch.Exact) => new(
        $"tp:{price}", "ITA", "AZ1", "CDG", "FCO", new DateTimeOffset(2027, 4, 10, 9, 0, 0, TimeSpan.Zero), null, 0, null, null, new Money(price, "USD"), "https://book/1", match, "test");

    private static SearchFlightsHandler Handler(IFlightProvider provider, Func<GeoPoint, CityMatch?>? cities = null) => new(
        new Fakes(), new FakeCities(cities ?? ToRomeOrParis), provider, new FixedClock(), NullLogger<SearchFlightsHandler>.Instance);

    // Fakes knows one city, so both ends resolve through this query only when the id is Rome's.
    private static SearchFlightsQuery Query(string? tripType = null, string? currency = null, int? limit = null, string destination = "3169070", string origin = "3169070") =>
        new(destination, origin, From, To, tripType, null, currency, limit);

    private static Func<GeoPoint, CityMatch?> TwoCities()
    {
        var calls = 0;
        return _ => new CityMatch(calls++ == 0 ? ParisCity : RomeCity, 1);
    }

    [Fact]
    public async Task Searches_with_the_flight_codes_of_both_cities()
    {
        var provider = new FakeProvider { Offers = [Offer(90)] };

        var result = await Handler(provider, TwoCities()).HandleAsync(Query(), CancellationToken.None);

        Assert.Equal(("PAR", "ROM"), (provider.Search?.OriginCode, provider.Search?.DestinationCode));
        Assert.Equal(["tp:90"], result.Items.Select(i => i.Id), StringComparer.Ordinal);
        Assert.Contains("Recent prices", result.PriceNote, StringComparison.Ordinal);
        Assert.Null(result.Message);
        Assert.Null(result.SearchUrl);
    }

    [Theory]
    [InlineData(DateMatch.Nearby, "within 3 days")]
    [InlineData(DateMatch.SameMonth, "same month")]
    [InlineData(DateMatch.AnyReturn, "return on other dates")]
    public async Task Fares_on_other_dates_are_explained(DateMatch match, string text)
    {
        var result = await Handler(new FakeProvider { Offers = [Offer(90, match)] }, TwoCities()).HandleAsync(Query(), CancellationToken.None);

        Assert.Contains(text, result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_fares_gives_a_message_and_a_search_link()
    {
        var result = await Handler(new FakeProvider(), TwoCities()).HandleAsync(Query(), CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.Contains("booking site", result.Message, StringComparison.Ordinal);
        Assert.Equal("https://book/search", result.SearchUrl);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(-5, 1)]
    [InlineData(999, 50)]
    public async Task The_limit_is_kept_in_range(int limit, int expected)
    {
        var offers = Enumerable.Range(1, 60).Select(i => Offer(i)).ToList();

        var result = await Handler(new FakeProvider { Offers = offers }, TwoCities()).HandleAsync(Query(limit: limit), CancellationToken.None);

        Assert.Equal(expected, result.Items.Count);
    }

    [Fact]
    public async Task The_default_limit_is_20()
    {
        var offers = Enumerable.Range(1, 30).Select(i => Offer(i)).ToList();

        var result = await Handler(new FakeProvider { Offers = offers }, TwoCities()).HandleAsync(Query(), CancellationToken.None);

        Assert.Equal(20, result.Items.Count);
    }

    [Fact]
    public async Task Unknown_destination_is_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => Handler(new FakeProvider()).HandleAsync(Query(destination: "nope"), CancellationToken.None));
    }

    [Fact]
    public async Task Unknown_origin_is_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => Handler(new FakeProvider()).HandleAsync(Query(origin: "nope"), CancellationToken.None));
    }

    [Fact]
    public async Task Bad_trip_type_is_rejected()
    {
        var error = await Assert.ThrowsAsync<DomainException>(() => Handler(new FakeProvider()).HandleAsync(Query(tripType: "multi"), CancellationToken.None));

        Assert.Equal("tripType", error.Field);
    }

    [Fact]
    public async Task Same_flight_city_is_rejected()
    {
        var error = await Assert.ThrowsAsync<DomainException>(() => Handler(new FakeProvider(), _ => new CityMatch(RomeCity, 1)).HandleAsync(Query(), CancellationToken.None));

        Assert.Equal("origin", error.Field);
    }

    [Fact]
    public async Task Invalid_dates_are_rejected()
    {
        var query = new SearchFlightsQuery("3169070", "3169070", To, From, null, null, null, null);

        var error = await Assert.ThrowsAsync<DomainException>(() => Handler(new FakeProvider(), TwoCities()).HandleAsync(query, CancellationToken.None));

        Assert.Equal("to", error.Field);
    }

    [Fact]
    public async Task A_city_with_no_airport_nearby_gets_a_message_and_no_search()
    {
        var provider = new FakeProvider();

        var result = await Handler(provider, _ => null).HandleAsync(Query(), CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.Contains("no airport nearby", result.Message, StringComparison.Ordinal);
        Assert.Null(provider.Search);
        Assert.Null(result.Destination.Code);
    }

    [Fact]
    public async Task A_different_place_is_reported_as_nearby()
    {
        var calls = 0;
        CityMatch? Find(GeoPoint _) => calls++ == 0 ? new CityMatch(ParisCity, 1) : new CityMatch(new FlightCity("PEG", "Perugia", "IT", default), 19);

        var result = await Handler(new FakeProvider(), Find).HandleAsync(Query(), CancellationToken.None);

        Assert.True(result.Destination.IsNearby);
        Assert.Equal(("Perugia", "PEG", 19d), (result.Destination.FlightCity, result.Destination.Code, result.Destination.DistanceKm));
        Assert.False(result.Origin.IsNearby);
    }

    [Fact]
    public async Task The_same_name_far_from_the_centre_is_not_nearby()
    {
        var calls = 0;
        CityMatch? Find(GeoPoint _) => calls++ == 0 ? new CityMatch(ParisCity, 1) : new CityMatch(new FlightCity("ROM", "ROME", "IT", default), 12);

        var result = await Handler(new FakeProvider(), Find).HandleAsync(Query(), CancellationToken.None);

        Assert.False(result.Destination.IsNearby);
    }

    [Fact]
    public async Task A_source_failure_is_a_warning()
    {
        var result = await Handler(new FakeProvider { Error = new HttpRequestException("down") }, TwoCities()).HandleAsync(Query(), CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.Equal(["Flights is unavailable right now."], result.Warnings, StringComparer.Ordinal);
        Assert.Null(result.Message);
        Assert.Null(result.SearchUrl);
    }

    [Fact]
    public async Task A_missing_token_warning_names_the_variable()
    {
        var error = new ProviderNotConfiguredException("Travelpayouts", "TRAVELPAYOUTS_API_TOKEN");

        var result = await Handler(new FakeProvider { Error = error }, TwoCities()).HandleAsync(Query(), CancellationToken.None);

        Assert.Contains("TRAVELPAYOUTS_API_TOKEN", result.Warnings[0], StringComparison.Ordinal);
    }
}
