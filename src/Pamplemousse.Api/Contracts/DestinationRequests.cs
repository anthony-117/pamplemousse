namespace Pamplemousse.Api.Contracts;

/// <summary>Query string of GET /destinations.</summary>
public sealed record SearchDestinationsRequest(string? Q, int? Limit);

/// <summary>Query string of GET /destinations/{id}/overview.</summary>
public sealed record GetOverviewRequest(string? Passport, string? Currency, DateOnly? From, DateOnly? To);

/// <summary>Query string of GET /destinations/{id}/places.</summary>
public sealed record ListPlacesRequest(string? Category, int? Limit);

/// <summary>Query string of GET /destinations/{id}/events.</summary>
public sealed record ListEventsRequest(DateOnly? From, DateOnly? To, int? Limit);

/// <summary>
/// Query string of GET /destinations/{id}/flights. <paramref name="Origin"/> is required: the id of the departure city.
/// <paramref name="Sort"/> is "cheapest" (the default) or "match"; the preferences add a match to each flight (spec 007).
/// Culture, food, nature and nightlife are accepted so the app can send one set, but only count for hotels.
/// </summary>
public sealed record SearchFlightsRequest(
    string Origin,
    DateOnly? From,
    DateOnly? To,
    string? TripType,
    bool? DirectOnly,
    string? Currency,
    int? Limit,
    string? Sort,
    int? Budget,
    int? Culture,
    int? Food,
    int? Nature,
    int? Nightlife,
    int? Comfort,
    decimal? TripBudget);
