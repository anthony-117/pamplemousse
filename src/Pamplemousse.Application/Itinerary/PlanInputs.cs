using Microsoft.Extensions.Logging;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Application.Cars.Queries.SearchCars;
using Pamplemousse.Application.Common;
using Pamplemousse.Application.Destinations.Queries.ListPlaces;
using Pamplemousse.Application.Flights.Queries.SearchFlights;
using Pamplemousse.Application.Hotels.Queries.SearchHotels;
using Pamplemousse.Application.Ranking;
using Pamplemousse.Domain.Cars;
using Pamplemousse.Domain.Destinations;
using Pamplemousse.Domain.Flights;
using Pamplemousse.Domain.Hotels;
using Pamplemousse.Domain.Itinerary;
using Pamplemousse.Domain.Places;
using Pamplemousse.Domain.Ranking;
using Pamplemousse.Domain.TravelEvents;

namespace Pamplemousse.Application.Itinerary;

/// <summary>The kept offers of a new plan and the places to build its days from.</summary>
public sealed record GatheredInputs(PlanAlternatives Alternatives, PlacePools Pools);

/// <summary>The kept cars of a plan, with the source and booking page their list shows.</summary>
public sealed record GatheredCars(IReadOnlyList<Ranked<CarOffer>> Cars, string Source, string BookingUrl);

/// <summary>
/// Gathers what <see cref="PlanBuilder"/> needs (spec 008, FR-002 and FR-017). The hotel, flight and car lists come from the
/// ranked searches of spec 007, the same stored searches a list screen uses, so a plan costs no extra paid call when the
/// traveller already looked. Places, events and weather are free and cached. Every source that fails becomes a warning.
/// </summary>
public sealed class PlanInputs(
    IReferenceData data,
    IQueryHandler<SearchHotelsQuery, SearchHotelsResponse> hotels,
    IQueryHandler<SearchFlightsQuery, SearchFlightsResponse> flights,
    IQueryHandler<SearchCarsQuery, SearchCarsResponse> cars,
    IEnumerable<IPlaceProvider> places,
    IEventProvider events,
    IWeatherProvider weather,
    TimeProvider clock,
    ILogger<PlanInputs> logger)
{
    /// <summary>Enough for 30 Packed days of 4 places, from lists that are cached per limit.</summary>
    public const int PlaceLimit = 120;

    /// <summary>Two meals a day for 30 days, and a top quarter of well-known places to choose from at Food High.</summary>
    public const int RestaurantLimit = 200;

    public const int EventLimit = 50;

    private static readonly Match NotRanked = new(ScoringEngine.Average, [], "Not ranked.", false);

    public async Task<GatheredInputs> GatherAsync(TripRequest trip, Destination destination, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(trip);
        var sections = new SectionLoader(logger);
        var ranking = RankingOf(trip);

        var hotelTask = sections.LoadAsync("Hotels", () => hotels.HandleAsync(
            new SearchHotelsQuery(destination.Id, trip.Dates.From, trip.Dates.To, trip.Adults, trip.Currency, Sort: "match", Ranking: ranking), ct));
        var flightTask = trip.OriginId is null
            ? Task.FromResult<SearchFlightsResponse?>(null)
            : sections.LoadAsync("Flights", () => flights.HandleAsync(
                new SearchFlightsQuery(destination.Id, trip.OriginId, trip.Dates.From, trip.Dates.To, null, null, trip.Currency, null, Sort: "match", Ranking: ranking), ct));
        var poolTask = LoadPoolsAsync(trip, destination, sections, ct);

        await Task.WhenAll(hotelTask, flightTask, poolTask);
        var hotelList = await hotelTask;
        var flightList = await flightTask;
        var now = clock.GetUtcNow();

        var alternatives = new PlanAlternatives(
            Combine(hotelList?.Items ?? [], hotelList?.Ranking, (HotelOffer h) => h.Hotel.Id),
            Combine(flightList?.Items ?? [], flightList?.Ranking, (FlightOffer f) => f.Id),
            null,
            hotelList is null ? null : hotelList.PricesFetchedAt ?? now,
            flightList is null ? null : now,
            null,
            hotelList?.Source);
        var pools = await poolTask;
        return new GatheredInputs(alternatives, pools with { Warnings = [.. sections.Warnings.Distinct(StringComparer.Ordinal)] });
    }

    /// <summary>The places again, for a rebuild after a swap (research R-7). Null when none could be loaded.</summary>
    public async Task<PlacePools?> PlacesAsync(TripRequest trip, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(trip);
        var destination = data.FindDestination(trip.Destination.Id);
        if (destination is null)
        {
            return null;
        }

        var sections = new SectionLoader(logger);
        var pools = await LoadPoolsAsync(trip, destination, sections, ct);
        return pools.HasPlaces ? pools with { Warnings = [.. sections.Warnings.Distinct(StringComparer.Ordinal)] } : null;
    }

    /// <summary>Runs the ranked car search once, as opening the Cars tab would (research R-7).</summary>
    public async Task<GatheredCars> CarsAsync(TripRequest trip, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(trip);
        var sections = new SectionLoader(logger);
        var list = await sections.LoadAsync("Cars", () => cars.HandleAsync(
            new SearchCarsQuery(trip.Destination.Id, trip.Dates.From, trip.Dates.To, Currency: trip.Currency, Sort: "match", Ranking: RankingOf(trip)), ct))
            ?? throw new ServiceUnavailableException(sections.Warnings.Count > 0 ? sections.Warnings[0] : "Cars are unavailable right now.");

        return new GatheredCars(Combine(list.Items, list.Ranking, (CarOffer c) => c.Id), list.Source, list.BookingUrl);
    }

    /// <summary>The destination's offset from UTC on the trip's first day, so flight times read as local times.</summary>
    public static TimeSpan UtcOffsetOf(Destination destination, DateOnly day)
    {
        ArgumentNullException.ThrowIfNull(destination);
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(destination.TimeZone);
            return zone.GetUtcOffset(day.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc));
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        {
            return TimeSpan.Zero;
        }
    }

    internal static RankingInput RankingOf(TripRequest trip)
    {
        var w = trip.Weights;
        return new RankingInput(w.Budget, w.Culture, w.Food, w.Nature, w.Nightlife, w.Comfort, trip.TripBudget);
    }

    /// <summary>
    /// The top of a list with each offer's match. A list with nothing to rank on (its weights are all 0) has no matches;
    /// its offers count as average, and its part of the plan's match weighs 0 anyway.
    /// </summary>
    internal static IReadOnlyList<Ranked<T>> Combine<T>(IReadOnlyList<T> items, RankingResult? ranking, Func<T, string> id) =>
        [.. items.Take(PlanAlternatives.Max).Select(i => new Ranked<T>(i, ranking?.Matches.GetValueOrDefault(id(i)) ?? NotRanked))];

    private async Task<PlacePools> LoadPoolsAsync(TripRequest trip, Destination destination, SectionLoader sections, CancellationToken ct)
    {
        var sights = Places(PlaceCategory.Attraction, PlaceLimit);
        var nature = Places(PlaceCategory.Nature, PlaceLimit);
        var nightlife = Places(PlaceCategory.Nightlife, PlaceLimit);
        var restaurants = Places(PlaceCategory.Restaurant, RestaurantLimit);
        var eventTask = sections.LoadAsync("Events", () => events.SearchAsync(destination, trip.Dates.From, trip.Dates.To, EventLimit, ct));
        var weatherTask = sections.LoadAsync("Weather", () => weather.GetAsync(destination.Location, trip.Dates.From, trip.Dates.To, ct));

        await Task.WhenAll(sights, nature, nightlife, restaurants, eventTask, weatherTask);
        return new PlacePools(
            await sights ?? [],
            await nature ?? [],
            await nightlife ?? [],
            await restaurants ?? [],
            await eventTask ?? (IReadOnlyList<TravelEvent>)[],
            (await weatherTask)?.Days ?? [],
            []);

        Task<IReadOnlyList<Place>?> Places(PlaceCategory category, int limit)
        {
            var provider = places.FirstOrDefault(p => p.Category == category);
            return provider is null
                ? Task.FromResult<IReadOnlyList<Place>?>(null)
                : sections.LoadAsync(ListPlacesHandler.SectionName(category), () => provider.SearchAsync(destination, limit, ct));
        }
    }
}
