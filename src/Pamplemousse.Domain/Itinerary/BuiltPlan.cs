using Pamplemousse.Domain.Cars;
using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Destinations;
using Pamplemousse.Domain.Flights;
using Pamplemousse.Domain.Hotels;
using Pamplemousse.Domain.Places;
using Pamplemousse.Domain.Ranking;
using Pamplemousse.Domain.TravelEvents;

namespace Pamplemousse.Domain.Itinerary;

/// <summary>
/// The offers a plan uses: the chosen hotel, flight and car, each with its match (spec 007), and the flight time to use when
/// the chosen fare has none (research R-5).
/// </summary>
public sealed record PlanOffers(
    Ranked<HotelOffer>? Hotel,
    Ranked<FlightOffer>? Flight,
    Ranked<CarOffer>? Car,
    int? FallbackFlightMinutes = null);

/// <summary>
/// The destination's places, in each source's order (best first), its events and weather, and a warning for each source
/// that failed (FR-017).
/// </summary>
public sealed record PlacePools(
    IReadOnlyList<Place> Sights,
    IReadOnlyList<Place> Nature,
    IReadOnlyList<Place> Nightlife,
    IReadOnlyList<Place> Restaurants,
    IReadOnlyList<TravelEvent> Events,
    IReadOnlyList<WeatherDay> Weather,
    IReadOnlyList<string> Warnings)
{
    public static PlacePools Empty { get; } = new([], [], [], [], [], [], []);

    public bool HasPlaces => Sights.Count + Nature.Count + Nightlife.Count + Restaurants.Count > 0;
}

public enum ItemKind
{
    Arrival,
    Departure,
    Sight,
    Nature,
    Restaurant,
    Nightlife,
    Event,
    FreeTime,
}

public enum RefKind
{
    Place,
    Event,
    Flight,
    Hotel,
}

/// <summary>A copy of what an item comes from, so a saved plan never needs the source again (FR-013).</summary>
public sealed record ItemRef(RefKind Kind, string Id, GeoPoint? Location, string? ImageUrl, string? Url);

/// <summary>One timed item of a day (FR-004). <see cref="WalkKm"/> is from the previous item, or the hotel.</summary>
public sealed record PlanItem(
    ItemKind Kind,
    TimeOnly Start,
    int Minutes,
    string Name,
    double WalkKm,
    Money? Price,
    ItemRef? Ref)
{
    public TimeOnly End => Start.AddMinutes(Minutes);
}

public sealed record PlanDay(int Number, DateOnly Date, string Title, WeatherDay? Weather, double WalkKm, IReadOnlyList<PlanItem> Items);

/// <summary>
/// What the plan costs in the trip's currency (FR-010). Parts are null when the plan has none. <see cref="Unpriced"/> counts
/// items with no known price, <see cref="OtherCurrency"/> those left out because their price is in another currency.
/// </summary>
public sealed record PlanTotal(
    decimal Amount,
    string Currency,
    decimal? Flight,
    decimal? Hotel,
    decimal? Car,
    decimal Events,
    int Unpriced,
    int OtherCurrency,
    decimal? OverBudgetBy);

public enum MatchPartKind
{
    Hotel,
    Flight,
    Car,
    Days,
}

public sealed record PlanMatchPart(MatchPartKind Part, int Weight, double Value)
{
    public int Percent => (int)Math.Round(Value * 100, MidpointRounding.AwayFromZero);
}

public sealed record DaysFitPart(Criterion Criterion, int Weight, double Value)
{
    public int Percent => (int)Math.Round(Value * 100, MidpointRounding.AwayFromZero);
}

/// <summary>The plan's match (FR-009, research R-6), with its parts and one reason in fixed wording.</summary>
public sealed record PlanMatch(double Total, IReadOnlyList<PlanMatchPart> Parts, IReadOnlyList<DaysFitPart> Days, string Reason)
{
    public int Percent => (int)Math.Round(Total * 100, MidpointRounding.AwayFromZero);
}

/// <summary>What <see cref="PlanBuilder"/> makes: the days, total, match, notes and warnings.</summary>
public sealed record BuiltPlan(
    IReadOnlyList<PlanDay> Days,
    PlanTotal Total,
    PlanMatch? Match,
    IReadOnlyList<string> Notes,
    IReadOnlyList<string> Warnings);
