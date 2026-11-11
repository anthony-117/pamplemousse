using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pamplemousse.Api.ViewModels;
using Pamplemousse.Domain.Itinerary;

namespace Pamplemousse.Api.Tests.Plans;

/// <summary>
/// The day edit and rebuild routes of spec 010 (contracts/plan-edits-api.md). Paris has one landmark from the fake
/// Wikidata and no other places, so its alternatives are empty; the edit rules themselves are tested in Domain.
/// The fake hotels are moved near the landmark, so it fits a day.
/// </summary>
public class PlanEditEndpointsTests : IClassFixture<TestApiFactory>
{
    private const string Paris = "2988507";
    private const string Rome = "3169070";
    private static readonly Uri PlansUri = new("/api/v1/plans", UriKind.Relative);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };

    private readonly TestApiFactory _factory;
    private readonly string _uid = "uid-" + Guid.NewGuid().ToString("N")[..10];

    public PlanEditEndpointsTests(TestApiFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        // By the Eiffel Tower (this class has its own factory).
        factory.Hotels.Location = new Domain.Common.GeoPoint(48.857, 2.30);
        _factory = factory;
    }

    private HttpClient As(string? uid = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = TestTokens.Bearer(TestTokens.For(uid ?? _uid));
        return client;
    }

    private static Uri At(string path) => new(path, UriKind.Relative);

    private static async Task<PlanViewModel> BuildAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(PlansUri, new
        {
            destinationId = Paris,
            from = "2027-04-10",
            to = "2027-04-14",
            adults = 2,
            currency = "USD",
            originId = Rome,
            preferences = new { budget = 90, culture = 90, food = 50, nature = 20, nightlife = 20, comfort = 50 },
            pace = "balanced",
            tripBudget = 1200,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PlanViewModel>(Json))!;
    }

    private static (int Day, string Id) Landmark(PlanViewModel plan)
    {
        var day = plan.Days.First(d => d.Items.Any(i => i.Kind == ItemKind.Sight));
        return (day.Number, day.Items.First(i => i.Kind == ItemKind.Sight).Ref!.Id);
    }

    private static async Task<JsonElement> ErrorsOf(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
    }

    [Fact]
    public async Task The_list_and_the_plan_carry_what_the_app_s_plan_cards_and_trip_pane_read()
    {
        using var client = As();
        var plan = await BuildAsync(client);

        var item = (await client.GetFromJsonAsync<PlanListViewModel>(PlansUri, Json))!.Items.Single();

        Assert.Equal((Paris, 2, 1200m, "balanced"), (item.DestinationId, item.Adults, item.TripBudget, item.Pace));
        Assert.NotNull(item.Lat);
        Assert.NotNull(item.Lng);
        Assert.Empty(plan.Edits);
    }

    [Fact]
    public async Task Removing_an_item_leaves_its_time_free_and_is_listed_as_an_edit()
    {
        using var client = As();
        var plan = await BuildAsync(client);
        var (day, id) = Landmark(plan);

        var response = await client.DeleteAsync(At($"/api/v1/plans/{plan.Id}/days/{day}/items/{Uri.EscapeDataString(id)}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var changed = (await response.Content.ReadFromJsonAsync<PlanViewModel>(Json))!;
        Assert.DoesNotContain(changed.Days.SelectMany(d => d.Items), i => string.Equals(i.Ref?.Id, id, StringComparison.Ordinal));
        var edit = Assert.Single(changed.Edits);
        Assert.Equal((day, EditAction.Remove, id), (edit.Day, edit.Action, edit.ItemId));

        var read = await client.GetFromJsonAsync<PlanViewModel>($"/api/v1/plans/{plan.Id}", Json);
        Assert.Single(read!.Edits);
    }

    [Fact]
    public async Task Alternatives_say_why_there_are_none()
    {
        using var client = As();
        var plan = await BuildAsync(client);
        var (day, id) = Landmark(plan);

        var answer = await client.GetFromJsonAsync<JsonElement>($"/api/v1/plans/{plan.Id}/days/{day}/alternatives?itemId={Uri.EscapeDataString(id)}", Json);

        Assert.Equal("sight", answer.GetProperty("kind").GetString());
        Assert.Equal(0, answer.GetProperty("items").GetArrayLength());
        Assert.Equal($"No other sights are known near day {day}.", answer.GetProperty("message").GetString());
    }

    [Fact]
    public async Task A_wrong_value_names_its_field()
    {
        using var client = As();
        var plan = await BuildAsync(client);
        var (day, id) = Landmark(plan);
        var items = At($"/api/v1/plans/{plan.Id}/days/{day}/items");

        Assert.True((await ErrorsOf(await client.PostAsJsonAsync(items, new { kind = "place", id = "otm:nope" }))).TryGetProperty("id", out _));
        Assert.True((await ErrorsOf(await client.PostAsJsonAsync(items, new { kind = "car", id = "x" }))).TryGetProperty("kind", out _));
        Assert.True((await ErrorsOf(await client.PostAsJsonAsync(items, new { kind = "place", id = "x", replaces = "nope" }))).TryGetProperty("itemId", out _));
        Assert.True((await ErrorsOf(await client.GetAsync(At($"/api/v1/plans/{plan.Id}/days/99/alternatives")))).TryGetProperty("day", out _));
        Assert.True((await ErrorsOf(await client.GetAsync(At($"/api/v1/plans/{plan.Id}/days/{day}/alternatives?kind=museum")))).TryGetProperty("kind", out _));
        Assert.True((await ErrorsOf(await client.DeleteAsync(At($"/api/v1/plans/{plan.Id}/days/{day}/items/nope")))).TryGetProperty("itemId", out _));
        Assert.NotNull(id);
    }

    [Fact]
    public async Task Rebuild_changes_the_values_in_place_and_keeps_the_name_and_edits()
    {
        using var client = As();
        var plan = await BuildAsync(client);
        var (day, id) = Landmark(plan);
        await client.PatchAsJsonAsync(At($"/api/v1/plans/{plan.Id}"), new { name = "Paris with Maya" });
        await client.DeleteAsync(At($"/api/v1/plans/{plan.Id}/days/{day}/items/{Uri.EscapeDataString(id)}"));

        var response = await client.PostAsJsonAsync(At($"/api/v1/plans/{plan.Id}/rebuild"), new { to = "2027-04-13", adults = 3, pace = "relaxed" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rebuilt = (await response.Content.ReadFromJsonAsync<PlanViewModel>(Json))!;
        Assert.Equal((plan.Id, "Paris with Maya", 4, 3, "relaxed"), (rebuilt.Id, rebuilt.Name, rebuilt.Days.Count, rebuilt.Adults, rebuilt.Pace));
        Assert.Equal(plan.Preferences, rebuilt.Preferences);
        Assert.Equal(1200m, rebuilt.TripBudget);
        Assert.Equal(day <= 4 ? 1 : 0, rebuilt.Edits.Count);
        Assert.Single((await client.GetFromJsonAsync<PlanListViewModel>(PlansUri, Json))!.Items);
    }

    [Fact]
    public async Task A_refused_rebuild_leaves_the_plan_as_it_was()
    {
        using var client = As();
        var plan = await BuildAsync(client);

        Assert.True((await ErrorsOf(await client.PostAsJsonAsync(At($"/api/v1/plans/{plan.Id}/rebuild"), new { pace = "fast" }))).TryGetProperty("pace", out _));
        Assert.True((await ErrorsOf(await client.PostAsJsonAsync(At($"/api/v1/plans/{plan.Id}/rebuild"), new { originId = "1" }))).TryGetProperty("originId", out _));

        var read = await client.GetFromJsonAsync<PlanViewModel>($"/api/v1/plans/{plan.Id}", Json);
        Assert.Equal(JsonSerializer.Serialize(plan), JsonSerializer.Serialize(read));
    }

    [Fact]
    public async Task Another_travellers_plan_is_a_404_on_every_new_route()
    {
        using var fares = As();
        using var maya = As("uid-maya-" + Guid.NewGuid().ToString("N")[..6]);
        var plan = await BuildAsync(fares);

        Assert.Equal(HttpStatusCode.NotFound, (await maya.GetAsync(At($"/api/v1/plans/{plan.Id}/days/2/alternatives"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await maya.PostAsJsonAsync(At($"/api/v1/plans/{plan.Id}/days/2/items"), new { kind = "place", id = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await maya.DeleteAsync(At($"/api/v1/plans/{plan.Id}/days/2/items/x"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await maya.PostAsJsonAsync(At($"/api/v1/plans/{plan.Id}/rebuild"), new { adults = 1 })).StatusCode);
    }
}
