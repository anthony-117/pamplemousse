using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pamplemousse.Api.ViewModels;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Domain.Cars;

namespace Pamplemousse.Api.Tests.Cars;

public sealed class CarEndpointsTests : IClassFixture<TestApiFactory>
{
    private const string Rome = "3169070";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };

    // Every test asks for its own dates, so cached answers from another test never get in the way.
    private static int _nextDay = 40;

    private readonly TestApiFactory _factory;
    private readonly HttpClient _client;
    private readonly string _dates;

    public CarEndpointsTests(TestApiFactory factory)
    {
        _factory = factory;
        _factory.Cars.Error = null;
        _client = factory.CreateClient();
        var from = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(Interlocked.Add(ref _nextDay, 5));
        _dates = $"from={from:yyyy-MM-dd}&to={from.AddDays(4):yyyy-MM-dd}";
    }

    private Task<CarSearchViewModel?> SearchAsync(string query = "") =>
        _client.GetFromJsonAsync<CarSearchViewModel>($"/api/v1/destinations/{Rome}/cars?{_dates}{query}", Json);

    [Fact]
    public async Task Places_are_the_centre_then_nearby_airports()
    {
        var result = await _client.GetFromJsonAsync<CarPlacesViewModel>($"/api/v1/destinations/{Rome}/car-places", Json);

        Assert.NotNull(result);
        Assert.Equal(["centre", "FCO"], result.Places.Select(p => p.Id), StringComparer.Ordinal);
        Assert.Equal((PickUpKind.Centre, PickUpKind.Airport), (result.Places[0].Kind, result.Places[1].Kind));
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task Places_json_uses_camel_case_kinds()
    {
        var json = await _client.GetStringAsync(new Uri($"/api/v1/destinations/{Rome}/car-places", UriKind.Relative));

        Assert.Contains("\"kind\":\"airport\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Returns_cars_with_the_total_for_the_rental()
    {
        var result = await SearchAsync();

        Assert.NotNull(result);
        Assert.Equal(("Rome", "centre", 4, 30, "USD"), (result.Destination.Name, result.Place.Id, result.Dates.Days, result.DriverAge, result.Currency));
        Assert.Equal(("10:00", "10:00"), (result.Dates.PickUpTime, result.Dates.ReturnTime));

        // The same model from the same company and desk comes once, at its lowest price.
        Assert.Equal(["bk:1", "bk:2"], result.Items.Select(i => i.Id), StringComparer.Ordinal);
        var first = result.Items[0];
        Assert.Equal((300m, 75m, "USD", true), (first.Price.Total, first.Price.PerDay, first.Price.Currency, first.Price.PayNow));
        Assert.Equal((Gearbox.Automatic, 5, 3, "Budget"), (first.Gearbox, first.Seats, first.Bags, first.Company.Name));
        Assert.Equal((200m, "EUR"), (first.Deposit!.Amount, first.Deposit.Currency));
        Assert.Null(first.DamageExcess);
        Assert.True(first.Sponsored);
        Assert.Equal(("https://cars.booking.com/search-results?driversAge=30", "Booking.com"), (first.BookingUrl, first.Source));
        Assert.Equal((1, 2, 1, false), (result.Page, result.TotalItems, result.TotalPages, result.MayHaveMore));
        Assert.Null(result.DriverNote);
        Assert.NotNull(result.PricesFetchedAt);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task Filter_options_count_the_whole_result()
    {
        var result = await SearchAsync("&size=Mini");

        Assert.Equal(["bk:2"], result!.Items.Select(i => i.Id), StringComparer.Ordinal);
        Assert.Equal(["Intermediate", "Mini"], result.FilterOptions.Sizes.Select(o => o.Value).Order(StringComparer.Ordinal), StringComparer.Ordinal);
        Assert.Contains(result.FilterOptions.PickUpTypes, o => string.Equals(o.Value, "trainStation", StringComparison.Ordinal) && o.Count == 1);
    }

    [Fact]
    public async Task Sorting_and_filtering_reuse_the_saved_search()
    {
        await SearchAsync();
        var searches = _factory.Cars.Searches;

        var cheapest = await SearchAsync("&sort=price");
        var automatic = await SearchAsync("&gearbox=automatic&minBags=2");
        var station = await SearchAsync("&pickUpType=trainStation&company=avis");

        Assert.Equal(["bk:2", "bk:1"], cheapest!.Items.Select(i => i.Id), StringComparer.Ordinal);
        Assert.Equal(["bk:1"], automatic!.Items.Select(i => i.Id), StringComparer.Ordinal);
        Assert.Equal(["bk:2"], station!.Items.Select(i => i.Id), StringComparer.Ordinal);
        Assert.Equal(searches, _factory.Cars.Searches);
    }

    [Fact]
    public async Task A_filter_with_no_match_gives_a_message_and_the_booking_search()
    {
        var result = await SearchAsync("&maxPrice=10");

        Assert.Empty(result!.Items);
        Assert.NotNull(result.Message);
        Assert.NotNull(result.SearchUrl);
    }

    [Fact]
    public async Task A_young_driver_gets_a_note()
    {
        var result = await SearchAsync("&driverAge=21");

        Assert.Equal(21, result!.DriverAge);
        Assert.Contains("young driver", result.DriverNote, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_airport_can_be_the_pick_up_place()
    {
        var result = await SearchAsync("&place=fco");

        Assert.Equal(("FCO", PickUpKind.Airport, "FCO"), (result!.Place.Id, result.Place.Kind, result.Place.Iata));
    }

    [Fact]
    public async Task A_source_failure_is_a_warning_not_an_error()
    {
        _factory.Cars.Error = new HttpRequestException("down");

        var result = await SearchAsync();

        Assert.Empty(result!.Items);
        Assert.NotNull(result.SearchUrl);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public async Task A_missing_key_names_the_variable()
    {
        _factory.Cars.Error = new ProviderNotConfiguredException("Cars", "RAPIDAPI_KEY");

        var result = await SearchAsync();

        Assert.Contains(result!.Warnings, w => w.Contains("RAPIDAPI_KEY", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("&driverAge=17", "driverAge")]
    [InlineData("&currency=EURO", "currency")]
    [InlineData("&sort=cheapest", "sort")]
    [InlineData("&gearbox=cvt", "gearbox")]
    [InlineData("&pickUpType=boat", "pickUpType")]
    [InlineData("&minSeats=0", "minSeats")]
    [InlineData("&minBags=10", "minBags")]
    [InlineData("&maxPrice=0", "maxPrice")]
    [InlineData("&page=0", "page")]
    [InlineData("&place=XYZ", "place")]
    public async Task Invalid_input_is_a_400_naming_the_field(string query, string field)
    {
        var response = await _client.GetAsync(new Uri($"/api/v1/destinations/{Rome}/cars?{_dates}{query}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(field, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Return_before_pick_up_is_a_400()
    {
        var response = await _client.GetAsync(new Uri($"/api/v1/destinations/{Rome}/cars?from=2030-04-14&to=2030-04-10", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("cars")]
    [InlineData("car-places")]
    public async Task An_unknown_destination_is_a_404(string path)
    {
        var response = await _client.GetAsync(new Uri($"/api/v1/destinations/0/{path}?{_dates}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
