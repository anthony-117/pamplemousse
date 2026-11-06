using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pamplemousse.Api.ViewModels;
using Pamplemousse.Domain.Itinerary;

namespace Pamplemousse.Api.Tests.Plans;

/// <summary>
/// The plan routes (spec 008, contracts/itinerary-api.md). Paris has one landmark from the fake Wikidata, so it has places;
/// Rome has none (OpenTripMap has no key here), so a plan for Rome is a 422. The day rules themselves are tested in Domain.
/// </summary>
public class PlanEndpointsTests(TestApiFactory factory) : IClassFixture<TestApiFactory>
{
    private const string Paris = "2988507";
    private const string Rome = "3169070";
    private static readonly Uri PlansUri = new("/api/v1/plans", UriKind.Relative);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };

    // Each test signs in as its own traveller, so tests sharing the API never see each other's plans.
    private readonly string _uid = "uid-" + Guid.NewGuid().ToString("N")[..10];

    private HttpClient As(string? uid = null)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = TestTokens.Bearer(TestTokens.For(uid ?? _uid));
        return client;
    }

    private static object Request(string destination = Paris, string? pace = "balanced", int budget = 90, object? tripBudget = null) => new
    {
        destinationId = destination,
        from = "2027-04-10",
        to = "2027-04-14",
        adults = 2,
        currency = "USD",
        originId = string.Equals(destination, Rome, StringComparison.Ordinal) ? Paris : Rome,
        preferences = new { budget, culture = 90, food = 50, nature = 20, nightlife = 20, comfort = 50 },
        pace,
        tripBudget,
    };

    private static async Task<PlanViewModel> PlanOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<PlanViewModel>(Json))!;

    private static async Task<PlanViewModel> BuildAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(PlansUri, Request());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await PlanOf(response);
    }

    [Fact]
    public async Task A_plan_is_built_saved_and_read_back()
    {
        using var client = As();

        var response = await client.PostAsJsonAsync(PlansUri, Request());
        var plan = await PlanOf(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"/api/v1/plans/{plan.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal(("Paris, 5 days", PlanStatus.Draft, "balanced"), (plan.Name, plan.Status, plan.Pace));
        Assert.Equal(5, plan.Days.Count);
        Assert.NotNull(plan.Hotel?.Offer.Match);
        Assert.NotNull(plan.Flight?.Offer.Match);
        Assert.Null(plan.Car);
        Assert.Equal(ItemKind.Arrival, plan.Days[0].Items[0].Kind);
        Assert.Equal(ItemKind.Departure, plan.Days[^1].Items[^1].Kind);
        Assert.Single(plan.Warnings, w => w.StartsWith("OpenTripMap is not configured", StringComparison.Ordinal));
        Assert.Matches("^[0-2][0-9]:[0-5][0-9]$", plan.Days[1].Items[0].Start);
        Assert.NotNull(plan.Match);

        var read = await client.GetFromJsonAsync<PlanViewModel>($"/api/v1/plans/{plan.Id}", Json);
        Assert.Equal(JsonSerializer.Serialize(plan), JsonSerializer.Serialize(read));
    }

    [Fact]
    public async Task A_city_with_no_places_is_a_422_no_places()
    {
        using var client = As();

        var response = await client.PostAsJsonAsync(PlansUri, Request(destination: Rome));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("no-places", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("1", "balanced", 90, null, "destinationId")]
    [InlineData(Paris, "fast", 90, null, "pace")]
    [InlineData(Paris, "balanced", 101, null, "budget")]
    [InlineData(Paris, "balanced", 90, -5, "tripBudget")]
    public async Task A_bad_value_is_a_400_naming_the_field(string destination, string pace, int budget, int? tripBudget, string field)
    {
        using var client = As();

        var response = await client.PostAsJsonAsync(PlansUri, Request(destination, pace, budget, tripBudget));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.True(errors.TryGetProperty(field, out _), field);
    }

    [Fact]
    public async Task Another_travellers_plan_is_a_404()
    {
        using var fares = As();
        using var maya = As("uid-maya-" + Guid.NewGuid().ToString("N")[..6]);
        var plan = await BuildAsync(fares);

        Assert.Equal(HttpStatusCode.NotFound, (await maya.GetAsync(new Uri($"/api/v1/plans/{plan.Id}", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await maya.DeleteAsync(new Uri($"/api/v1/plans/{plan.Id}", UriKind.Relative))).StatusCode);
        Assert.Equal(0, (await maya.GetFromJsonAsync<PlanListViewModel>(PlansUri, Json))?.Count);
    }

    [Fact]
    public async Task The_list_renames_and_deletes()
    {
        using var client = As();
        var plan = await BuildAsync(client);

        var patch = await client.PatchAsJsonAsync($"/api/v1/plans/{plan.Id}", new { name = "Paris with Maya", status = "planned" });
        var list = await client.GetFromJsonAsync<PlanListViewModel>(PlansUri, Json);

        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        Assert.Equal((1, 50), (list?.Count, list?.Max));
        Assert.Equal(("Paris with Maya", PlanStatus.Planned, "Paris"), (list!.Items[0].Name, list.Items[0].Status, list.Items[0].DestinationName));

        var badName = await client.PatchAsJsonAsync($"/api/v1/plans/{plan.Id}", new { name = new string('a', 61) });
        Assert.Equal(HttpStatusCode.BadRequest, badName.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(new Uri($"/api/v1/plans/{plan.Id}", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(new Uri($"/api/v1/plans/{plan.Id}", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task The_hotel_is_swapped_from_the_kept_list()
    {
        using var client = As();
        var plan = await BuildAsync(client);
        var hotels = await client.GetFromJsonAsync<PlanAlternativesViewModel<HotelViewModel>>($"/api/v1/plans/{plan.Id}/alternatives/hotels", Json);
        var other = hotels!.Items.First(h => !string.Equals(h.Id, plan.Hotel!.Offer.Id, StringComparison.Ordinal));
        var searches = factory.Hotels.Searches;

        var swap = await client.PutAsJsonAsync($"/api/v1/plans/{plan.Id}/hotel", new { offerId = other.Id });
        var swapped = await PlanOf(swap);

        Assert.Equal(HttpStatusCode.OK, swap.StatusCode);
        Assert.Equal(other.Id, swapped.Hotel?.Offer.Id);
        Assert.Equal(searches, factory.Hotels.Searches);
        Assert.All(hotels.Items, h => Assert.NotNull(h.Match));

        var unknown = await client.PutAsJsonAsync($"/api/v1/plans/{plan.Id}/hotel", new { offerId = "bk:nope" });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.True((await unknown.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").TryGetProperty("offerId", out _));
    }

    [Fact]
    public async Task A_car_is_added_from_the_car_list_then_removed()
    {
        using var client = As();
        var plan = await BuildAsync(client);

        var cars = await client.GetFromJsonAsync<PlanAlternativesViewModel<CarViewModel>>($"/api/v1/plans/{plan.Id}/alternatives/cars", Json);
        Assert.NotEmpty(cars!.Items);

        var withCar = await PlanOf(await client.PutAsJsonAsync($"/api/v1/plans/{plan.Id}/car", new { offerId = cars.Items[0].Id }));
        Assert.Equal(cars.Items[0].Id, withCar.Car?.Offer.Id);
        Assert.NotNull(withCar.Total.Car);

        var without = await PlanOf(await client.DeleteAsync(new Uri($"/api/v1/plans/{plan.Id}/car", UriKind.Relative)));
        Assert.Null(without.Car);
        Assert.Null(without.Total.Car);
    }

    [Fact]
    public async Task An_unknown_kind_is_not_a_route()
    {
        using var client = As();
        var plan = await BuildAsync(client);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(new Uri($"/api/v1/plans/{plan.Id}/alternatives/boats", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task Plans_need_a_sign_in()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = null;

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(PlansUri)).StatusCode);
    }

    [Fact]
    public async Task The_profile_pace_is_saved_and_used_when_a_plan_leaves_it_out()
    {
        using var client = As();
        var form = new
        {
            displayName = "Fares H",
            passport = "LB",
            homeCurrency = "EUR",
            homeCityId = Rome,
            preferences = new { budget = 50, culture = 50, food = 50, nature = 50, nightlife = 50, comfort = 50 },
            pace = "packed",
        };

        var put = await client.PutAsJsonAsync("/api/v1/me", form);
        var profile = await client.GetFromJsonAsync<ProfileViewModel>("/api/v1/me", Json);
        var plan = await PlanOf(await client.PostAsJsonAsync(PlansUri, Request(pace: null)));

        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Equal("packed", profile?.Pace);
        Assert.Equal("packed", plan.Pace);
    }
}
