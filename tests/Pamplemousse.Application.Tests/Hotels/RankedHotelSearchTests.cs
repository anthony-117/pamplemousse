using Microsoft.Extensions.Logging.Abstractions;
using Pamplemousse.Application.Hotels;
using Pamplemousse.Application.Hotels.Queries.SearchHotels;
using Pamplemousse.Application.Ranking;
using Pamplemousse.Application.Tests.Destinations;
using Pamplemousse.Domain.Exceptions;
using Pamplemousse.Domain.Hotels;
using Pamplemousse.Domain.Places;
using Pamplemousse.Domain.Ranking;

namespace Pamplemousse.Application.Tests.Hotels;

/// <summary>Hotel search with preferences (spec 007).</summary>
public class RankedHotelSearchTests
{
    private readonly FakeHotelProvider _provider = new();
    private readonly FakeHotelData _store = new();
    private readonly FakeNearby _nearby = new();
    private readonly FakePlaces _sights = new(PlaceCategory.Attraction);

    public RankedHotelSearchTests()
    {
        // Hotel 1: dear, central, average. Hotel 2: cheap, 12 km out, well rated. Hotel 3: mid-price, 2 km out.
        _provider.Hotels.AddRange([
            HotelFakes.Found(1, 900, stars: 3, review: 7.0),
            HotelFakes.Found(2, 400, stars: 5, review: 9.2, km: 12),
            HotelFakes.Found(3, 600, stars: 4, review: 8.0, km: 2),
        ]);
    }

    private SearchHotelsHandler Handler() => new(
        new Fakes(), _provider, _store, _store, _store, new FakeSearchCache(), new FakeSearchLock(), new HotelSettings(), new MovableClock(),
        new HotelNearby(_nearby, [_sights], NullLogger<HotelNearby>.Instance), NullLogger<SearchHotelsHandler>.Instance);

    private static RankingInput Prefs(
        int? budget = null, int? culture = null, int? food = null, int? nature = null, int? nightlife = null, int? comfort = null, decimal? tripBudget = null) =>
        new(budget, culture, food, nature, nightlife, comfort, tripBudget);

    private static RankingInput Only(Criterion criterion, decimal? tripBudget = null) => new(
        criterion == Criterion.Budget ? 100 : 0,
        criterion == Criterion.Culture ? 100 : 0,
        criterion == Criterion.Food ? 100 : 0,
        criterion == Criterion.Nature ? 100 : 0,
        criterion == Criterion.Nightlife ? 100 : 0,
        criterion == Criterion.Comfort ? 100 : 0,
        tripBudget);

    private Task<SearchHotelsResponse> SearchAsync(string? sort, RankingInput? ranking = null, int? page = null) => Handler().HandleAsync(
        new SearchHotelsQuery("3169070", new DateOnly(2027, 4, 10), new DateOnly(2027, 4, 14), null, null, sort, Page: page, Ranking: ranking),
        CancellationToken.None);

    private static string[] Ids(SearchHotelsResponse r) => [.. r.Items.Select(i => i.Hotel.Id)];

    [Fact]
    public async Task Without_preferences_nothing_is_ranked_or_fetched()
    {
        var result = await SearchAsync(null, Prefs());

        Assert.Null(result.Ranking);
        Assert.Equal(["bk:1", "bk:2", "bk:3"], Ids(result));
        Assert.Equal((0, 0), (_nearby.Calls, _sights.Calls));
    }

    [Fact]
    public async Task Sort_by_match_alone_uses_neutral_weights_and_scores_every_hotel()
    {
        var result = await SearchAsync("match");

        Assert.Equal(HotelSort.Match, result.Sort);
        Assert.Equal(Weights.Neutral, result.Ranking!.Weights);
        Assert.Equal(3, result.Ranking.Matches.Count);
        Assert.All(result.Ranking.Matches.Values, m => Assert.Equal(6, m.Parts.Count));
    }

    [Fact]
    public async Task Budget_first_puts_the_cheapest_first()
    {
        var result = await SearchAsync("match", Only(Criterion.Budget));

        Assert.Equal(["bk:2", "bk:3", "bk:1"], Ids(result));
        Assert.Equal("$400, the cheapest of 3", result.Ranking!.Matches["bk:2"].Parts.Single().Detail);
    }

    [Fact]
    public async Task Food_favours_the_central_hotels_and_skips_the_sights()
    {
        var result = await SearchAsync("match", Only(Criterion.Food));

        Assert.Equal("bk:2", Ids(result)[^1]);
        Assert.Equal("300 restaurants and cafés within 1 km", result.Ranking!.Matches["bk:1"].Parts.Single().Detail);
        Assert.Equal((3, 0), (_nearby.Calls, _sights.Calls));
    }

    [Fact]
    public async Task Culture_counts_the_top_sights_near_each_hotel()
    {
        var result = await SearchAsync("match", Only(Criterion.Culture));

        Assert.Equal("1 top sight within 1.5 km", result.Ranking!.Matches["bk:1"].Parts.Single().Detail);
        Assert.Equal("0 top sights within 1.5 km", result.Ranking.Matches["bk:2"].Parts.Single().Detail);
        Assert.Equal(0, _nearby.Calls);
    }

    [Fact]
    public async Task Preferences_without_sort_by_match_keep_the_order_but_add_the_match()
    {
        var result = await SearchAsync("price", Only(Criterion.Comfort));

        Assert.Equal(["bk:2", "bk:3", "bk:1"], Ids(result));
        Assert.Equal(3, result.Ranking!.Matches.Count);
    }

    [Fact]
    public async Task Places_that_fail_count_as_average_with_one_warning()
    {
        _nearby.Error = new HttpRequestException("limit");

        var result = await SearchAsync("match", Prefs(food: 100));

        Assert.Equal([HotelNearby.Warning], result.Warnings);
        var food = result.Ranking!.Matches["bk:1"].Parts.Single(p => p.Criterion == Criterion.Food);
        Assert.Equal((false, "Unknown", 50), (food.Known, food.Detail, food.Percent));
    }

    [Fact]
    public async Task All_weights_zero_leaves_the_usual_order_with_a_note()
    {
        var result = await SearchAsync("match", Prefs(0, 0, 0, 0, 0, 0));

        Assert.Equal(["bk:1", "bk:2", "bk:3"], Ids(result));
        Assert.Empty(result.Ranking!.Matches);
        Assert.Equal("None of your preferences apply to hotels, so they are in the usual order.", result.Ranking.Note);
        Assert.Equal(0, _nearby.Calls);
    }

    [Fact]
    public async Task Hotels_over_the_trip_budget_come_last()
    {
        var result = await SearchAsync("match", Only(Criterion.Comfort, tripBudget: 500));

        Assert.Equal("bk:2", Ids(result)[0]);
        Assert.Equal("bk:1", Ids(result)[^1]);
        Assert.True(result.Ranking!.Matches["bk:1"].OverBudget);
        Assert.Equal(500m, result.Ranking.TripBudget);
    }

    [Fact]
    public async Task A_later_page_by_match_costs_no_request_and_says_why_it_is_empty()
    {
        _provider.TotalPages = 5;

        var result = await SearchAsync("match", page: 2);

        Assert.Equal([1], _provider.SearchedPages);
        Assert.Empty(result.Items);
        Assert.Equal((1, 3), (result.TotalPages, result.TotalItems));
        Assert.Equal("All the ranked hotels are on the earlier pages.", result.Message);
    }

    [Fact]
    public async Task Out_of_range_weights_name_the_field()
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() => SearchAsync("match", Prefs(food: 120)));

        Assert.Equal("food", ex.Field);
    }
}
