using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pamplemousse.Api.ViewModels;
using Pamplemousse.Application.Abstractions;

namespace Pamplemousse.Api.Tests.Hotels;

public sealed class HotelEndpointsTests : IClassFixture<TestApiFactory>
{
    private const string Rome = "3169070";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };

    // Every test asks for its own dates, so cached answers from another test never get in the way.
    private static int _nextDay = 40;

    private readonly TestApiFactory _factory;
    private readonly HttpClient _client;
    private readonly string _dates;

    public HotelEndpointsTests(TestApiFactory factory)
    {
        _factory = factory;
        _factory.Hotels.Error = null;
        _client = factory.CreateClient();
        var from = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(Interlocked.Add(ref _nextDay, 5));
        _dates = $"from={from:yyyy-MM-dd}&to={from.AddDays(4):yyyy-MM-dd}";
    }

    private Task<HotelSearchViewModel?> SearchAsync(string query = "") =>
        _client.GetFromJsonAsync<HotelSearchViewModel>($"/api/v1/destinations/{Rome}/hotels?{_dates}{query}", Json);

    [Fact]
    public async Task Returns_hotels_with_the_total_for_the_stay()
    {
        var result = await SearchAsync();

        Assert.NotNull(result);
        Assert.Equal(("Rome", 4, 2, "USD"), (result.Destination.Name, result.Dates.Nights, result.Adults, result.Currency));
        Assert.Equal(["bk:1", "bk:2"], result.Items.Select(i => i.Id), StringComparer.Ordinal);
        var first = result.Items[0];
        Assert.Equal((900m, 225m, "USD"), (first.Price.Total, first.Price.PerNight, first.Price.Currency));
        Assert.Equal(("https://cf.bstatic.com/square500/1.jpg", 4, 8.8, "Booking.com"), (first.PhotoUrl, first.Stars, first.ReviewScore, first.Source));
        Assert.Null(result.Items[1].Stars);
        Assert.Equal((1, 2, 1), (result.Page, result.TotalItems, result.TotalPages));
        Assert.NotNull(result.PricesFetchedAt);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task Sorting_and_filtering_reuse_the_stored_search()
    {
        await SearchAsync();
        var searches = _factory.Hotels.Searches;

        var cheapest = await SearchAsync("&sort=price&minReview=7");
        var fourStars = await SearchAsync("&minStars=4");

        Assert.Equal(["bk:2", "bk:1"], cheapest!.Items.Select(i => i.Id), StringComparer.Ordinal);
        Assert.Equal(["bk:1"], fourStars!.Items.Select(i => i.Id), StringComparer.Ordinal);
        Assert.Equal(searches, _factory.Hotels.Searches);
    }

    [Fact]
    public async Task A_source_failure_is_a_warning_not_an_error()
    {
        _factory.Hotels.Error = new HttpRequestException("down");

        var result = await SearchAsync();

        Assert.Empty(result!.Items);
        Assert.NotNull(result.SearchUrl);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public async Task A_missing_key_names_the_variable()
    {
        _factory.Hotels.Error = new ProviderNotConfiguredException("Hotels", "RAPIDAPI_KEY");

        var result = await SearchAsync();

        Assert.Contains(result!.Warnings, w => w.Contains("RAPIDAPI_KEY", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("&adults=9", "adults")]
    [InlineData("&currency=EURO", "currency")]
    [InlineData("&sort=cheapest", "sort")]
    [InlineData("&minStars=6", "minStars")]
    [InlineData("&minPrice=500&maxPrice=100", "maxPrice")]
    [InlineData("&page=0", "page")]
    public async Task Invalid_input_is_a_400_naming_the_field(string query, string field)
    {
        var response = await _client.GetAsync(new Uri($"/api/v1/destinations/{Rome}/hotels?{_dates}{query}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(field, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Check_out_before_check_in_is_a_400()
    {
        var response = await _client.GetAsync(new Uri($"/api/v1/destinations/{Rome}/hotels?from=2030-04-14&to=2030-04-10", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_destination_is_a_404()
    {
        var response = await _client.GetAsync(new Uri($"/api/v1/destinations/0/hotels?{_dates}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Opening_a_hotel_gives_its_details_and_booking_link()
    {
        await SearchAsync();

        var hotel = await _client.GetFromJsonAsync<HotelDetailsViewModel>($"/api/v1/hotels/bk:1?{_dates}", Json);

        Assert.NotNull(hotel);
        Assert.Equal(("bk:1", "Hotels", true), (hotel.Id, hotel.StayType, hotel.FreeCancellation));
        Assert.StartsWith("https://www.booking.com/hotel/it/roma.html?checkin=", hotel.BookingUrl, StringComparison.Ordinal);
        Assert.Equal(900m, hotel.Price?.Total);
        Assert.Equal(["Free WiFi"], hotel.Facilities, StringComparer.Ordinal);
    }

    [Fact]
    public async Task An_unknown_hotel_is_a_404()
    {
        var response = await _client.GetAsync(new Uri($"/api/v1/hotels/bk:999?{_dates}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
