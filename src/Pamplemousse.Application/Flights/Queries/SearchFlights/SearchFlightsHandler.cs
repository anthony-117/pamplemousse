using System.Globalization;
using Microsoft.Extensions.Logging;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Application.Common;
using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Destinations;
using Pamplemousse.Domain.Exceptions;
using Pamplemousse.Domain.Flights;

namespace Pamplemousse.Application.Flights.Queries.SearchFlights;

/// <summary><paramref name="TripType"/> is "roundTrip" (the default) or "oneWay", in any case.</summary>
public sealed record SearchFlightsQuery(
    string DestinationId,
    string OriginId,
    DateOnly? From,
    DateOnly? To,
    string? TripType,
    bool? DirectOnly,
    string? Currency,
    int? Limit);

/// <summary>A TravelGenie city and the flight city that serves it. <see cref="IsNearby"/> means a different place was used.</summary>
public sealed record FlightEndpoint(string Id, string Name, string? FlightCity, string? Code, bool IsNearby, double? DistanceKm);

public sealed record SearchFlightsResponse(
    FlightEndpoint Origin,
    FlightEndpoint Destination,
    DateRange Dates,
    TripType TripType,
    bool DirectOnly,
    string Currency,
    IReadOnlyList<FlightOffer> Items,
    string PriceNote,
    string? Message,
    string? SearchUrl,
    IReadOnlyList<string> Warnings);

public sealed class SearchFlightsHandler(
    IReferenceData data,
    IFlightCities flightCities,
    IFlightProvider flights,
    TimeProvider clock,
    ILogger<SearchFlightsHandler> logger) : IQueryHandler<SearchFlightsQuery, SearchFlightsResponse>
{
    private const string PriceNote = "Recent prices found by other travellers. The live price is on the booking site.";
    private const double SamePlaceKm = 5;

    public async Task<SearchFlightsResponse> HandleAsync(SearchFlightsQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var destination = data.FindDestination(query.DestinationId) ?? throw new NotFoundException($"Destination '{query.DestinationId}' was not found.");
        var origin = data.FindDestination(query.OriginId) ?? throw new NotFoundException($"Origin '{query.OriginId}' was not found.");
        var dates = DateRange.ForTrip(query.From, query.To, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));

        var tripType = TripType.RoundTrip;
        if (query.TripType is not null && (!Enum.TryParse(query.TripType, ignoreCase: true, out tripType) || !Enum.IsDefined(tripType)))
        {
            throw new DomainException("Use 'roundTrip' or 'oneWay'.", "tripType");
        }

        var directOnly = query.DirectOnly ?? false;
        var originMatch = flightCities.FindNearest(origin.Location);
        var destinationMatch = flightCities.FindNearest(destination.Location);
        var from = ToEndpoint(origin, originMatch);
        var to = ToEndpoint(destination, destinationMatch);

        if (originMatch is null || destinationMatch is null)
        {
            var city = originMatch is null ? origin.Name : destination.Name;
            return new SearchFlightsResponse(
                from, to, dates, tripType, directOnly, query.Currency?.Trim().ToUpperInvariant() ?? FlightSearch.DefaultCurrency, [], PriceNote,
                $"{city} has no airport nearby, so no flights can be shown.", null, []);
        }

        var search = FlightSearch.Create(originMatch.City.Code, destinationMatch.City.Code, dates, tripType, directOnly, query.Currency);
        var sections = new SectionLoader(logger);
        var results = await sections.LoadAsync("Flights", () => flights.SearchAsync(search, ct));
        foreach (var warning in results?.Warnings ?? [])
        {
            sections.AddWarning(warning);
        }

        IReadOnlyList<FlightOffer> items = [.. (results?.Offers ?? []).Take(Math.Clamp(query.Limit ?? 20, 1, 50))];
        return new SearchFlightsResponse(
            from, to, dates, tripType, directOnly, search.Currency, items, PriceNote,
            results is null ? null : MessageFor(items),
            results is not null && items.Count == 0 ? results.SearchUrl : null,
            sections.Warnings);
    }

    private static FlightEndpoint ToEndpoint(Destination city, CityMatch? match) =>
        match is null
            ? new FlightEndpoint(city.Id, city.Name, null, null, IsNearby: false, DistanceKm: null)
            : new FlightEndpoint(
                city.Id,
                city.Name,
                match.City.Name,
                match.City.Code,
                match.DistanceKm > SamePlaceKm && !SameName(city.Name, match.City.Name),
                match.DistanceKm);

    private static bool SameName(string a, string b) =>
        CultureInfo.InvariantCulture.CompareInfo.Compare(a, b, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) == 0;

    private static string? MessageFor(IReadOnlyList<FlightOffer> items) => items.Count == 0
        ? "No recent prices for this route in these months. Search the booking site for live prices."
        : items[0].DateMatch switch
        {
            DateMatch.Nearby => "No prices on your exact dates. Showing dates within 3 days.",
            DateMatch.SameMonth => "No prices within 3 days of your dates. Showing other dates in the same month.",
            DateMatch.AnyReturn => "No prices returning in the month you asked for. Showing trips that depart that month and return on other dates.",
            _ => null,
        };
}
