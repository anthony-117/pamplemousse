using System.Net;
using Microsoft.Extensions.Options;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Exceptions;
using Pamplemousse.Domain.Flights;
using Pamplemousse.Infrastructure.Caching;
using Pamplemousse.Infrastructure.Providers;
using Pamplemousse.Infrastructure.ReferenceData;

namespace Pamplemousse.Infrastructure.Tests.Providers;

public class TravelpayoutsFlightProviderTests
{
    private static readonly FlightCityStore Cities = new();
    private static readonly FixedClock Clock = new(new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero));
    private static readonly DateOnly Today = new(2026, 11, 2);

    private const string Empty = """{"data":[],"success":true,"currency":"usd"}""";

    // Shaped like a real answer: Paris–Rome, December 2026.
    private static string Fare(string dep, string? ret, int price = 56, string airline = "FR", int transfers = 0, int returnTransfers = 0) =>
        "{\"flight_number\":\"166\",\"link\":\"/search/PAR1612ROM20121?t=abc&expected_price=56\",\"origin_airport\":\"BVA\",\"destination_airport\":\"FCO\","
        + $"\"departure_at\":\"{dep}T06:20:00+01:00\",\"airline\":\"{airline}\",\"destination\":\"ROM\","
        + (ret is null ? "" : $"\"return_at\":\"{ret}T19:40:00+01:00\",\"return_transfers\":{returnTransfers},")
        + $"\"origin\":\"PAR\",\"price\":{price},\"gate\":\"x\",\"duration\":260,\"transfers\":{transfers}}}";

    private static string Answer(params string[] fares) => "{\"data\":[" + string.Join(',', fares) + "],\"success\":true,\"currency\":\"usd\"}";

    private static FlightSearch Search(string from = "2026-12-16", string to = "2026-12-20", TripType type = TripType.RoundTrip, bool direct = false, string currency = "USD") =>
        FlightSearch.Create("PAR", "ROM", DateRange.ForTrip(DateOnly.Parse(from, System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(to, System.Globalization.CultureInfo.InvariantCulture), Today), type, direct, currency);

    private static TravelpayoutsFlightProvider Provider(StubHttp http, string token = "secret-token", string marker = "796702", JsonCache? cache = null) => new(
        http.Client(),
        cache ?? TestDoubles.Cache(),
        Options.Create(new ExternalApiOptions { TravelpayoutsApiToken = token, TravelpayoutsMarker = marker }),
        Cities,
        Clock);

    [Fact]
    public async Task Maps_the_real_fields()
    {
        var http = new StubHttp().On("prices_for_dates", Answer(Fare("2026-12-16", "2026-12-20")));

        var offer = Assert.Single((await Provider(http).SearchAsync(Search(), CancellationToken.None)).Offers);

        Assert.Equal("Ryanair", offer.Airline);
        Assert.Equal("FR166", offer.FlightNumber);
        Assert.Equal(("BVA", "FCO"), (offer.OriginAirport, offer.DestinationAirport));
        Assert.Equal(new DateTimeOffset(2026, 12, 16, 6, 20, 0, TimeSpan.FromHours(1)), offer.DepartureAt);
        Assert.Equal(new DateTimeOffset(2026, 12, 20, 19, 40, 0, TimeSpan.FromHours(1)), offer.ReturnAt);
        Assert.Equal((0, 0, 260), (offer.Stops, offer.ReturnStops, offer.DurationMinutes));
        Assert.Equal(new Money(56m, "USD"), offer.Price);
        Assert.Equal(DateMatch.Exact, offer.DateMatch);
        Assert.Equal("Travelpayouts", offer.Source);
        Assert.StartsWith("tp:", offer.Id, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sends_the_currency_and_keeps_the_token_out_of_the_url()
    {
        var http = new StubHttp().On("prices_for_dates", Answer(Fare("2026-12-16", "2026-12-20")));

        await Provider(http).SearchAsync(Search(currency: "EUR"), CancellationToken.None);

        var query = http.Requests[0].Query;
        Assert.Contains("currency=EUR", query, StringComparison.Ordinal);
        Assert.Contains("origin=PAR&destination=ROM&departure_at=2026-12-16&return_at=2026-12-20", query, StringComparison.Ordinal);
        Assert.Contains("one_way=false", query, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-token", http.Requests[0].ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Leaves_out_bad_prices_and_broken_dates_and_shows_duplicates_once_cheapest_first()
    {
        var http = new StubHttp().On("prices_for_dates", Answer(
            Fare("2026-12-16", "2026-12-20", price: 300, airline: "AF"),
            Fare("2026-12-16", "2026-12-20", price: 0, airline: "XX"),
            Fare("2026-12-16", "2026-12-20", price: -5, airline: "YY"),
            "{\"airline\":\"NO\",\"departure_at\":\"2026-12-16T06:20:00+01:00\"}",
            "{\"airline\":\"ZZ\",\"price\":10,\"departure_at\":\"soon\"}",
            Fare("2026-12-16", "2026-12-20", price: 56),
            Fare("2026-12-16", "2026-12-20", price: 56)));

        var offers = (await Provider(http).SearchAsync(Search(), CancellationToken.None)).Offers;

        Assert.Equal(["Ryanair", "Air France"], offers.Select(o => o.Airline), StringComparer.Ordinal);
    }

    [Fact]
    public async Task Exact_dates_need_only_one_call()
    {
        var http = new StubHttp().On("prices_for_dates", Answer(Fare("2026-12-16", "2026-12-20")));

        await Provider(http).SearchAsync(Search(), CancellationToken.None);

        Assert.Single(http.Requests);
    }

    [Fact]
    public async Task With_no_exact_fare_it_uses_dates_within_3_days_on_both_legs()
    {
        var http = new StubHttp()
            .On("departure_at=2026-12-16", Empty)
            .On("departure_at=2026-12", Answer(
                Fare("2026-12-18", "2026-12-22", price: 80),   // within 3 days on both
                Fare("2026-12-19", "2026-12-23", price: 70),   // exactly 3 days on both
                Fare("2026-12-18", "2026-12-28", price: 40),   // return too far
                Fare("2026-12-05", "2026-12-20", price: 30))); // departure too far

        var offers = (await Provider(http).SearchAsync(Search(), CancellationToken.None)).Offers;

        Assert.Equal([70m, 80m], offers.Select(o => o.Price.Amount));
        Assert.All(offers, o => Assert.Equal(DateMatch.Nearby, o.DateMatch));
    }

    [Fact]
    public async Task With_nothing_nearby_it_uses_the_same_month()
    {
        var http = new StubHttp()
            .On("departure_at=2026-12-16", Empty)
            .On("departure_at=2026-12", Answer(Fare("2026-12-05", "2026-12-09", price: 90)));

        var offer = Assert.Single((await Provider(http).SearchAsync(Search(), CancellationToken.None)).Offers);

        Assert.Equal(DateMatch.SameMonth, offer.DateMatch);
    }

    [Fact]
    public async Task With_nothing_in_the_month_a_round_trip_accepts_any_return_date()
    {
        var http = new StubHttp()
            .On("departure_at=2026-12&one_way=false", Answer(
                Fare("2026-12-09", "2027-01-08", price: 214),   // returns next month
                Fare("2027-01-09", "2027-01-20", price: 90),    // departs in another month
                Fare("2026-11-01", "2027-01-20", price: 80)))   // in the past
            .On("prices_for_dates", Empty);

        var offer = Assert.Single((await Provider(http).SearchAsync(Search(), CancellationToken.None)).Offers);

        Assert.Equal(DateMatch.AnyReturn, offer.DateMatch);
        Assert.Equal(214m, offer.Price.Amount);
        Assert.DoesNotContain("return_at", http.Requests[^1].Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Any_return_date_is_only_asked_for_when_earlier_levels_are_empty()
    {
        var http = new StubHttp()
            .On("departure_at=2026-12-16", Empty)
            .On("departure_at=2026-12", Answer(Fare("2026-12-05", "2026-12-09", price: 90)));

        await Provider(http).SearchAsync(Search(), CancellationToken.None);

        Assert.All(http.Requests, r => Assert.Contains("return_at", r.Query, StringComparison.Ordinal));
    }

    [Fact]
    public async Task One_way_has_no_any_return_level()
    {
        var http = new StubHttp().On("prices_for_dates", Empty);

        var results = await Provider(http).SearchAsync(Search(type: TripType.OneWay), CancellationToken.None);

        Assert.Empty(results.Offers);
        Assert.Equal(2, http.Requests.Count); // exact, then the month
    }

    [Fact]
    public async Task With_nothing_at_all_the_list_is_empty_and_a_search_link_is_given()
    {
        var http = new StubHttp().On("prices_for_dates", Empty);

        var results = await Provider(http).SearchAsync(Search(), CancellationToken.None);

        Assert.Empty(results.Offers);
        Assert.Equal("https://www.aviasales.com/search/PAR1612ROM20121?marker=796702", results.SearchUrl);
    }

    [Fact]
    public async Task A_range_near_the_edge_of_a_month_also_asks_the_next_month()
    {
        var http = new StubHttp()
            .On("departure_at=2026-12-30", Empty)
            .On("departure_at=2026-12", Answer(Fare("2026-12-29", "2027-01-03")))
            .On("departure_at=2027-01", Answer(Fare("2027-01-02", "2027-01-05", price: 40)));

        var offers = (await Provider(http).SearchAsync(Search("2026-12-30", "2027-01-03"), CancellationToken.None)).Offers;

        Assert.Equal([40m, 56m], offers.Select(o => o.Price.Amount));
        Assert.Contains(http.Requests, r => r.Query.Contains("departure_at=2027-01&", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Fares_in_the_past_are_never_shown()
    {
        var http = new StubHttp()
            .On("departure_at=2026-11-03", Empty)
            .On("departure_at=2026-11", Answer(Fare("2026-11-01", "2026-11-05", price: 10), Fare("2026-11-04", "2026-11-06", price: 60)))
            .On("departure_at=2026-10", Answer(Fare("2026-10-31", "2026-11-05", price: 5)));

        var offers = (await Provider(http).SearchAsync(Search("2026-11-03", "2026-11-05"), CancellationToken.None)).Offers;

        Assert.Equal([60m], offers.Select(o => o.Price.Amount));
    }

    [Fact]
    public async Task One_way_sends_no_return_date_and_has_no_return()
    {
        var http = new StubHttp().On("prices_for_dates", Answer(Fare("2026-12-16", ret: null, price: 34)));

        var results = await Provider(http).SearchAsync(Search(type: TripType.OneWay), CancellationToken.None);

        var offer = Assert.Single(results.Offers);
        Assert.Null(offer.ReturnAt);
        Assert.Null(offer.ReturnStops);
        Assert.Contains("one_way=true", http.Requests[0].Query, StringComparison.Ordinal);
        Assert.DoesNotContain("return_at", http.Requests[0].Query, StringComparison.Ordinal);
        Assert.StartsWith("https://www.aviasales.com/search/PAR1612ROM1?", results.SearchUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Repeating_a_search_uses_the_cache()
    {
        var http = new StubHttp().On("prices_for_dates", Answer(Fare("2026-12-16", "2026-12-20")));
        var provider = Provider(http, cache: TestDoubles.Cache());

        var first = await provider.SearchAsync(Search(), CancellationToken.None);
        var second = await provider.SearchAsync(Search(), CancellationToken.None);

        Assert.Single(http.Requests);
        Assert.Equal(first.Offers.Select(o => o.Id), second.Offers.Select(o => o.Id), StringComparer.Ordinal);
    }

    [Fact]
    public async Task A_different_search_is_not_served_from_the_cache()
    {
        var http = new StubHttp().On("prices_for_dates", Answer(Fare("2026-12-16", "2026-12-20")));
        var provider = Provider(http, cache: TestDoubles.Cache());

        await provider.SearchAsync(Search(), CancellationToken.None);
        await provider.SearchAsync(Search(currency: "EUR"), CancellationToken.None);
        await provider.SearchAsync(Search(direct: true), CancellationToken.None);

        Assert.Equal(3, http.Requests.Count);
    }

    [Fact]
    public async Task Missing_token_is_reported_without_a_call()
    {
        var http = new StubHttp();

        var error = await Assert.ThrowsAsync<ProviderNotConfiguredException>(() => Provider(http, token: "").SearchAsync(Search(), CancellationToken.None));

        Assert.Equal("TRAVELPAYOUTS_API_TOKEN", error.EnvironmentVariable);
        Assert.Empty(http.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task Source_errors_are_raised_for_the_handler_to_turn_into_a_warning(HttpStatusCode status)
    {
        var http = new StubHttp().On("prices_for_dates", "nope", status);

        await Assert.ThrowsAsync<HttpRequestException>(() => Provider(http).SearchAsync(Search(), CancellationToken.None));
    }

    [Fact]
    public async Task A_failure_is_not_cached()
    {
        var cache = TestDoubles.Cache();
        var failing = new StubHttp().On("prices_for_dates", "nope", HttpStatusCode.InternalServerError);
        await Assert.ThrowsAsync<HttpRequestException>(() => Provider(failing, cache: cache).SearchAsync(Search(), CancellationToken.None));

        var working = new StubHttp().On("prices_for_dates", Answer(Fare("2026-12-16", "2026-12-20")));

        Assert.Single((await Provider(working, cache: cache).SearchAsync(Search(), CancellationToken.None)).Offers);
    }

    [Fact]
    public async Task A_broken_answer_is_raised()
    {
        var http = new StubHttp().On("prices_for_dates", "<html>");

        await Assert.ThrowsAnyAsync<System.Text.Json.JsonException>(() => Provider(http).SearchAsync(Search(), CancellationToken.None));
    }

    [Fact]
    public async Task A_currency_the_source_rejects_is_bad_input_on_its_field()
    {
        var http = new StubHttp().On("prices_for_dates", """{"error":"bad request: unknown currency code xyz","data":null,"status":400}""", HttpStatusCode.BadRequest);

        var error = await Assert.ThrowsAsync<DomainException>(() => Provider(http).SearchAsync(Search(currency: "XYZ"), CancellationToken.None));

        Assert.Equal("currency", error.Field);
    }

    [Fact]
    public async Task Other_bad_requests_are_source_errors()
    {
        var http = new StubHttp().On("prices_for_dates", """{"error":"bad origin"}""", HttpStatusCode.BadRequest);

        await Assert.ThrowsAsync<HttpRequestException>(() => Provider(http).SearchAsync(Search(), CancellationToken.None));
    }

    [Fact]
    public async Task Booking_links_go_to_the_site_and_carry_the_marker()
    {
        var http = new StubHttp().On("prices_for_dates", Answer(Fare("2026-12-16", "2026-12-20")));

        var results = await Provider(http).SearchAsync(Search(), CancellationToken.None);

        Assert.Equal("https://www.aviasales.com/search/PAR1612ROM20121?t=abc&expected_price=56&marker=796702", results.Offers[0].BookingUrl);
        Assert.Empty(results.Warnings);
    }

    [Fact]
    public async Task Without_a_marker_links_still_work_and_a_warning_says_so()
    {
        var http = new StubHttp().On("prices_for_dates", Answer(Fare("2026-12-16", "2026-12-20")));

        var results = await Provider(http, marker: "").SearchAsync(Search(), CancellationToken.None);

        Assert.Equal("https://www.aviasales.com/search/PAR1612ROM20121?t=abc&expected_price=56", results.Offers[0].BookingUrl);
        Assert.Equal("https://www.aviasales.com/search/PAR1612ROM20121", results.SearchUrl);
        Assert.Contains(results.Warnings, w => w.Contains("TRAVELPAYOUTS_MARKER", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_fare_without_a_link_uses_the_search_page()
    {
        var http = new StubHttp().On("prices_for_dates", Answer("{\"airline\":\"AF\",\"price\":90,\"departure_at\":\"2026-12-16T06:20:00+01:00\",\"return_at\":\"2026-12-20T19:40:00+01:00\"}"));

        var offer = Assert.Single((await Provider(http).SearchAsync(Search(), CancellationToken.None)).Offers);

        Assert.Equal("https://www.aviasales.com/search/PAR1612ROM20121?marker=796702", offer.BookingUrl);
        Assert.Null(offer.FlightNumber);
    }

    [Fact]
    public async Task Direct_only_is_sent_and_any_fare_with_stops_is_still_left_out()
    {
        var http = new StubHttp().On("prices_for_dates", Answer(
            Fare("2026-12-16", "2026-12-20", price: 90),
            Fare("2026-12-16", "2026-12-20", price: 50, airline: "AF", transfers: 1),
            Fare("2026-12-16", "2026-12-20", price: 40, airline: "KL", returnTransfers: 1)));

        var offers = (await Provider(http).SearchAsync(Search(direct: true), CancellationToken.None)).Offers;

        Assert.Contains("direct=true", http.Requests[0].Query, StringComparison.Ordinal);
        Assert.Equal([90m], offers.Select(o => o.Price.Amount));
    }
}
