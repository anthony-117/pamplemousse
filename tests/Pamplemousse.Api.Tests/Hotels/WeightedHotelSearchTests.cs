using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pamplemousse.Api.ViewModels;
using Pamplemousse.Domain.Hotels;
using Pamplemousse.Domain.Ranking;

namespace Pamplemousse.Api.Tests.Hotels;

/// <summary>GET /destinations/{id}/hotels with preferences (spec 007, contracts/weighted-search-api.md).</summary>
public sealed class WeightedHotelSearchTests : IClassFixture<TestApiFactory>
{
    private const string Rome = "3169070";
    private const string BudgetOnly = "&budget=100&culture=0&food=0&nature=0&nightlife=0&comfort=0";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };

    private static int _nextDay = 400;

    private readonly TestApiFactory _factory;
    private readonly HttpClient _client;
    private readonly string _dates;

    public WeightedHotelSearchTests(TestApiFactory factory)
    {
        _factory = factory;
        _factory.Hotels.Error = null;
        _factory.Nearby.Error = null;
        _client = factory.CreateClient();
        var from = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(Interlocked.Add(ref _nextDay, 5));
        _dates = $"from={from:yyyy-MM-dd}&to={from.AddDays(4):yyyy-MM-dd}";
    }

    private Task<HotelSearchViewModel?> SearchAsync(string query) =>
        _client.GetFromJsonAsync<HotelSearchViewModel>($"/api/v1/destinations/{Rome}/hotels?{_dates}{query}", Json);

    [Fact]
    public async Task Sort_by_match_ranks_and_explains_each_hotel()
    {
        var result = await SearchAsync("&sort=match" + BudgetOnly + "&tripBudget=1500");

        Assert.NotNull(result);
        Assert.Equal(HotelSort.Match, result.Sort);
        Assert.Equal(["bk:2", "bk:1"], result.Items.Select(i => i.Id), StringComparer.Ordinal);
        Assert.Equal(new WeightsViewModel(100, 0, 0, 0, 0, 0), result.Weights);
        Assert.Equal(1500m, result.TripBudget);
        Assert.Null(result.RankingNote);

        var match = result.Items[0].Match!;
        Assert.Equal((100, "Cheapest option", false), (match.Percent, match.Reason, match.OverBudget));
        var part = Assert.Single(match.Parts);
        Assert.Equal((Criterion.Budget, 100, 100, true, "$400, the cheapest of 2"), (part.Criterion, part.Weight, part.Percent, part.Known, part.Detail));
    }

    [Fact]
    public async Task Interests_show_what_is_near_each_hotel()
    {
        var result = await SearchAsync("&sort=match&food=100&culture=0");

        var food = result!.Items[0].Match!.Parts.Single(p => p.Criterion == Criterion.Food);
        Assert.Equal((true, "120 restaurants and cafés within 1 km"), (food.Known, food.Detail));
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task Without_preferences_there_is_no_match()
    {
        var result = await SearchAsync("&sort=price");

        Assert.All(result!.Items, i => Assert.Null(i.Match));
        Assert.Null(result.Weights);
    }

    [Fact]
    public async Task Places_that_cannot_be_checked_give_a_warning_not_an_error()
    {
        _factory.Nearby.Error = new HttpRequestException("down");

        var result = await SearchAsync("&sort=match&nightlife=90");

        Assert.Contains("Places near the hotels couldn't be checked just now", result!.Warnings[0], StringComparison.Ordinal);
        Assert.False(result.Items[0].Match!.Parts.Single(p => p.Criterion == Criterion.Nightlife).Known);
    }

    [Fact]
    public async Task All_weights_zero_gives_a_note_and_no_match()
    {
        var result = await SearchAsync("&sort=match&budget=0&culture=0&food=0&nature=0&nightlife=0&comfort=0");

        Assert.Equal("None of your preferences apply to hotels, so they are in the usual order.", result!.RankingNote);
        Assert.All(result.Items, i => Assert.Null(i.Match));
    }

    [Theory]
    [InlineData("&budget=101", "budget")]
    [InlineData("&comfort=-5", "comfort")]
    [InlineData("&tripBudget=0", "tripBudget")]
    public async Task Out_of_range_preferences_are_a_400_naming_the_field(string query, string field)
    {
        var response = await _client.GetAsync(new Uri($"/api/v1/destinations/{Rome}/hotels?{_dates}&sort=match{query}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(field, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
}
