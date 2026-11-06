using System.Globalization;
using Pamplemousse.Domain.Flights;

namespace Pamplemousse.Domain.Ranking;

/// <summary>Turns flights into candidates: budget, and comfort from stops, journey time and how close the dates are (research R-3).</summary>
public static class FlightRanking
{
    public static IReadOnlyList<Criterion> Used { get; } = [Criterion.Budget, Criterion.Comfort];

    public static IReadOnlyList<Ranked<FlightOffer>> Rank(IReadOnlyList<FlightOffer> offers, Preferences preferences, string currency)
    {
        ArgumentNullException.ThrowIfNull(offers);
        var prices = offers.Select(o => o.Price.Amount).ToList();

        // Ties keep the source's order (cheapest first).
        var order = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < offers.Count; i++)
        {
            order.TryAdd(offers[i].Id, i + 1);
        }

        return ScoringEngine.Rank(offers, o => Describe(o, order[o.Id], prices), preferences, currency);
    }

    private static Candidate Describe(FlightOffer offer, int sourceRank, List<decimal> prices)
    {
        var price = offer.Price.Amount;
        var direct = offer.Stops == 0 && offer.ReturnStops is null or 0;
        var comfort = new List<Part> { new(StopsMeasure(offer.Stops), Scale.Fixed) };
        if (offer.ReturnStops is { } back)
        {
            comfort.Add(new Part(StopsMeasure(back), Scale.Fixed));
        }

        comfort.Add(new Part(DatesMeasure(offer.DateMatch), Scale.Fixed));
        comfort.Add(new Part(offer.DurationMinutes, Scale.ShareOfLeast));

        var criteria = new Dictionary<Criterion, CriterionInput>
        {
            [Criterion.Budget] = new(
                [new Part((double)price, Scale.LowerIsBetter)],
                $"{PriceText.Format(price, offer.Price.Currency)}, {PriceText.CheapestRank(PriceText.PositionOf(price, prices), prices.Count)}",
                "Cheap",
                "Good price",
                "Cheapest option"),
            [Criterion.Comfort] = new(
                comfort,
                ComfortDetail(offer),
                direct ? "Direct, short journey" : "Short journey",
                direct ? "Direct" : "Fair journey"),
        };
        return new Candidate(offer.Id, sourceRank, price, criteria);
    }

    private static double StopsMeasure(int stops) => stops switch
    {
        <= 0 => 1,
        1 => 0.5,
        _ => 0,
    };

    private static double DatesMeasure(DateMatch match) => match switch
    {
        DateMatch.Exact => 1,
        DateMatch.Nearby => 0.7,
        DateMatch.SameMonth => 0.4,
        _ => 0.2,
    };

    private static string ComfortDetail(FlightOffer offer)
    {
        var parts = new List<string> { Stops(offer.Stops, offer.ReturnStops) };
        if (offer.DurationMinutes is { } minutes)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{minutes / 60}h {minutes % 60:00}m"));
        }

        if (offer.DateMatch != DateMatch.Exact)
        {
            parts.Add("other dates");
        }

        return string.Join(" · ", parts);
    }

    private static string Stops(int outbound, int? back) => back switch
    {
        null => Leg(outbound),
        var b when b == outbound => outbound == 0 ? "Direct both ways" : $"{Leg(outbound)} each way",
        var b => $"{Leg(outbound)} out, {Leg(b.Value).ToLowerInvariant()} back",
    };

    private static string Leg(int stops) => stops switch
    {
        <= 0 => "Direct",
        1 => "1 stop",
        _ => $"{stops.ToString(CultureInfo.InvariantCulture)} stops",
    };
}
