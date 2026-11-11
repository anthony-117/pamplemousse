using System.ComponentModel;
using System.Globalization;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Application.Cars.Queries.GetCarPlaces;
using Pamplemousse.Application.Cars.Queries.SearchCars;
using Pamplemousse.Application.Destinations;
using Pamplemousse.Application.Destinations.Queries.GetOverview;
using Pamplemousse.Application.Destinations.Queries.ListEvents;
using Pamplemousse.Application.Destinations.Queries.ListPlaces;
using Pamplemousse.Application.Destinations.Queries.SearchDestinations;
using Pamplemousse.Application.Flights.Queries.SearchFlights;
using Pamplemousse.Application.Hotels.Queries.SearchHotels;
using Pamplemousse.Application.Ranking;
using Pamplemousse.Domain.Destinations;
using Pamplemousse.Domain.Exceptions;
using Pamplemousse.Domain.Places;

namespace Pamplemousse.Application.Assistant.Reply;

/// <summary>
/// The search tools (spec 010 FR-008 to FR-011): the same use cases, caches and quotas as the screens, ranked by the
/// traveller's preferences unless they ask for another order. Each one reports a step with its cards.
/// </summary>
public sealed class SearchTools(
    IReferenceData data,
    ICardJson cards,
    IQueryHandler<SearchDestinationsQuery, IReadOnlyList<DestinationSummary>> searchDestinations,
    IQueryHandler<GetOverviewQuery, OverviewResponse> overview,
    IQueryHandler<ListPlacesQuery, PlacesResponse> places,
    IQueryHandler<ListEventsQuery, EventsResponse> events,
    IQueryHandler<SearchFlightsQuery, SearchFlightsResponse> flights,
    IQueryHandler<SearchHotelsQuery, SearchHotelsResponse> hotels,
    IQueryHandler<GetCarPlacesQuery, GetCarPlacesResponse> carPlaces,
    IQueryHandler<SearchCarsQuery, SearchCarsResponse> cars)
{
    private const int Max = AssistantTools.MaxItems;

    internal IReferenceData Data { get; } = data;

    internal ICardJson Cards { get; } = cards;

    internal IQueryHandler<SearchDestinationsQuery, IReadOnlyList<DestinationSummary>> Destinations { get; } = searchDestinations;

    internal IQueryHandler<GetOverviewQuery, OverviewResponse> Overview { get; } = overview;

    internal IQueryHandler<ListPlacesQuery, PlacesResponse> Places { get; } = places;

    internal IQueryHandler<ListEventsQuery, EventsResponse> Events { get; } = events;

    internal IQueryHandler<SearchFlightsQuery, SearchFlightsResponse> Flights { get; } = flights;

    internal IQueryHandler<SearchHotelsQuery, SearchHotelsResponse> Hotels { get; } = hotels;

    internal IQueryHandler<GetCarPlacesQuery, GetCarPlacesResponse> CarPlaces { get; } = carPlaces;

    internal IQueryHandler<SearchCarsQuery, SearchCarsResponse> Cars { get; } = cars;

    internal Run For(ReplyRun run) => new(this, run);

    internal sealed class Run(SearchTools t, ReplyRun run)
    {
        public Task<object> FindCityAsync(
            [Description("City name, at least 2 characters.")] string name,
            CancellationToken ct = default) => Tool.RunAsync(run, "find_city", "Cities", async () =>
            {
                var found = await t.Destinations.HandleAsync(new SearchDestinationsQuery(name, Max), ct);
                run.Step("find_city", string.Create(CultureInfo.InvariantCulture, $"Looked up “{name.Trim()}” · {found.Count} found"), found.Count > 0, found.Count);
                return new { cities = found.Select(c => new { id = c.Id, c.Name, country = c.CountryName, c.Population }).ToList() };
            });

        public Task<object> GetOverviewAsync(
            [Description("City id from find_city; empty for the trip's city.")] string? cityId = null,
            [Description("Passport country, 2 letters; empty for the trip's.")] string? passport = null,
            [Description("First day, yyyy-MM-dd; empty for the trip's.")] DateOnly? from = null,
            [Description("Last day, yyyy-MM-dd; empty for the trip's.")] DateOnly? to = null,
            CancellationToken ct = default) => Tool.RunAsync(run, "get_overview", "Travel info", async () =>
            {
                var city = City(cityId);
                var (start, end) = Dates(from, to);
                var r = await t.Overview.HandleAsync(new GetOverviewQuery(city.Id, passport ?? run.Job.Trip.Passport, run.Job.Trip.Currency, start, end), ct);
                run.Warn(r.Warnings);
                run.Step("get_overview", $"Checked visa, weather and safety for {city.Name}", true, null, [t.Cards.Facts(r, city, run.Now)]);
                return new
                {
                    city = city.Name,
                    weather = r.Weather?.Days.Take(7).Select(d => new { d.Date, d.MaxC, d.MinC, d.Summary }).ToList(),
                    exchange = r.Exchange is null ? null : new { from = r.Exchange.Base, to = r.Exchange.Quote, r.Exchange.Rate },
                    holidays = r.Holidays?.Take(5).Select(h => new { h.Date, h.Name }).ToList(),
                    visa = r.Visa is null ? null : new { r.Visa.Requirement, r.Visa.VisaFreeDays },
                    safety = r.Advisory is null ? null : new { r.Advisory.Level, r.Advisory.Label },
                    warnings = r.Warnings,
                };
            });

        public Task<object> ListPlacesAsync(
            [Description("'sight', 'restaurant', 'nature' or 'nightlife'.")] string category,
            [Description("City id from find_city; empty for the trip's city.")] string? cityId = null,
            CancellationToken ct = default) => Tool.RunAsync(run, "list_places", "Places", async () =>
            {
                var city = City(cityId);
                var asked = string.Equals(category?.Trim(), "sight", StringComparison.OrdinalIgnoreCase) ? "attraction" : category;
                var r = await t.Places.HandleAsync(new ListPlacesQuery(city.Id, asked, Max * 2), ct);
                run.Warn(r.Warnings);
                var top = r.Items.Take(Max).ToList();
                var label = r.Category switch
                {
                    PlaceCategory.Restaurant => "restaurants",
                    PlaceCategory.Nature => "nature places",
                    PlaceCategory.Nightlife => "places for an evening out",
                    _ => "sights",
                };
                run.Step("list_places", top.Count > 0 ? $"Found top {label} in {city.Name}" : $"No {label} found in {city.Name}", top.Count > 0 || r.Warnings.Count == 0, r.Items.Count, t.Cards.Places(top, city, run.Now));
                return new { places = top.Select(p => new { p.Id, p.Name, p.Kind, p.Rating, p.PriceLevel }).ToList(), warnings = r.Warnings };
            });

        public Task<object> ListEventsAsync(
            [Description("City id from find_city; empty for the trip's city.")] string? cityId = null,
            [Description("First day, yyyy-MM-dd; empty for the trip's.")] DateOnly? from = null,
            [Description("Last day, yyyy-MM-dd; empty for the trip's.")] DateOnly? to = null,
            CancellationToken ct = default) => Tool.RunAsync(run, "list_events", "Events", async () =>
            {
                var city = City(cityId);
                var (start, end) = Dates(from, to);
                var r = await t.Events.HandleAsync(new ListEventsQuery(city.Id, start, end, Max * 2), ct);
                run.Warn(r.Warnings);
                var top = r.Items.Take(Max).ToList();
                var ok = top.Count > 0 || r.Warnings.Count == 0;
                run.Step("list_events", ok ? string.Create(CultureInfo.InvariantCulture, $"Found {r.Items.Count} events in {city.Name}") : "Events unavailable right now", ok, r.Items.Count, t.Cards.Events(top, city, run.Now));
                return new { events = top.Select(e => new { e.Id, e.Name, e.Date, e.Time, e.Venue, price = e.PriceFrom?.Amount, e.PriceFrom?.Currency }).ToList(), warnings = r.Warnings };
            });

        public Task<object> SearchFlightsAsync(
            [Description("Destination city id; empty for the trip's city.")] string? cityId = null,
            [Description("Departure city id from find_city; empty for the trip's departure city.")] string? originCityId = null,
            [Description("Departure day, yyyy-MM-dd; empty for the trip's.")] DateOnly? from = null,
            [Description("Return day, yyyy-MM-dd; empty for the trip's.")] DateOnly? to = null,
            [Description("True for one way.")] bool? oneWay = null,
            [Description("'match' (best for the traveller, default) or 'price' (cheapest first).")] string? sort = null,
            CancellationToken ct = default) => Tool.RunAsync(run, "search_flights", "Flights", async () =>
            {
                var city = City(cityId);
                var origin = !string.IsNullOrWhiteSpace(originCityId) ? originCityId.Trim()
                    : run.Job.Trip.OriginId ?? throw new DomainException("No departure city. Ask where they fly from, then call find_city.");
                var (start, end) = Dates(from, to);
                var r = await t.Flights.HandleAsync(
                    new SearchFlightsQuery(city.Id, origin, start, oneWay == true ? null : end, oneWay == true ? "oneWay" : "roundTrip", null, run.Job.Trip.Currency, 20, Sort(sort), Ranking()), ct);
                run.Warn(r.Warnings);
                var ok = r.Items.Count > 0 || r.Warnings.Count == 0;
                run.Step(
                    "search_flights",
                    ok ? string.Create(CultureInfo.InvariantCulture, $"Searched flights {r.Origin.Name} → {r.Destination.Name} · {r.Items.Count} fares") : "Flights unavailable right now",
                    ok,
                    r.Items.Count,
                    t.Cards.Flights(r, Max, city, run.Now));
                return new
                {
                    from = r.Origin.Name,
                    to = r.Destination.Name,
                    r.PriceNote,
                    r.Message,
                    flights = r.Items.Take(Max).Select(f => new
                    {
                        f.Id,
                        f.Airline,
                        f.DepartureAt,
                        f.ReturnAt,
                        f.Stops,
                        f.DurationMinutes,
                        price = f.Price.Amount,
                        f.Price.Currency,
                        match = r.Ranking?.Matches.GetValueOrDefault(f.Id)?.Percent,
                    }).ToList(),
                    warnings = r.Warnings,
                };
            });

        public Task<object> SearchHotelsAsync(
            [Description("City id; empty for the trip's city.")] string? cityId = null,
            [Description("Check-in, yyyy-MM-dd; empty for the trip's.")] DateOnly? from = null,
            [Description("Check-out, yyyy-MM-dd; empty for the trip's.")] DateOnly? to = null,
            [Description("Adults, 1 to 8; empty for the trip's.")] int? adults = null,
            [Description("'match' (default), 'price', 'review', 'stars' or 'distance'.")] string? sort = null,
            [Description("Minimum stars, 1 to 5.")] int? minStars = null,
            [Description("Maximum total for the stay.")] decimal? maxPrice = null,
            [Description("True for free cancellation only.")] bool? freeCancellation = null,
            CancellationToken ct = default) => Tool.RunAsync(run, "search_hotels", "Hotels", async () =>
            {
                var city = City(cityId);
                var (start, end) = Dates(from, to);
                var r = await t.Hotels.HandleAsync(
                    new SearchHotelsQuery(
                        city.Id, start, end, adults ?? run.Job.Trip.Adults, run.Job.Trip.Currency, Sort(sort), minStars, MaxPrice: maxPrice,
                        FreeCancellation: freeCancellation, Ranking: Ranking()),
                    ct);
                run.Warn(r.Warnings);
                var ok = r.Items.Count > 0 || r.Warnings.Count == 0;
                var ranked = string.Equals(Sort(sort), "match", StringComparison.Ordinal) ? ", ranked for you" : "";
                run.Step(
                    "search_hotels",
                    ok ? string.Create(CultureInfo.InvariantCulture, $"Searched hotels in {city.Name} · {r.TotalItems} found{ranked}") : "Hotels unavailable right now",
                    ok,
                    r.TotalItems,
                    t.Cards.Hotels(r, Max, city, run.Now));
                return new
                {
                    city = city.Name,
                    r.Nights,
                    r.Currency,
                    r.TotalItems,
                    r.Message,
                    hotels = r.Items.Take(Max).Select(h => new
                    {
                        h.Hotel.Id,
                        h.Hotel.Name,
                        h.Hotel.Stars,
                        h.Hotel.ReviewScore,
                        distanceKm = Math.Round(h.DistanceKm, 1),
                        total = h.Price.Total,
                        perNight = h.Price.PerNight,
                        match = r.Ranking?.Matches.GetValueOrDefault(h.Hotel.Id)?.Percent,
                    }).ToList(),
                    warnings = r.Warnings,
                };
            });

        public Task<object> SearchCarsAsync(
            [Description("City id; empty for the trip's city.")] string? cityId = null,
            [Description("Pick-up day, yyyy-MM-dd; empty for the trip's.")] DateOnly? from = null,
            [Description("Return day, yyyy-MM-dd; empty for the trip's.")] DateOnly? to = null,
            [Description("'centre' (default) or an airport's 3-letter code.")] string? pickUp = null,
            [Description("Driver's age, 18 to 99; empty for 30.")] int? driverAge = null,
            [Description("'match' (default), 'price' or 'rating'.")] string? sort = null,
            CancellationToken ct = default) => Tool.RunAsync(run, "search_cars", "Cars", async () =>
            {
                var city = City(cityId);
                var (start, end) = Dates(from, to);
                var place = await PlaceAsync(city.Id, pickUp, ct);
                var r = await t.Cars.HandleAsync(
                    new SearchCarsQuery(city.Id, start, end, place, driverAge, run.Job.Trip.Currency, Sort(sort), Ranking: Ranking()), ct);
                run.Warn(r.Warnings);
                var ok = r.Items.Count > 0 || r.Warnings.Count == 0;
                run.Step(
                    "search_cars",
                    ok ? string.Create(CultureInfo.InvariantCulture, $"Searched cars in {city.Name} · {r.TotalItems} found") : "Cars unavailable right now",
                    ok,
                    r.TotalItems,
                    t.Cards.Cars(r, Max, city, run.Now));
                return new
                {
                    pickUp = r.Place.Name,
                    r.Days,
                    r.Currency,
                    r.TotalItems,
                    cars = r.Items.Take(Max).Select(c => new
                    {
                        c.Id,
                        c.Model,
                        c.Size,
                        c.Seats,
                        company = c.Company.Name,
                        total = c.Price.Total,
                        c.FreeCancellation,
                        match = r.Ranking?.Matches.GetValueOrDefault(c.Id)?.Percent,
                    }).ToList(),
                    warnings = r.Warnings,
                };
            });

        private async Task<string?> PlaceAsync(string cityId, string? pickUp, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(pickUp) || string.Equals(pickUp.Trim(), "centre", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var known = await t.CarPlaces.HandleAsync(new GetCarPlacesQuery(cityId), ct);
            return known.Places.FirstOrDefault(p => string.Equals(p.Iata, pickUp.Trim(), StringComparison.OrdinalIgnoreCase) || string.Equals(p.Id, pickUp.Trim(), StringComparison.Ordinal))?.Id
                ?? throw new DomainException($"No pick-up at '{pickUp}'. Use 'centre' or one of: {string.Join(", ", known.Places.Select(p => p.Iata ?? p.Id))}.");
        }

        private CardCity City(string? cityId) => Tool.City(t.Data, cityId, run);

        private (DateOnly From, DateOnly To) Dates(DateOnly? from, DateOnly? to) => Tool.Dates(run, from, to);

        private RankingInput Ranking()
        {
            var w = run.Job.Weights;
            return new RankingInput(w.Budget, w.Culture, w.Food, w.Nature, w.Nightlife, w.Comfort, run.Job.TripBudget);
        }

        private static string Sort(string? sort) => string.IsNullOrWhiteSpace(sort) ? "match" : sort.Trim().ToLowerInvariant();
    }
}

/// <summary>What every tool shares: running safely, the trip's city and dates.</summary>
internal static class Tool
{
    /// <summary>
    /// A broken rule or unknown id goes back to the model, which explains or asks again; a source that fails is a failed
    /// step ("Hotels unavailable right now") and the answer goes on (spec Edge Cases). Nothing here logs the arguments.
    /// </summary>
    public static async Task<object> RunAsync(ReplyRun run, string tool, string label, Func<Task<object>> work)
    {
        try
        {
            return await work();
        }
        catch (DomainException ex)
        {
            return new { error = ex.Message };
        }
        catch (Exception ex) when (ex is ServiceUnavailableException or HttpRequestException or TimeoutException or InvalidOperationException)
        {
            run.Step(tool, $"{label} unavailable right now", false);
            return new { error = $"{label} are unavailable right now." };
        }
    }

    public static CardCity City(IReferenceData data, string? cityId, ReplyRun run)
    {
        var id = !string.IsNullOrWhiteSpace(cityId) ? cityId.Trim()
            : run.Job.Trip.DestinationId ?? throw new DomainException("No city given. Call find_city first and pass its id.");
        var city = data.FindDestination(id) ?? throw new DomainException($"Unknown city id '{id}'. Call find_city.");
        return Of(city);
    }

    public static CardCity Of(Destination city) => new(city.Id, city.Name, city.Location);

    /// <summary>Missing dates come from the trip; a start without an end keeps the trip's length, else 5 days.</summary>
    public static (DateOnly From, DateOnly To) Dates(ReplyRun run, DateOnly? from, DateOnly? to)
    {
        var trip = run.Job.Trip;
        var tripFrom = trip.From ?? run.Today.AddDays(30);
        var tripTo = trip.To ?? tripFrom.AddDays(4);
        var start = from ?? tripFrom;
        var end = to ?? (from is null ? tripTo : start.AddDays(tripTo.DayNumber - tripFrom.DayNumber));
        return (start, end);
    }
}
