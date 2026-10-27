using Microsoft.AspNetCore.Http.HttpResults;
using Pamplemousse.Domain.Abstractions;
using Pamplemousse.Domain.Places;

namespace Pamplemousse.Api.Features.Destinations;

public static class DestinationEndpoints
{
    private const int MaxTripDays = 30;

    public static IEndpointRouteBuilder MapDestinationEndpoints(this IEndpointRouteBuilder routes)
    {
        var destinations = routes.MapGroup("/destinations").WithTags("Destinations");

        destinations.MapGet("/", Search)
            .WithName("SearchDestinations")
            .WithSummary("City search for autocomplete (at least 2 characters).");

        destinations.MapGet("/{id}", GetDestination)
            .WithName("GetDestination");

        destinations.MapGet("/{id}/overview", GetOverviewAsync)
            .WithName("GetDestinationOverview")
            .WithSummary("Country facts, weather, exchange rate, holidays, visa and safety for the trip dates.");

        destinations.MapGet("/{id}/places", GetPlacesAsync)
            .WithName("GetDestinationPlaces")
            .WithSummary("Top attractions or restaurants (OpenTripMap).");

        destinations.MapGet("/{id}/events", GetEventsAsync)
            .WithName("GetDestinationEvents")
            .WithSummary("Events near the city during the trip dates (Ticketmaster).");

        routes.MapGet("/places/{id}", GetPlaceAsync)
            .WithTags("Destinations")
            .WithName("GetPlace")
            .WithSummary("Place details by id, e.g. otm:W123.");

        routes.MapGet("/countries", ListCountries)
            .WithTags("Destinations")
            .WithName("ListCountries")
            .WithSummary("Countries for passport and currency pickers.");

        return routes;
    }

    internal static Ok<IReadOnlyList<DestinationSummary>> Search(string? q, int? limit, IReferenceData data)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
        {
            return TypedResults.Ok<IReadOnlyList<DestinationSummary>>([]);
        }

        IReadOnlyList<DestinationSummary> results = [.. data.SearchDestinations(q, Math.Clamp(limit ?? 10, 1, 25))
            .Select(d => DestinationSummary.From(d, data.FindCountry(d.CountryCode)))];
        return TypedResults.Ok(results);
    }

    internal static Results<Ok<DestinationDetails>, NotFound> GetDestination(string id, IReferenceData data)
    {
        if (data.FindDestination(id) is not { } d)
        {
            return TypedResults.NotFound();
        }

        var country = data.FindCountry(d.CountryCode);
        return TypedResults.Ok(new DestinationDetails(DestinationSummary.From(d, country), country));
    }

    internal static async Task<Results<Ok<OverviewResponse>, NotFound, ValidationProblem>> GetOverviewAsync(
        string id, string? passport, string? currency, DateOnly? from, DateOnly? to,
        IReferenceData data, DestinationService service, TimeProvider clock, CancellationToken ct)
    {
        if (data.FindDestination(id) is not { } d)
        {
            return TypedResults.NotFound();
        }

        if (ResolveDates(from, to, clock) is not { } dates)
        {
            return DatesProblem();
        }

        var quote = string.IsNullOrWhiteSpace(currency) ? "USD" : currency;
        return TypedResults.Ok(await service.GetOverviewAsync(d, dates, passport, quote, ct));
    }

    internal static async Task<Results<Ok<PlacesResponse>, NotFound, ValidationProblem>> GetPlacesAsync(
        string id, string? category, int? limit, IReferenceData data, DestinationService service, CancellationToken ct)
    {
        if (data.FindDestination(id) is not { } d)
        {
            return TypedResults.NotFound();
        }

        // Accept the same camelCase names the API returns ("attraction", "restaurant").
        var kind = PlaceCategory.Attraction;
        if (category is not null && (!Enum.TryParse(category, ignoreCase: true, out kind) || !Enum.IsDefined(kind)))
        {
            return Problem("category", "Use 'attraction' or 'restaurant'.");
        }

        return TypedResults.Ok(await service.GetPlacesAsync(d, kind, Math.Clamp(limit ?? 20, 1, 20), ct));
    }

    internal static async Task<Results<Ok<EventsResponse>, NotFound, ValidationProblem>> GetEventsAsync(
        string id, DateOnly? from, DateOnly? to, int? limit, IReferenceData data, DestinationService service, TimeProvider clock, CancellationToken ct)
    {
        if (data.FindDestination(id) is not { } d)
        {
            return TypedResults.NotFound();
        }

        return ResolveDates(from, to, clock) is { } dates
            ? TypedResults.Ok(await service.GetEventsAsync(d, dates, Math.Clamp(limit ?? 20, 1, 50), ct))
            : DatesProblem();
    }

    internal static async Task<Results<Ok<Place>, NotFound<string>>> GetPlaceAsync(string id, DestinationService service, CancellationToken ct)
    {
        var (place, warning) = await service.GetPlaceAsync(id, ct);
        return place is not null ? TypedResults.Ok(place) : TypedResults.NotFound(warning ?? "Place not found.");
    }

    internal static Ok<IReadOnlyList<CountryOption>> ListCountries(IReferenceData data) =>
        TypedResults.Ok<IReadOnlyList<CountryOption>>([.. data.Countries.Select(c => new CountryOption(c.Code, c.Name, c.Flag, [.. c.Currencies.Select(x => x.Code)]))]);

    /// <summary>Defaults to a 5-day trip starting in 30 days; rejects ranges that end before they start, exceed 30 days or are in the past.</summary>
    internal static TripDates? ResolveDates(DateOnly? from, DateOnly? to, TimeProvider clock)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var start = from ?? today.AddDays(30);
        var end = to ?? start.AddDays(4);
        return end < start || end.DayNumber - start.DayNumber > MaxTripDays || start < today.AddDays(-1) ? null : new TripDates(start, end);
    }

    private static ValidationProblem DatesProblem() =>
        Problem("to", $"Dates must be in the future, 'to' on or after 'from', and at most {MaxTripDays} days apart.");

    private static ValidationProblem Problem(string field, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });
}
