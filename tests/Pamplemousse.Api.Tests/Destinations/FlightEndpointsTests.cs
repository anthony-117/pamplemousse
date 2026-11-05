using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pamplemousse.Api.ViewModels;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Domain.Exceptions;
using Pamplemousse.Domain.Flights;
using Pamplemousse.Domain.Ranking;

namespace Pamplemousse.Api.Tests.Destinations;

public sealed class FlightEndpointsTests : IClassFixture<TestApiFactory>
{
    private const string Rome = "3169070";
    private const string Paris = "2988507";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };

    private readonly TestApiFactory _factory;
    private readonly HttpClient _client;

    public FlightEndpointsTests(TestApiFactory factory)
    {
        _factory = factory;
        _factory.Flights.Error = null;
        _factory.Flights.Offers = [FakeFlights.Offer("tp:cheap", 90), FakeFlights.Offer("tp:dear", 240)];
        _client = factory.CreateClient();
    }

    private static string Dates()
    {
        var from = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(40);
        return $"from={from:yyyy-MM-dd}&to={from.AddDays(4):yyyy-MM-dd}";
    }

    private Task<FlightSearchViewModel?> GetAsync(string query) =>
        _client.GetFromJsonAsync<FlightSearchViewModel>($"/api/v1/destinations/{Rome}/flights?origin={Paris}&{Dates()}{query}", Json);

    [Fact]
    public async Task Returns_flights_with_the_route_and_a_price_note()
    {
        var result = await GetAsync("");

        Assert.NotNull(result);
        Assert.Equal(("Paris", "PAR"), (result.Origin.Name, result.Origin.Code));
        Assert.Equal(("Rome", "ROM"), (result.Destination.Name, result.Destination.Code));
        Assert.False(result.Destination.IsNearby);
        Assert.Equal(["tp:cheap", "tp:dear"], result.Items.Select(i => i.Id), StringComparer.Ordinal);
        Assert.Equal(TripType.RoundTrip, result.TripType);
        Assert.Equal("USD", result.Currency);
        Assert.Contains("Recent prices", result.PriceNote, StringComparison.Ordinal);
        Assert.Equal("Air France", result.Items[0].Airline);
        Assert.Equal(90m, result.Items[0].Price.Amount);
        Assert.Null(result.SearchUrl);
    }

    [Fact]
    public async Task Json_uses_the_contract_names()
    {
        var body = await _client.GetStringAsync(new Uri($"/api/v1/destinations/{Rome}/flights?origin={Paris}&{Dates()}", UriKind.Relative));

        Assert.Contains("\"tripType\":\"roundTrip\"", body, StringComparison.Ordinal);
        Assert.Contains("\"dateMatch\":\"exact\"", body, StringComparison.Ordinal);
        Assert.Contains("\"bookingUrl\":", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_origin_is_rejected_and_named()
    {
        var response = await _client.GetAsync(new Uri($"/api/v1/destinations/{Rome}/flights", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Origin", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("origin=3169070", "origin")]                         // same city
    [InlineData("origin=2988507&from=2020-01-01&to=2020-01-03", "to")] // past dates
    [InlineData("origin=2988507&tripType=multiCity", "tripType")]
    [InlineData("origin=2988507&currency=EURO", "currency")]
    [InlineData("origin=2988507&limit=abc", "Limit")]
    [InlineData("origin=2988507&directOnly=maybe", "DirectOnly")]
    public async Task Bad_input_is_rejected_and_names_the_field(string query, string field)
    {
        var response = await _client.GetAsync(new Uri($"/api/v1/destinations/{Rome}/flights?{query}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty(field, out _), $"Expected an error on {field}: {errors}");
    }

    [Theory]
    [InlineData("/api/v1/destinations/nope/flights?origin=2988507")]
    [InlineData("/api/v1/destinations/3169070/flights?origin=nope")]
    public async Task Unknown_cities_are_not_found(string url)
    {
        var response = await _client.GetAsync(new Uri(url, UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Without_a_token_the_response_succeeds_with_a_warning()
    {
        _factory.Flights.Error = new ProviderNotConfiguredException("Travelpayouts", "TRAVELPAYOUTS_API_TOKEN");

        var result = await GetAsync("");

        Assert.NotNull(result);
        Assert.Empty(result.Items);
        Assert.Contains(result.Warnings, w => w.Contains("TRAVELPAYOUTS_API_TOKEN", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_source_failure_is_a_warning()
    {
        _factory.Flights.Error = new HttpRequestException("down");

        var result = await GetAsync("");

        Assert.NotNull(result);
        Assert.Empty(result.Items);
        Assert.Equal(["Flights is unavailable right now."], result.Warnings, StringComparer.Ordinal);
    }

    [Fact]
    public async Task A_currency_the_source_rejects_is_bad_input()
    {
        _factory.Flights.Error = new DomainException("Currency XYZ is not supported.", "currency");

        var response = await _client.GetAsync(new Uri($"/api/v1/destinations/{Rome}/flights?origin={Paris}&currency=XYZ", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task One_way_and_direct_only_reach_the_search()
    {
        await GetAsync("&tripType=ONEWAY&directOnly=true&currency=eur");

        var search = _factory.Flights.LastSearch;
        Assert.NotNull(search);
        Assert.Equal((TripType.OneWay, true, "EUR"), (search.TripType, search.DirectOnly, search.Currency));
    }

    [Fact]
    public async Task No_direct_flights_is_an_empty_result_not_an_error()
    {
        _factory.Flights.Offers = [];

        var result = await GetAsync("&directOnly=true");

        Assert.NotNull(result);
        Assert.Empty(result.Items);
        Assert.NotNull(result.Message);
        Assert.NotNull(result.SearchUrl);
    }

    [Fact]
    public async Task A_small_city_shows_which_airport_was_used()
    {
        var response = await _client.GetAsync(new Uri($"/api/v1/destinations?q=byblos", UriKind.Relative));
        var byblos = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement[0].GetProperty("id").GetString();

        var result = await _client.GetFromJsonAsync<FlightSearchViewModel>($"/api/v1/destinations/{byblos}/flights?origin={Paris}&{Dates()}", Json);

        Assert.NotNull(result);
        Assert.Equal("BEY", result.Destination.Code);
        Assert.True(result.Destination.IsNearby);
    }

    [Fact]
    public async Task Sort_by_match_with_comfort_first_puts_the_direct_flight_first()
    {
        _factory.Flights.Offers = [FakeFlights.Offer("tp:cheap", 90) with { Stops = 2 }, FakeFlights.Offer("tp:dear", 240)];

        var result = await GetAsync("&sort=match&budget=20&comfort=90");

        Assert.Equal(FlightSort.Match, result!.Sort);
        Assert.Equal(["tp:dear", "tp:cheap"], result.Items.Select(i => i.Id), StringComparer.Ordinal);
        Assert.Equal(new WeightsViewModel(20, 50, 50, 50, 50, 90), result.Weights);
        Assert.Equal([Criterion.Budget, Criterion.Comfort], result.Items[0].Match!.Parts.Select(p => p.Criterion));
    }

    [Fact]
    public async Task The_default_order_is_cheapest_first_without_a_match()
    {
        var result = await GetAsync("");

        Assert.Equal(FlightSort.Cheapest, result!.Sort);
        Assert.All(result.Items, i => Assert.Null(i.Match));
        Assert.Null(result.Weights);
    }

    [Theory]
    [InlineData("&sort=fastest", "sort")]
    [InlineData("&comfort=200", "comfort")]
    public async Task Bad_ranking_values_are_a_400_naming_the_field(string query, string field)
    {
        var response = await _client.GetAsync(new Uri($"/api/v1/destinations/{Rome}/flights?origin={Paris}&{Dates()}{query}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(field, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
}
