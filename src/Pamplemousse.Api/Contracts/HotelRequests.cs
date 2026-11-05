namespace Pamplemousse.Api.Contracts;

/// <summary>
/// Query string of GET /api/v1/destinations/{id}/hotels. <paramref name="StayType"/> can repeat. The preference weights (0 to 100)
/// and <paramref name="TripBudget"/> add a match to each hotel; sort=match orders by it (spec 007).
/// </summary>
public sealed record SearchHotelsRequest(
    DateOnly? From,
    DateOnly? To,
    int? Adults,
    string? Currency,
    string? Sort,
    int? MinStars,
    double? MinReview,
    decimal? MinPrice,
    decimal? MaxPrice,
    IReadOnlyList<string>? StayType,
    bool? FreeCancellation,
    int? Page,
    int? Budget,
    int? Culture,
    int? Food,
    int? Nature,
    int? Nightlife,
    int? Comfort,
    decimal? TripBudget);

/// <summary>Query string of GET /api/v1/hotels/{hotelId}.</summary>
public sealed record GetHotelRequest(DateOnly? From, DateOnly? To, int? Adults, string? Currency);
