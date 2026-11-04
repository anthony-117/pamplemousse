using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Application.Destinations;
using Pamplemousse.Application.Destinations.Queries.GetOverview;
using Pamplemousse.Application.Destinations.Queries.ListEvents;
using Pamplemousse.Application.Destinations.Queries.ListPlaces;
using Pamplemousse.Application.Destinations.Queries.SearchDestinations;
using Pamplemousse.Application.Flights.Queries.SearchFlights;
using Pamplemousse.Application.Hotels.Queries.SearchHotels;
using Pamplemousse.Domain.Assistant;
using Pamplemousse.Domain.Exceptions;
using Pamplemousse.Domain.Places;

namespace Pamplemousse.Application.Assistant;

/// <summary>
/// The assistant's read-only tools (spec 003, FR-004). Each one runs an existing use case, so the chat sees exactly what
/// the screens show, shares their caches and quotas, and records a card for the screen it matches (FR-003).
/// </summary>
public sealed class AssistantTools(
    IReferenceData data,
    IQueryHandler<SearchDestinationsQuery, IReadOnlyList<DestinationSummary>> searchDestinations,
    IQueryHandler<GetOverviewQuery, OverviewResponse> overview,
    IQueryHandler<ListPlacesQuery, PlacesResponse> places,
    IQueryHandler<ListEventsQuery, EventsResponse> events,
    IQueryHandler<SearchFlightsQuery, SearchFlightsResponse> flights,
    IQueryHandler<SearchHotelsQuery, SearchHotelsResponse> hotels)
{
    private readonly IReferenceData _data = data;
    private readonly IQueryHandler<SearchDestinationsQuery, IReadOnlyList<DestinationSummary>> _searchDestinations = searchDestinations;
    private readonly IQueryHandler<GetOverviewQuery, OverviewResponse> _overview = overview;
    private readonly IQueryHandler<ListPlacesQuery, PlacesResponse> _places = places;
    private readonly IQueryHandler<ListEventsQuery, EventsResponse> _events = events;
    private readonly IQueryHandler<SearchFlightsQuery, SearchFlightsResponse> _flights = flights;
    private readonly IQueryHandler<SearchHotelsQuery, SearchHotelsResponse> _hotels = hotels;

    /// <summary>Compact JSON for tool results: indentation would only cost the model tokens.</summary>
    private static readonly JsonSerializerOptions ResultJson = new(AIJsonUtilities.DefaultOptions) { WriteIndented = false };

    /// <summary>How many items of a list the model sees: enough to choose from, small enough to keep the reply fast.</summary>
    public const int MaxItems = 6;

    internal IList<AITool> For(ChatTrip trip, AssistantSession session)
    {
        var run = new Run(this, trip, session);
        return
        [
            AIFunctionFactory.Create(run.FindCityAsync, "find_city", "Finds cities by name. Returns up to 6 matches, biggest first, with the city id the other tools need.", ResultJson),
            AIFunctionFactory.Create(run.GetOverviewAsync, "get_overview", "City overview for a trip: country facts, weather for the dates, exchange rate, public holidays, visa rule for the passport and the safety advisory.", ResultJson),
            AIFunctionFactory.Create(run.ListPlacesAsync, "list_places", "Top attractions or restaurants in a city.", ResultJson),
            AIFunctionFactory.Create(run.ListEventsAsync, "list_events", "Concerts, sports and shows in or near a city during the dates.", ResultJson),
            AIFunctionFactory.Create(run.SearchFlightsAsync, "search_flights", "Recent flight prices from a departure city to a city, cheapest first. Prices are recent, not live.", ResultJson),
            AIFunctionFactory.Create(run.SearchHotelsAsync, "search_hotels", "Hotels for the dates with the total price for the stay. Uses a limited monthly quota: only call it when the traveller asks about hotels.", ResultJson),
        ];
    }

    /// <summary>The tools for one message: defaults come from the chat's trip, and results go into the session.</summary>
    private sealed class Run(AssistantTools tools, ChatTrip trip, AssistantSession session)
    {
        public Task<object> FindCityAsync(
            [Description("City name, e.g. 'Rome' or 'Paris'. At least 2 characters.")] string name,
            CancellationToken ct = default) => GuardAsync(async () =>
            {
                var found = await tools._searchDestinations.HandleAsync(new SearchDestinationsQuery(name, MaxItems), ct);
                return new
                {
                    cities = found.Select(c => new { id = c.Id, c.Name, c.Region, country = c.CountryName, c.Population }).ToList(),
                };
            });

        public Task<object> GetOverviewAsync(
            [Description("City id from find_city. Leave empty for the trip's city.")] string? cityId = null,
            [Description("Passport country code (ISO 2 letters). Leave empty for the trip's passport.")] string? passport = null,
            [Description("Currency to convert to (ISO 3 letters). Leave empty for the trip's currency.")] string? currency = null,
            [Description("First day, yyyy-MM-dd. Leave empty for the trip's dates.")] DateOnly? from = null,
            [Description("Last day, yyyy-MM-dd. Leave empty for the trip's dates.")] DateOnly? to = null,
            CancellationToken ct = default) => GuardAsync(async () =>
            {
                var (start, end) = Dates(from, to);
                var r = await tools._overview.HandleAsync(new GetOverviewQuery(City(cityId), passport ?? trip.Passport, currency ?? trip.Currency, start, end), ct);
                session.AddWarnings(r.Warnings);
                session.AddCard(new AssistantCard(
                    CardKind.Overview, r.Destination.Id, $"About {r.Destination.Name}", $"{AssistantCard.Describe(r.Dates)} · visa, weather, safety", r.Dates.From, r.Dates.To));
                return new
                {
                    city = r.Destination.Name,
                    country = r.Destination.CountryName,
                    dates = new { r.Dates.From, r.Dates.To },
                    weather = r.Weather is null ? null : new
                    {
                        kind = r.Weather.Kind.ToString(),
                        days = r.Weather.Days.Select(d => new { d.Date, maxC = d.MaxC, minC = d.MinC, d.RainChance, d.Summary }).ToList(),
                    },
                    exchange = r.Exchange is null ? null : new { from = r.Exchange.Base, to = r.Exchange.Quote, r.Exchange.Rate, r.Exchange.Date },
                    holidays = r.Holidays?.Select(h => new { h.Date, h.Name }).ToList(),
                    visa = r.Visa is null ? null : new { r.Visa.Passport, r.Visa.Requirement, r.Visa.VisaFreeDays },
                    safety = r.Advisory is null ? null : new { r.Advisory.Level, r.Advisory.Label, r.Advisory.Summary },
                    warnings = r.Warnings,
                };
            });

        public Task<object> ListPlacesAsync(
            [Description("'attraction' or 'restaurant'.")] string category,
            [Description("City id from find_city. Leave empty for the trip's city.")] string? cityId = null,
            CancellationToken ct = default) => GuardAsync(async () =>
            {
                var r = await tools._places.HandleAsync(new ListPlacesQuery(City(cityId), category, MaxItems), ct);
                session.AddWarnings(r.Warnings);
                var label = r.Category == PlaceCategory.Restaurant ? "Restaurants" : "Attractions";
                session.AddCard(new AssistantCard(
                    CardKind.Places, City(cityId), $"{label} in {CityName(cityId)}", "Top places with a map", Category: r.Category));
                return new
                {
                    category = r.Category.ToString(),
                    places = r.Items.Take(MaxItems).Select(p => new
                    {
                        p.Name,
                        p.Kind,
                        p.Rating,
                        p.RatingCount,
                        p.PriceLevel,
                        p.Address,
                        description = Shorten(p.Description, 200),
                    }).ToList(),
                    warnings = r.Warnings,
                };
            });

        public Task<object> ListEventsAsync(
            [Description("City id from find_city. Leave empty for the trip's city.")] string? cityId = null,
            [Description("First day, yyyy-MM-dd. Leave empty for the trip's dates.")] DateOnly? from = null,
            [Description("Last day, yyyy-MM-dd. Leave empty for the trip's dates.")] DateOnly? to = null,
            CancellationToken ct = default) => GuardAsync(async () =>
            {
                var (start, end) = Dates(from, to);
                var r = await tools._events.HandleAsync(new ListEventsQuery(City(cityId), start, end, MaxItems), ct);
                session.AddWarnings(r.Warnings);
                session.AddCard(new AssistantCard(
                    CardKind.Overview, City(cityId), $"Events in {CityName(cityId)}", AssistantCard.Describe(r.Dates), r.Dates.From, r.Dates.To));
                return new
                {
                    dates = new { r.Dates.From, r.Dates.To },
                    events = r.Items.Take(MaxItems).Select(e => new
                    {
                        e.Name,
                        e.Date,
                        e.Time,
                        e.Venue,
                        e.Category,
                        priceFrom = e.PriceFrom is null ? null : new { e.PriceFrom.Amount, e.PriceFrom.Currency },
                    }).ToList(),
                    warnings = r.Warnings,
                };
            });

        public Task<object> SearchFlightsAsync(
            [Description("Destination city id from find_city. Leave empty for the trip's city.")] string? cityId = null,
            [Description("Departure city id from find_city. Leave empty for the trip's departure city.")] string? originCityId = null,
            [Description("Departure day, yyyy-MM-dd. Leave empty for the trip's dates.")] DateOnly? from = null,
            [Description("Return day, yyyy-MM-dd. Leave empty for the trip's dates.")] DateOnly? to = null,
            [Description("True for a one-way flight. Default: round trip.")] bool? oneWay = null,
            [Description("True to keep only direct flights.")] bool? directOnly = null,
            CancellationToken ct = default) => GuardAsync(async () =>
            {
                var origin = !string.IsNullOrWhiteSpace(originCityId) ? originCityId.Trim()
                    : trip.OriginId ?? throw new DomainException("No departure city. Ask the traveller where they fly from, then call find_city.");
                var (start, end) = Dates(from, to);
                var tripType = oneWay == true ? "oneWay" : "roundTrip";
                var r = await tools._flights.HandleAsync(
                    new SearchFlightsQuery(City(cityId), origin, start, oneWay == true ? null : end, tripType, directOnly, trip.Currency, MaxItems), ct);
                session.AddWarnings(r.Warnings);
                var cheapest = r.Items.Count > 0 ? $" · from {r.Items.Min(i => i.Price.Amount):0} {r.Currency}" : "";
                session.AddCard(new AssistantCard(
                    CardKind.Flights, r.Destination.Id, $"Flights from {r.Origin.Name} to {r.Destination.Name}", $"{AssistantCard.Describe(r.Dates)}{cheapest}",
                    r.Dates.From, r.Dates.To, OriginId: r.Origin.Id));
                return new
                {
                    from = r.Origin.Name,
                    to = r.Destination.Name,
                    dates = new { r.Dates.From, r.Dates.To },
                    tripType = r.TripType.ToString(),
                    r.PriceNote,
                    r.Message,
                    flights = r.Items.Take(MaxItems).Select(f => new
                    {
                        f.DepartureAt,
                        f.ReturnAt,
                        f.Stops,
                        f.ReturnStops,
                        f.DurationMinutes,
                        price = new { f.Price.Amount, f.Price.Currency },
                        dateMatch = f.DateMatch.ToString(),
                    }).ToList(),
                    warnings = r.Warnings,
                };
            });

        public Task<object> SearchHotelsAsync(
            [Description("City id from find_city. Leave empty for the trip's city.")] string? cityId = null,
            [Description("Check-in day, yyyy-MM-dd. Leave empty for the trip's dates.")] DateOnly? from = null,
            [Description("Check-out day, yyyy-MM-dd. Leave empty for the trip's dates.")] DateOnly? to = null,
            [Description("Number of adults, 1 to 8. Leave empty for the trip's adults.")] int? adults = null,
            [Description("'recommended', 'price' (cheapest first), 'review', 'stars' or 'distance' (closest to the centre first).")] string? sort = null,
            [Description("Minimum stars, 1 to 5.")] int? minStars = null,
            [Description("Maximum total price for the stay, in the trip's currency.")] decimal? maxPrice = null,
            [Description("True to keep only hotels with free cancellation.")] bool? freeCancellation = null,
            CancellationToken ct = default) => GuardAsync(async () =>
            {
                var (start, end) = Dates(from, to);
                var r = await tools._hotels.HandleAsync(
                    new SearchHotelsQuery(
                        City(cityId), start, end, adults ?? trip.Adults, trip.Currency, sort, minStars, MaxPrice: maxPrice,
                        FreeCancellation: freeCancellation),
                    ct);
                session.AddWarnings(r.Warnings);
                session.AddCard(new AssistantCard(
                    CardKind.Hotels, r.Destination.Id, $"Hotels in {r.Destination.Name}", $"{AssistantCard.Describe(r.Dates)} · {AssistantCard.Describe(r.Adults)}",
                    r.Dates.From, r.Dates.To, r.Adults));
                return new
                {
                    city = r.Destination.Name,
                    dates = new { r.Dates.From, r.Dates.To },
                    r.Nights,
                    r.Adults,
                    r.Currency,
                    r.PricesFetchedAt,
                    savedPrices = r.IsSavedPrice,
                    r.TotalItems,
                    r.Message,
                    hotels = r.Items.Take(MaxItems).Select(h => new
                    {
                        h.Hotel.Name,
                        h.Hotel.StayType,
                        h.Hotel.Stars,
                        h.Hotel.ReviewScore,
                        h.Hotel.ReviewCount,
                        distanceKm = Math.Round(h.DistanceKm, 1),
                        totalPrice = h.Price.Total,
                        perNight = h.Price.PerNight,
                        h.Price.Currency,
                        h.Hotel.FreeCancellation,
                    }).ToList(),
                    warnings = r.Warnings,
                };
            });

        /// <summary>A broken rule or unknown id goes back to the model, which explains or asks again; the message never fails.</summary>
        private static async Task<object> GuardAsync(Func<Task<object>> run)
        {
            try
            {
                return await run();
            }
            catch (DomainException ex)
            {
                return new { error = ex.Message };
            }
        }

        private string City(string? cityId) => !string.IsNullOrWhiteSpace(cityId)
            ? cityId.Trim()
            : trip.DestinationId ?? throw new DomainException("No city given. Call find_city first and pass its id.");

        private string CityName(string? cityId)
        {
            var id = City(cityId);
            return tools._data.FindDestination(id)?.Name ?? id;
        }

        /// <summary>Missing dates come from the trip; a start without an end keeps the trip's length.</summary>
        private (DateOnly From, DateOnly To) Dates(DateOnly? from, DateOnly? to)
        {
            var start = from ?? trip.Dates.From;
            var end = to ?? (from is null ? trip.Dates.To : start.AddDays(trip.Dates.To.DayNumber - trip.Dates.From.DayNumber));
            return (start, end);
        }

        private static string? Shorten(string? text, int max) => text is null || text.Length <= max ? text : string.Concat(text.AsSpan(0, max), "…");
    }
}
