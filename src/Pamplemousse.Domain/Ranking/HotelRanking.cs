using System.Globalization;
using Pamplemousse.Domain.Hotels;

namespace Pamplemousse.Domain.Ranking;

/// <summary>
/// What is within walking distance of a hotel (spec 007, FR-003). Null counts are unknown. A count of
/// <see cref="Cap"/> means that many or more (research R-1).
/// </summary>
public sealed record Surroundings(int? Sights, int? Food, int? Nightlife, int? Nature)
{
    public const int Cap = 500;
    public const double SightsKm = 1.5;
    public const double FoodKm = 1;
    public const double NightlifeKm = 1;
    public const double NatureKm = 2;

    public static Surroundings Unknown { get; } = new(null, null, null, null);
}

/// <summary>Turns hotels into candidates: budget and comfort, plus culture, food, nature and nightlife from what is nearby.</summary>
public static class HotelRanking
{
    public static IReadOnlyList<Criterion> Used { get; } = Enum.GetValues<Criterion>();

    /// <summary>Ranks hotels. <paramref name="around"/> holds each hotel's surroundings by hotel id; a missing hotel counts as unknown.</summary>
    public static IReadOnlyList<Ranked<HotelOffer>> Rank(
        IReadOnlyList<HotelOffer> offers, IReadOnlyDictionary<string, Surroundings> around, Preferences preferences, string currency)
    {
        ArgumentNullException.ThrowIfNull(offers);
        ArgumentNullException.ThrowIfNull(around);
        var prices = offers.Select(o => o.Price.Total).ToList();
        return ScoringEngine.Rank(offers, o => Describe(o, around.GetValueOrDefault(o.Hotel.Id) ?? Surroundings.Unknown, prices), preferences, currency);
    }

    private static Candidate Describe(HotelOffer offer, Surroundings around, List<decimal> prices)
    {
        var hotel = offer.Hotel;
        var price = offer.Price.Total;
        var criteria = new Dictionary<Criterion, CriterionInput>
        {
            [Criterion.Budget] = new(
                [new Part((double)price, Scale.LowerIsBetter)],
                $"{PriceText.Format(price, offer.Price.Currency)}, {PriceText.CheapestRank(PriceText.PositionOf(price, prices), prices.Count)}",
                "Cheap",
                "Good price",
                "Cheapest option"),
            [Criterion.Culture] = Nearby(around.Sights, "top sight", "top sights", Surroundings.SightsKm, around.Sights == 1 ? "Near a top sight" : $"Near {around.Sights} sights", "Some sights nearby", int.MaxValue),
            [Criterion.Food] = Nearby(around.Food, "restaurant or café", "restaurants and cafés", Surroundings.FoodKm, "Lots of food nearby", "Food nearby"),
            [Criterion.Nature] = Nearby(around.Nature, "park or natural site", "parks and natural sites", Surroundings.NatureKm, "Near parks", "A park nearby"),
            [Criterion.Nightlife] = Nearby(around.Nightlife, "bar or pub", "bars and pubs", Surroundings.NightlifeKm, "Lively at night", "Some bars nearby"),
            [Criterion.Comfort] = new(
                [new Part(hotel.ReviewScore / 10, Scale.Fixed), new Part(hotel.Stars / 5.0, Scale.Fixed)],
                ComfortDetail(hotel),
                "Great reviews",
                "Good reviews"),
        };
        return new Candidate(hotel.Id, offer.Rank, price, criteria);
    }

    private static CriterionInput Nearby(int? count, string one, string many, double km, string high, string middle, int cap = Surroundings.Cap)
    {
        var within = $"within {km.ToString("0.#", CultureInfo.InvariantCulture)} km";
        var detail = count is { } n ? $"{PriceText.Count(n, one, many, cap)} {within}" : "Unknown";
        return new([new Part(count, Scale.ShareOfMost)], detail, high, middle);
    }

    private static string ComfortDetail(Hotel hotel)
    {
        var parts = new List<string>();
        if (hotel.ReviewScore is { } score)
        {
            parts.Add($"Reviews {score.ToString("0.0", CultureInfo.InvariantCulture)} / 10");
        }

        if (hotel.Stars is { } stars)
        {
            parts.Add(stars == 1 ? "1 star" : $"{stars.ToString(CultureInfo.InvariantCulture)} stars");
        }

        return parts.Count > 0 ? string.Join(" · ", parts) : "Unknown";
    }
}
