using Pamplemousse.Domain.Exceptions;
using Pamplemousse.Domain.Ranking;

namespace Pamplemousse.Domain.Tests.Ranking;

public class ScoringEngineTests
{
    /// <summary>An offer with a price and a fixed comfort measure; null comfort is unknown.</summary>
    private sealed record Item(string Id, int Rank, decimal Price, double? Comfort);

    private static Candidate Describe(Item item) => new(item.Id, item.Rank, item.Price, new Dictionary<Criterion, CriterionInput>
    {
        [Criterion.Budget] = new([new Part((double)item.Price, Scale.LowerIsBetter)], $"price {item.Price}", "Cheap", "Good price", "Cheapest option"),
        [Criterion.Comfort] = new([new Part(item.Comfort, Scale.Fixed)], "comfy", "Great reviews", "Good reviews"),
    });

    private static Preferences Prefs(int budget = 50, int comfort = 50, decimal? tripBudget = null) =>
        new(new Weights(budget, 50, 50, 50, 50, comfort), tripBudget);

    private static IReadOnlyList<Ranked<Item>> Rank(Preferences prefs, params Item[] items) => ScoringEngine.Rank(items, Describe, prefs, "USD");

    private static string[] Ids(IEnumerable<Ranked<Item>> ranked) => [.. ranked.Select(r => r.Offer.Id)];

    [Fact]
    public void Match_is_the_weighted_average_of_the_measures()
    {
        // Prices 100 and 300: the cheaper gets budget 1, the dearer 0. Comfort is fixed.
        var ranked = Rank(Prefs(budget: 90, comfort: 30), new Item("a", 1, 100, 0.2), new Item("b", 2, 300, 1));

        var a = ranked.Single(r => string.Equals(r.Offer.Id, "a", StringComparison.Ordinal)).Match;
        var b = ranked.Single(r => string.Equals(r.Offer.Id, "b", StringComparison.Ordinal)).Match;
        Assert.Equal(((90 * 1.0) + (30 * 0.2)) / 120, a.Total, 9);
        Assert.Equal(30 * 1.0 / 120, b.Total, 9);
        Assert.Equal(80, a.Percent);
        Assert.Equal(["a", "b"], Ids(ranked));
    }

    [Fact]
    public void Changing_the_weights_changes_the_order()
    {
        Item[] items = [new("cheap", 1, 100, 0.2), new("comfy", 2, 300, 1)];

        Assert.Equal(["cheap", "comfy"], Ids(Rank(Prefs(budget: 90, comfort: 20), items)));
        Assert.Equal(["comfy", "cheap"], Ids(Rank(Prefs(budget: 20, comfort: 90), items)));
    }

    [Fact]
    public void Breakdown_lists_each_part_with_its_weight_measure_and_detail()
    {
        var match = Rank(Prefs(budget: 90, comfort: 50), new Item("a", 1, 100, 0.92), new Item("b", 2, 200, 0.5))[0].Match;

        Assert.Collection(
            match.Parts,
            p => Assert.Equal((Criterion.Budget, 90, 100, true, "price 100"), (p.Criterion, p.Weight, p.Percent, p.Known, p.Detail)),
            p => Assert.Equal((Criterion.Comfort, 50, 92, true, "comfy"), (p.Criterion, p.Weight, p.Percent, p.Known, p.Detail)));
    }

    [Fact]
    public void A_preference_with_weight_zero_is_left_out()
    {
        var match = Rank(Prefs(budget: 0, comfort: 50), new Item("a", 1, 100, 0.6), new Item("b", 2, 200, 0.4))[0].Match;

        Assert.Equal([Criterion.Comfort], match.Parts.Select(p => p.Criterion));
        Assert.Equal(0.6, match.Total, 9);
    }

    [Fact]
    public void Unknown_measure_counts_as_average_and_says_so()
    {
        var match = Rank(Prefs(), new Item("a", 1, 100, null), new Item("b", 2, 200, 1)).Single(r => string.Equals(r.Offer.Id, "a", StringComparison.Ordinal)).Match;

        var comfort = match.Parts.Single(p => p.Criterion == Criterion.Comfort);
        Assert.Equal((0.5, false, "Unknown"), (comfort.Value, comfort.Known, comfort.Detail));
    }

    [Fact]
    public void Equal_values_count_as_average_so_they_neither_lift_nor_inflate()
    {
        var ranked = Rank(Prefs(budget: 90, comfort: 10), new Item("a", 1, 200, 1), new Item("b", 2, 200, 1));

        Assert.All(ranked, r => Assert.Equal(0.5, r.Match.Parts[0].Value, 9));
        Assert.Equal(["a", "b"], Ids(ranked));
    }

    [Fact]
    public void A_single_offer_counts_list_measures_as_average_but_keeps_fixed_ones()
    {
        var match = Rank(Prefs(), new Item("only", 1, 500, 0.9))[0].Match;

        Assert.Equal(0.5, match.Parts[0].Value, 9);
        Assert.Equal(0.9, match.Parts[1].Value, 9);
    }

    [Fact]
    public void Ties_keep_the_source_order_then_the_id()
    {
        var ranked = Rank(Prefs(), new Item("z", 2, 100, 0.5), new Item("y", 1, 100, 0.5), new Item("x", 2, 100, 0.5));

        Assert.Equal(["y", "x", "z"], Ids(ranked));
    }

    [Fact]
    public void Offers_over_the_trip_budget_come_last_whatever_their_match()
    {
        var ranked = Rank(Prefs(budget: 10, comfort: 90, tripBudget: 250), new Item("dear", 1, 300, 1), new Item("ok", 2, 200, 0.1));

        Assert.Equal(["ok", "dear"], Ids(ranked));
        Assert.True(ranked[1].Match.OverBudget);
        Assert.Equal("Over your $250 budget", ranked[1].Match.Reason);
        Assert.False(ranked[0].Match.OverBudget);
    }

    [Fact]
    public void Reason_uses_the_two_largest_contributions()
    {
        var ranked = Rank(Prefs(budget: 90, comfort: 50), new Item("a", 1, 100, 0.95), new Item("b", 2, 300, 0.6));

        Assert.Equal("Cheapest option, great reviews", ranked[0].Match.Reason);
        Assert.Equal("Good reviews", ranked[1].Match.Reason);
    }

    [Fact]
    public void Reason_falls_back_when_no_measure_reaches_half()
    {
        var ranked = Rank(Prefs(), new Item("a", 1, 100, 0.1), new Item("b", 2, 300, 0.2));

        Assert.Equal("Weaker on what matters most to you", ranked[1].Match.Reason);
    }

    [Fact]
    public void Same_input_gives_the_same_output()
    {
        Item[] items = [new("a", 3, 120, 0.7), new("b", 1, 90, null), new("c", 2, 150, 0.9)];

        var first = Rank(Prefs(budget: 70, comfort: 40), items);
        var second = Rank(Prefs(budget: 70, comfort: 40), items);

        Assert.Equal(first.Select(r => (r.Offer.Id, r.Match.Total, r.Match.Reason)), second.Select(r => (r.Offer.Id, r.Match.Total, r.Match.Reason)));
    }

    [Theory]
    [InlineData(new[] { 50, 50 }, false)]
    [InlineData(new[] { 0, 0 }, true)]
    public void Nothing_applies_when_every_used_weight_is_zero(int[] budgetAndComfort, bool expected)
    {
        var weights = new Weights(budgetAndComfort[0], 90, 90, 90, 90, budgetAndComfort[1]);

        Assert.Equal(expected, ScoringEngine.NothingApplies([Criterion.Budget, Criterion.Comfort], weights));
    }
}

public class PreferencesTests
{
    [Fact]
    public void Missing_weights_are_50_and_nothing_sent_is_null()
    {
        Assert.Null(Preferences.Create(null, null, null, null, null, null, null));
        Assert.Equal(new Weights(90, 50, 50, 50, 50, 50), Preferences.Create(90, null, null, null, null, null, null)!.Weights);
        Assert.Equal(1500m, Preferences.Create(null, null, null, null, null, null, 1500)!.TripBudget);
    }

    [Theory]
    [InlineData(101, null, "budget")]
    [InlineData(-1, null, "budget")]
    [InlineData(null, 0, "tripBudget")]
    public void Out_of_range_values_name_the_field(int? budget, int? tripBudget, string field)
    {
        var ex = Assert.Throws<DomainException>(() => Preferences.Create(budget, null, null, null, null, null, tripBudget));

        Assert.Equal(field, ex.Field);
    }

    [Theory]
    [InlineData("culture")]
    [InlineData("food")]
    [InlineData("nature")]
    [InlineData("nightlife")]
    [InlineData("comfort")]
    public void Each_weight_is_checked(string field)
    {
        int? Bad(string name) => string.Equals(name, field, StringComparison.Ordinal) ? 150 : null;

        var ex = Assert.Throws<DomainException>(() => Weights.Create(null, Bad("culture"), Bad("food"), Bad("nature"), Bad("nightlife"), Bad("comfort")));

        Assert.Equal(field, ex.Field);
    }
}

public class PriceTextTests
{
    [Theory]
    [InlineData(612.4, "USD", "$612")]
    [InlineData(1240, "eur", "€1,240")]
    [InlineData(1500, "AED", "1,500 AED")]
    public void Prices_are_whole_units(decimal amount, string currency, string expected) => Assert.Equal(expected, PriceText.Format(amount, currency));

    [Theory]
    [InlineData(1, "the cheapest of 20")]
    [InlineData(2, "the 2nd cheapest of 20")]
    [InlineData(3, "the 3rd cheapest of 20")]
    [InlineData(4, "the 4th cheapest of 20")]
    [InlineData(11, "the 11th cheapest of 20")]
    [InlineData(12, "the 12th cheapest of 20")]
    public void Cheapest_rank_reads_naturally(int position, string expected) => Assert.Equal(expected, PriceText.CheapestRank(position, 20));

    [Fact]
    public void Counts_are_singular_plural_or_capped()
    {
        Assert.Equal("1 top sight", PriceText.Count(1, "top sight", "top sights"));
        Assert.Equal("12 top sights", PriceText.Count(12, "top sight", "top sights"));
        Assert.Equal("500+ bars", PriceText.Count(500, "bar", "bars", 500));
    }
}
