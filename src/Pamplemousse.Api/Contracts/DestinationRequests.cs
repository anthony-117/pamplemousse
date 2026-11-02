namespace Pamplemousse.Api.Contracts;

/// <summary>Query string of GET /destinations.</summary>
public sealed record SearchDestinationsRequest(string? Q, int? Limit);

/// <summary>Query string of GET /destinations/{id}/overview.</summary>
public sealed record GetOverviewRequest(string? Passport, string? Currency, DateOnly? From, DateOnly? To);

/// <summary>Query string of GET /destinations/{id}/places.</summary>
public sealed record ListPlacesRequest(string? Category, int? Limit);

/// <summary>Query string of GET /destinations/{id}/events.</summary>
public sealed record ListEventsRequest(DateOnly? From, DateOnly? To, int? Limit);

/// <summary>Query string of GET /destinations/{id}/flights. <paramref name="Origin"/> is required: the id of the departure city.</summary>
public sealed record SearchFlightsRequest(string Origin, DateOnly? From, DateOnly? To, string? TripType, bool? DirectOnly, string? Currency, int? Limit);
