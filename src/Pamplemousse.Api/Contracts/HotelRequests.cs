namespace Pamplemousse.Api.Contracts;

/// <summary>Query string of GET /api/v1/destinations/{id}/hotels. <paramref name="StayType"/> can repeat.</summary>
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
    int? Page);

/// <summary>Query string of GET /api/v1/hotels/{hotelId}.</summary>
public sealed record GetHotelRequest(DateOnly? From, DateOnly? To, int? Adults, string? Currency);
