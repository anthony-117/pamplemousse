using Pamplemousse.Domain.Cars;
using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Flights;
using Pamplemousse.Domain.Hotels;
using Pamplemousse.Domain.Ranking;
using Pamplemousse.Domain.Tests.Cars;

namespace Pamplemousse.Domain.Tests.Ranking;

public class HotelRankingTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);

    private static HotelOffer Hotel(int rank, decimal total, int? stars = 4, double? review = 8.5) => new(
        new Hotel { Id = $"bk:{rank}", Name = $"Hotel {rank}", Location = new GeoPoint(41.9, 12.49), Stars = stars, ReviewScore = review },
        new HotelPrice(total, total / 5, "USD", new DateOnly(2027, 4, 10), new DateOnly(2027, 4, 15), 1, Now),
        rank,
        1,
        NearCity: false);

    private static Preferences Only(Criterion high) => new(
        new Weights(
            high == Criterion.Budget ? 90 : 20,
            high == Criterion.Culture ? 90 : 20,
            high == Criterion.Food ? 90 : 20,
            high == Criterion.Nature ? 90 : 20,
            high == Criterion.Nightlife ? 90 : 20,
            high == Criterion.Comfort ? 90 : 20),
        null);

    private static readonly HotelOffer[] Offers = [Hotel(1, 600), Hotel(2, 450), Hotel(3, 700)];

    private static readonly Dictionary<string, Surroundings> Around = new(StringComparer.Ordinal)
    {
        ["bk:1"] = new(12, 392, 43, 21),
        ["bk:2"] = new(3, 31, 8, 2),
        ["bk:3"] = new(6, 200, 20, 10),
    };

    private static string[] Ids(IEnumerable<Ranked<HotelOffer>> ranked) => [.. ranked.Select(r => r.Offer.Hotel.Id)];

    [Fact]
    public void Culture_high_lifts_the_hotel_with_the_most_sights_nearby()
    {
        var ranked = HotelRanking.Rank(Offers, Around, Only(Criterion.Culture), "USD");

        Assert.Equal("bk:1", ranked[0].Offer.Hotel.Id);
        Assert.StartsWith("Near 12 sights", ranked[0].Match.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Budget_high_puts_the_cheapest_first_when_the_surroundings_are_alike()
    {
        var alike = new Dictionary<string, Surroundings>(StringComparer.Ordinal) { ["bk:1"] = new(5, 100, 10, 5), ["bk:2"] = new(5, 100, 10, 5), ["bk:3"] = new(5, 100, 10, 5) };

        var ranked = HotelRanking.Rank(Offers, alike, Only(Criterion.Budget), "USD");

        Assert.Equal(["bk:2", "bk:1", "bk:3"], Ids(ranked));
        Assert.Equal("$450, the cheapest of 3", ranked[0].Match.Parts.Single(p => p.Criterion == Criterion.Budget).Detail);
    }

    [Fact]
    public void Every_preference_appears_in_a_hotel_breakdown_with_what_was_counted()
    {
        var match = HotelRanking.Rank(Offers, Around, Only(Criterion.Food), "USD").Single(r => string.Equals(r.Offer.Hotel.Id, "bk:1", StringComparison.Ordinal)).Match;

        Assert.Equal(Enum.GetValues<Criterion>(), match.Parts.Select(p => p.Criterion));
        Assert.Equal("12 top sights within 1.5 km", match.Parts[1].Detail);
        Assert.Equal("392 restaurants and cafés within 1 km", match.Parts[2].Detail);
        Assert.Equal("21 parks and natural sites within 2 km", match.Parts[3].Detail);
        Assert.Equal("43 bars and pubs within 1 km", match.Parts[4].Detail);
        Assert.Equal("Reviews 8.5 / 10 · 4 stars", match.Parts[5].Detail);
        Assert.Equal(100, match.Parts[2].Percent);
    }

    [Fact]
    public void Unknown_surroundings_count_as_average()
    {
        var ranked = HotelRanking.Rank(Offers, new Dictionary<string, Surroundings>(StringComparer.Ordinal), Only(Criterion.Culture), "USD");

        Assert.All(ranked, r => Assert.All(r.Match.Parts.Where(p => p.Criterion is Criterion.Culture or Criterion.Food or Criterion.Nature or Criterion.Nightlife), p => Assert.False(p.Known)));
    }

    [Fact]
    public void A_hotel_with_nothing_nearby_scores_zero_on_that_preference()
    {
        var around = new Dictionary<string, Surroundings>(Around, StringComparer.Ordinal) { ["bk:2"] = new(0, 0, 0, 0) };

        var match = HotelRanking.Rank(Offers, around, Only(Criterion.Nature), "USD").Single(r => string.Equals(r.Offer.Hotel.Id, "bk:2", StringComparison.Ordinal)).Match;

        Assert.Equal(0, match.Parts.Single(p => p.Criterion == Criterion.Nature).Value);
    }

    [Fact]
    public void Comfort_uses_what_is_known()
    {
        HotelOffer[] offers = [Hotel(1, 500, stars: null, review: 9.0), Hotel(2, 500, stars: 5, review: null), Hotel(3, 500, stars: null, review: null)];

        var parts = HotelRanking.Rank(offers, Around, Only(Criterion.Comfort), "USD")
            .ToDictionary(r => r.Offer.Hotel.Id, r => r.Match.Parts.Single(p => p.Criterion == Criterion.Comfort), StringComparer.Ordinal);

        Assert.Equal(0.9, parts["bk:1"].Value, 9);
        Assert.Equal(1, parts["bk:2"].Value, 9);
        Assert.False(parts["bk:3"].Known);
    }
}

public class FlightRankingTests
{
    private static FlightOffer Flight(string id, decimal price, int stops, int? back, int? minutes, DateMatch dates = DateMatch.Exact) => new(
        id, "ITA Airways", null, "BEY", "FCO", new DateTimeOffset(2027, 4, 10, 8, 40, 0, TimeSpan.Zero), null, stops, back, minutes, new Money(price, "USD"), "https://example.com", dates, "Aviasales");

    private static readonly FlightOffer[] Offers =
    [
        Flight("cheap", 241, 1, 1, 335),
        Flight("direct", 289, 0, 0, 225),
        Flight("late", 268, 1, 1, 400, DateMatch.Nearby),
    ];

    [Fact]
    public void Comfort_high_puts_direct_flights_first()
    {
        var ranked = FlightRanking.Rank(Offers, new Preferences(new Weights(20, 90, 90, 90, 90, 90), null), "USD");

        Assert.Equal("direct", ranked[0].Offer.Id);
        Assert.Equal("Direct, short journey", ranked[0].Match.Reason);
        Assert.Equal("Direct both ways · 3h 45m", ranked[0].Match.Parts[1].Detail);
    }

    [Fact]
    public void Only_budget_and_comfort_count_for_flights()
    {
        var match = FlightRanking.Rank(Offers, new Preferences(Weights.Neutral, null), "USD")[0].Match;

        Assert.Equal([Criterion.Budget, Criterion.Comfort], match.Parts.Select(p => p.Criterion));
    }

    [Fact]
    public void Budget_high_puts_the_cheapest_first_and_says_so()
    {
        var ranked = FlightRanking.Rank(Offers, new Preferences(new Weights(90, 50, 50, 50, 50, 20), null), "USD");

        Assert.Equal("cheap", ranked[0].Offer.Id);
        Assert.StartsWith("Cheapest option", ranked[0].Match.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Other_dates_and_stops_are_described()
    {
        var match = FlightRanking.Rank(Offers, new Preferences(Weights.Neutral, null), "USD").Single(r => string.Equals(r.Offer.Id, "late", StringComparison.Ordinal)).Match;

        Assert.Equal("1 stop each way · 6h 40m · other dates", match.Parts[1].Detail);
    }
}

public class CarRankingTests
{
    [Fact]
    public void Comfort_high_lifts_larger_automatic_well_rated_cars()
    {
        CarOffer[] cars =
        [
            CarTestData.Car(1, 60, size: "Mini", gearbox: Gearbox.Manual, rating: 6.0, freeCancellation: false),
            CarTestData.Car(2, 90, size: "Standard", gearbox: Gearbox.Automatic, rating: 8.5),
        ];

        var ranked = CarRanking.Rank(cars, new Preferences(new Weights(20, 50, 50, 50, 50, 90), null), "USD");

        Assert.Equal("bk:2", ranked[0].Offer.Id);
        Assert.Equal("Roomy, automatic", ranked[0].Match.Reason);
        Assert.Equal("Standard · automatic · Avis 8.5 / 10 · free cancellation", ranked[0].Match.Parts[1].Detail);
        Assert.Equal([Criterion.Budget, Criterion.Comfort], ranked[0].Match.Parts.Select(p => p.Criterion));
    }

    [Fact]
    public void Unknown_size_and_gearbox_use_what_is_known()
    {
        CarOffer[] cars = [CarTestData.Car(1, 60, size: "Special", gearbox: null, rating: 8.0, freeCancellation: true)];

        var comfort = CarRanking.Rank(cars, new Preferences(Weights.Neutral, null), "USD")[0].Match.Parts[1];

        Assert.Equal((0.8 + 1) / 2, comfort.Value, 9);
    }
}
