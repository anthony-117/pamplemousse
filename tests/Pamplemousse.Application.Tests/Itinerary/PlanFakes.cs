using Pamplemousse.Application.Abstractions;
using Pamplemousse.Application.Cars.Queries.SearchCars;
using Pamplemousse.Application.Flights.Queries.SearchFlights;
using Pamplemousse.Application.Hotels.Queries.SearchHotels;
using Pamplemousse.Application.Ranking;
using Pamplemousse.Domain.Cars;
using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Destinations;
using Pamplemousse.Domain.Flights;
using Pamplemousse.Domain.Hotels;
using Pamplemousse.Domain.Itinerary;
using Pamplemousse.Domain.Places;
using Pamplemousse.Domain.Ranking;
using Pamplemousse.Domain.Tests.Itinerary;
using Pamplemousse.Domain.Travellers;

namespace Pamplemousse.Application.Tests.Itinerary;

/// <summary>Rome, and Beirut to fly from.</summary>
internal sealed class PlanData : IReferenceData
{
    public static readonly Destination Rome = new("3169070", "Rome", "Lazio", "IT", PlanFixtures.Centre, 2_318_895, "Europe/Rome");
    public static readonly Destination Beirut = new("276781", "Beirut", "Beirut", "LB", new GeoPoint(33.89, 35.50), 1_916_100, "Asia/Beirut");

    public IReadOnlyList<Country> Countries => [];

    public IReadOnlyList<Destination> SearchDestinations(string query, int limit) => [Rome, Beirut];

    public Destination? FindDestination(string id) => new[] { Rome, Beirut }.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.Ordinal));

    public Country? FindCountry(string code) => null;

    public VisaRequirement? FindVisa(string passportCountry, string destinationCountry) => null;
}

internal static class Rankings
{
    public static RankingResult Of<T>(IEnumerable<Ranked<T>> ranked, Func<T, string> id) =>
        new(Weights.Neutral, null, ranked.ToDictionary(r => id(r.Offer), r => r.Match, StringComparer.Ordinal), null);
}

internal sealed class FakeHotelSearch : IQueryHandler<SearchHotelsQuery, SearchHotelsResponse>
{
    public static readonly DateTimeOffset FetchedAt = new(2026, 10, 27, 8, 30, 0, TimeSpan.Zero);

    public int Calls { get; private set; }

    public Exception? Error { get; set; }

    public SearchHotelsQuery? Last { get; private set; }

    public Task<SearchHotelsResponse> HandleAsync(SearchHotelsQuery query, CancellationToken ct)
    {
        Calls++;
        Last = query;
        if (Error is not null)
        {
            throw Error;
        }

        Ranked<HotelOffer>[] hotels = [PlanFixtures.Hotel("bk:1", 0.9, 500), PlanFixtures.Hotel("bk:2", 0.7, 300, at: new GeoPoint(41.92, 12.47))];
        return Task.FromResult(new SearchHotelsResponse(
            new HotelDestination("3169070", "Rome", null),
            DateRange.ForTrip(query.From, query.To, query.From!.Value),
            4,
            2,
            "USD",
            HotelSort.Match,
            1,
            2,
            1,
            FetchedAt,
            false,
            [.. hotels.Select(h => h.Offer)],
            "Booking.com",
            null,
            null,
            null,
            [],
            Rankings.Of(hotels, h => h.Hotel.Id)));
    }
}

internal sealed class FakeFlightSearch : IQueryHandler<SearchFlightsQuery, SearchFlightsResponse>
{
    public int Calls { get; private set; }

    public Task<SearchFlightsResponse> HandleAsync(SearchFlightsQuery query, CancellationToken ct)
    {
        Calls++;
        Ranked<FlightOffer>[] flights = [PlanFixtures.Flight("tp:1", 0.8), PlanFixtures.Flight("tp:2", 0.6, 150)];
        return Task.FromResult(new SearchFlightsResponse(
            new FlightEndpoint("276781", "Beirut", "Beirut", "BEY", false, null),
            new FlightEndpoint("3169070", "Rome", "Rome", "ROM", false, null),
            DateRange.ForTrip(query.From, query.To, query.From!.Value),
            TripType.RoundTrip,
            false,
            "USD",
            [.. flights.Select(f => f.Offer)],
            "per traveller",
            null,
            null,
            [],
            FlightSort.Match,
            Rankings.Of(flights, f => f.Id)));
    }
}

internal sealed class FakeCarSearch : IQueryHandler<SearchCarsQuery, SearchCarsResponse>
{
    public int Calls { get; private set; }

    public bool Fail { get; set; }

    public Task<SearchCarsResponse> HandleAsync(SearchCarsQuery query, CancellationToken ct)
    {
        Calls++;
        if (Fail)
        {
            throw new HttpRequestException("cars down");
        }

        Ranked<CarOffer>[] cars = [PlanFixtures.Car("car:1"), PlanFixtures.Car("car:2", 0.5, 90)];
        return Task.FromResult(new SearchCarsResponse(
            PlanData.Rome,
            new PickUpPlace(PickUpPlace.CentreId, PickUpKind.Centre, "Rome centre", null, PlanFixtures.Centre),
            DateRange.ForTrip(query.From, query.To, query.From!.Value),
            5,
            30,
            "USD",
            CarSort.Match,
            1,
            2,
            1,
            FakeHotelSearch.FetchedAt,
            false,
            false,
            [.. cars.Select(c => c.Offer)],
            new CarFilterOptions([], [], []),
            "https://cars.example.test",
            "Booking.com",
            null,
            null,
            null,
            [],
            Rankings.Of(cars, c => c.Id)));
    }
}

/// <summary>The fixture pools by category; <see cref="Fail"/> makes every list fail.</summary>
internal sealed class FakePlaceLists(PlaceCategory category) : IPlaceProvider
{
    public string Source => "test";

    public PlaceCategory Category => category;

    public bool Fail { get; set; }

    public int Calls { get; private set; }

    public static List<FakePlaceLists> All() =>
        [new(PlaceCategory.Attraction), new(PlaceCategory.Nature), new(PlaceCategory.Nightlife), new(PlaceCategory.Restaurant)];

    public Task<IReadOnlyList<Place>> SearchAsync(Destination destination, int limit, CancellationToken ct)
    {
        Calls++;
        if (Fail)
        {
            return Task.FromException<IReadOnlyList<Place>>(new HttpRequestException("down"));
        }

        var pools = PlanFixtures.Pools();
        return Task.FromResult(category switch
        {
            PlaceCategory.Nature => pools.Nature,
            PlaceCategory.Nightlife => pools.Nightlife,
            PlaceCategory.Restaurant => pools.Restaurants,
            _ => pools.Sights,
        });
    }

    public Task<Place?> GetAsync(string providerId, CancellationToken ct) => Task.FromResult<Place?>(null);
}

internal sealed class FakePlanStore : IPlanStore
{
    private readonly Dictionary<(string Owner, string Id), Plan> _plans = [];

    public int Saves { get; private set; }

    public Task<int> CountAsync(TravellerId owner, CancellationToken ct) =>
        Task.FromResult(_plans.Keys.Count(k => string.Equals(k.Owner, owner.Value, StringComparison.Ordinal)));

    public Task AddAsync(Plan plan, CancellationToken ct) => SaveAsync(plan, ct);

    public Task<Plan?> GetAsync(TravellerId owner, string id, CancellationToken ct) => Task.FromResult(_plans.GetValueOrDefault((owner.Value, id)));

    public Task<IReadOnlyList<PlanSummary>> ListAsync(TravellerId owner, CancellationToken ct) => Task.FromResult<IReadOnlyList<PlanSummary>>(
        [.. _plans.Where(p => string.Equals(p.Key.Owner, owner.Value, StringComparison.Ordinal)).Select(p => PlanSummary.Of(p.Value)).OrderByDescending(s => s.UpdatedAt)]);

    public Task SaveAsync(Plan plan, CancellationToken ct)
    {
        Saves++;
        _plans[(plan.Owner.Value, plan.Id)] = plan;
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(TravellerId owner, string id, CancellationToken ct) => Task.FromResult(_plans.Remove((owner.Value, id)));

    public Task DeleteAllAsync(TravellerId owner, CancellationToken ct)
    {
        foreach (var key in _plans.Keys.Where(k => string.Equals(k.Owner, owner.Value, StringComparison.Ordinal)).ToList())
        {
            _plans.Remove(key);
        }

        return Task.CompletedTask;
    }
}

internal sealed class FakeProfiles : ITravelProfileStore
{
    private readonly Dictionary<string, TravelProfile> _profiles = new(StringComparer.Ordinal);

    public Task<TravelProfile?> GetAsync(TravellerId traveller, CancellationToken ct) => Task.FromResult(_profiles.GetValueOrDefault(traveller.Value));

    public Task SaveAsync(TravelProfile profile, CancellationToken ct)
    {
        _profiles[profile.Traveller.Value] = profile;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(TravellerId traveller, CancellationToken ct)
    {
        _profiles.Remove(traveller.Value);
        return Task.CompletedTask;
    }
}
