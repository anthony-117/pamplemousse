using System.Globalization;
using Pamplemousse.Domain.Cars;
using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Destinations;
using Pamplemousse.Domain.Flights;
using Pamplemousse.Domain.Hotels;
using Pamplemousse.Domain.Itinerary;
using Pamplemousse.Domain.Places;
using Pamplemousse.Domain.Ranking;
using Pamplemousse.Domain.TravelEvents;

namespace Pamplemousse.Domain.Tests.Itinerary;

/// <summary>Hand-made inputs around central Rome, the same on every run.</summary>
internal static class PlanFixtures
{
    public static readonly GeoPoint Centre = new(41.9000, 12.4900);
    public static readonly DateOnly Today = new(2027, 3, 1);
    public static readonly DateOnly From = new(2027, 4, 10);

    public static PlanDestination Rome { get; } = new("3169070", "Rome", "IT", Centre);

    public static TripRequest Trip(
        int days = 5,
        Weights? weights = null,
        Pace pace = Pace.Balanced,
        string? origin = "276781",
        decimal? budget = null,
        int adults = 2,
        string currency = "USD") =>
        TripRequest.Create(
            Rome,
            DateRange.ForTrip(From, From.AddDays(days - 1), Today),
            adults,
            currency,
            origin,
            weights ?? Weights.Neutral,
            pace,
            budget,
            TimeSpan.FromHours(2));

    public static Weights W(int budget = 50, int culture = 50, int food = 50, int nature = 50, int nightlife = 50, int comfort = 50) =>
        new(budget, culture, food, nature, nightlife, comfort);

    /// <summary>Spread over about 4 × 4 km around the centre, in a fixed pattern.</summary>
    public static GeoPoint Spot(int i, int salt) => new(
        Centre.Lat + ((((i * 37) + salt) % 21) - 10) * 0.0018,
        Centre.Lng + ((((i * 53) + (salt * 3)) % 21) - 10) * 0.0024);

    public static Place Place(string prefix, int i, PlaceCategory category, int salt) => new(
        $"{prefix}:{i.ToString(CultureInfo.InvariantCulture)}",
        $"{prefix} {i.ToString(CultureInfo.InvariantCulture)}",
        category,
        Spot(i, salt),
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        [],
        "test");

    public static PlacePools Pools(int sights = 40, int nature = 30, int nightlife = 20, int restaurants = 80, IReadOnlyList<TravelEvent>? events = null, IReadOnlyList<WeatherDay>? weather = null) => new(
        [.. Enumerable.Range(1, sights).Select(i => Place("sight", i, PlaceCategory.Attraction, 1))],
        [.. Enumerable.Range(1, nature).Select(i => Place("park", i, PlaceCategory.Nature, 5))],
        [.. Enumerable.Range(1, nightlife).Select(i => Place("bar", i, PlaceCategory.Nightlife, 9))],
        [.. Enumerable.Range(1, restaurants).Select(i => Place("food", i, PlaceCategory.Restaurant, 13))],
        events ?? [],
        weather ?? [],
        []);

    public static Match M(double total) => new(total, [], "test", false);

    public static Ranked<HotelOffer> Hotel(string id = "bk:1", double match = 0.9, decimal total = 500, string currency = "USD", GeoPoint? at = null) =>
        new(
            new HotelOffer(
                new Hotel { Id = id, Name = "Hotel " + id, Location = at ?? Centre },
                new HotelPrice(total, total / 4, currency, From, From.AddDays(4), 2, new DateTimeOffset(2027, 3, 1, 10, 0, 0, TimeSpan.Zero)),
                1,
                0.5,
                true),
            M(match));

    /// <summary>Leaves Beirut at 08:00 local (UTC+2), lands at 10:30 Rome time; back at 18:00 Rome time.</summary>
    public static Ranked<FlightOffer> Flight(string id = "tp:1", double match = 0.8, decimal price = 200, int? minutes = 150, int days = 5, bool returns = true) =>
        new(
            new FlightOffer(
                id,
                "ME",
                "ME231",
                "BEY",
                "FCO",
                new DateTimeOffset(From.Year, From.Month, From.Day, 8, 0, 0, TimeSpan.FromHours(2)),
                returns ? new DateTimeOffset(From.AddDays(days - 1).ToDateTime(new TimeOnly(18, 0)), TimeSpan.FromHours(2)) : null,
                0,
                returns ? 0 : null,
                minutes,
                new Money(price, "USD"),
                "https://example.test/f",
                DateMatch.Exact,
                "test"),
            M(match));

    public static Ranked<CarOffer> Car(string id = "car:1", double match = 0.7, decimal total = 150) =>
        new(
            new CarOffer(
                id,
                "Fiat 500",
                null,
                "Mini",
                null,
                Gearbox.Manual,
                4,
                3,
                1,
                true,
                null,
                true,
                null,
                new RentalCompany("Rent", null, 8, 100),
                new PickUpPoint("Desk", PickUpType.Downtown, null, Centre, 0.1),
                new CarPrice(total, total / 5, "USD", false),
                true,
                null,
                null,
                false,
                1,
                null),
            M(match));

    public static PlanOffers Offers(bool flight = true, bool hotel = true, int days = 5) =>
        new(hotel ? Hotel() : null, flight ? Flight(days: days) : null, null);

    public static TravelEvent Event(string id, DateOnly date, string? time, decimal? price = null, string currency = "USD") =>
        new(id, "Event " + id, date, time, "Venue", "Music", price is { } p ? new Money(p, currency) : null, null, "https://example.test/e", "test");

    public static int Minutes(TimeOnly t) => (t.Hour * 60) + t.Minute;

    public static bool IsPlace(PlanItem item) => item.Kind is ItemKind.Sight or ItemKind.Nature;
}
