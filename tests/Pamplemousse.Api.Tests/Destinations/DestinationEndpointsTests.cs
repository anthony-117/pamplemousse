using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pamplemousse.Api.Features.Destinations;

namespace Pamplemousse.Api.Tests.Destinations;

public class DestinationEndpointsTests : IClassFixture<TestApiFactory>
{
    private const string Rome = "3169070";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };

    private readonly TestApiFactory _factory;
    private readonly HttpClient _client;

    public DestinationEndpointsTests(TestApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static string FutureDates(int startInDays = 40, int days = 4)
    {
        var from = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(startInDays);
        return $"from={from:yyyy-MM-dd}&to={from.AddDays(days):yyyy-MM-dd}";
    }

    [Fact]
    public async Task Search_ranks_the_biggest_exact_match_first()
    {
        var results = await _client.GetFromJsonAsync<List<DestinationSummary>>("/api/v1/destinations?q=rom", Json);

        Assert.NotNull(results);
        Assert.Equal("Rome", results[0].Name);
        Assert.Equal("Italy", results[0].CountryName);
        Assert.Equal("Lazio", results[0].Region);
    }

    [Fact]
    public async Task Search_ignores_accents_and_returns_each_city_once()
    {
        var results = await _client.GetFromJsonAsync<List<DestinationSummary>>("/api/v1/destinations?q=sao%20paulo", Json);

        Assert.NotNull(results);
        Assert.Equal("São Paulo", results[0].Name);
        Assert.Equal(results.Count, results.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task Search_needs_two_characters()
    {
        var results = await _client.GetFromJsonAsync<List<DestinationSummary>>("/api/v1/destinations?q=r", Json);

        Assert.NotNull(results);
        Assert.Empty(results);
    }

    [Fact]
    public async Task Destination_details_include_the_country()
    {
        var details = await _client.GetFromJsonAsync<DestinationDetails>($"/api/v1/destinations/{Rome}", Json);

        Assert.NotNull(details);
        Assert.Equal("Rome", details.Destination.Name);
        Assert.Equal("EUR", details.Country?.Currencies[0].Code);
    }

    [Theory]
    [InlineData("/api/v1/places/gp%3Aabc")]
    [InlineData("/api/v1/places/no-prefix")]
    public async Task Unknown_place_returns_404(string url)
    {
        var response = await _client.GetAsync(new Uri(url, UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Place_details_without_a_key_explain_why()
    {
        var response = await _client.GetAsync(new Uri("/api/v1/places/otm%3AW3", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("OPENTRIPMAP_API_KEY", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_destination_returns_404()
    {
        var response = await _client.GetAsync(new Uri("/api/v1/destinations/999999999", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Overview_combines_every_section()
    {
        _factory.Exchange.Fail = false;

        var overview = await _client.GetFromJsonAsync<OverviewResponse>($"/api/v1/destinations/{Rome}/overview?passport=LB&currency=USD&{FutureDates()}", Json);

        Assert.NotNull(overview);
        Assert.Equal("Rome", overview.Destination.Name);
        Assert.Equal("EUR", overview.Exchange?.Quote);
        Assert.Equal("visa required", overview.Visa?.Requirement);
        Assert.Equal(2, overview.Advisory?.Level);
        Assert.Single(overview.Weather!.Days);
        Assert.Single(overview.Holidays!);
        Assert.Empty(overview.Warnings);
    }

    [Fact]
    public async Task Overview_keeps_other_sections_when_one_source_fails()
    {
        _factory.Exchange.Fail = true;
        try
        {
            var overview = await _client.GetFromJsonAsync<OverviewResponse>($"/api/v1/destinations/{Rome}/overview?currency=USD&{FutureDates()}", Json);

            Assert.NotNull(overview);
            Assert.Null(overview.Exchange);
            Assert.NotNull(overview.Weather);
            Assert.Contains(overview.Warnings, w => w.StartsWith("Exchange rate is unavailable", StringComparison.Ordinal));
        }
        finally
        {
            _factory.Exchange.Fail = false;
        }
    }

    [Theory]
    [InlineData(40, -2)] // ends before it starts
    [InlineData(40, 31)] // longer than 30 days
    public async Task Overview_rejects_invalid_dates(int startInDays, int days)
    {
        var response = await _client.GetAsync(new Uri($"/api/v1/destinations/{Rome}/overview?{FutureDates(startInDays, days)}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("restaurant", "OPENTRIPMAP_API_KEY")]
    [InlineData("attraction", "OPENTRIPMAP_API_KEY")]
    public async Task Places_without_a_key_return_a_warning(string category, string variable)
    {
        var places = await _client.GetFromJsonAsync<PlacesResponse>($"/api/v1/destinations/{Rome}/places?category={category}", Json);

        Assert.NotNull(places);
        Assert.Empty(places.Items);
        Assert.Contains(places.Warnings, w => w.Contains(variable, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Attractions_come_from_wikidata_and_need_no_key()
    {
        var places = await _client.GetFromJsonAsync<PlacesResponse>("/api/v1/destinations/2988507/places?category=attraction&limit=1", Json);

        Assert.NotNull(places);
        Assert.Empty(places.Warnings);
        var tower = Assert.Single(places.Items);
        Assert.Equal("wd:Q243", tower.Id);
        Assert.Equal("Eiffel Tower", tower.Name);
        Assert.Equal("Observation tower", tower.Kind);
    }

    [Theory]
    [InlineData("/api/v1/destinations/3169070/places?category=museum")]
    [InlineData("/api/v1/destinations/3169070/places?limit=abc")]
    public async Task Bad_query_values_return_400(string url)
    {
        var response = await _client.GetAsync(new Uri(url, UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Events_without_a_key_return_a_warning()
    {
        var events = await _client.GetFromJsonAsync<EventsResponse>($"/api/v1/destinations/{Rome}/events?{FutureDates()}", Json);

        Assert.NotNull(events);
        Assert.Contains(events.Warnings, w => w.Contains("TICKETMASTER_API_KEY", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Countries_list_includes_currencies()
    {
        var countries = await _client.GetFromJsonAsync<List<CountryOption>>("/api/v1/countries", Json);

        Assert.NotNull(countries);
        Assert.Contains(countries, c => string.Equals(c.Code, "LB", StringComparison.Ordinal) && c.Currencies.Contains("LBP", StringComparer.Ordinal));
    }
}
