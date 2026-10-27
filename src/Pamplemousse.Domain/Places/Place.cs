using Pamplemousse.Domain.Common;

namespace Pamplemousse.Domain.Places;

public enum PlaceCategory
{
    Attraction,
    Restaurant,
}

/// <summary>An attraction or restaurant. <see cref="Id"/> is "{source}:{providerId}", e.g. "otm:W12345".</summary>
public sealed record Place(
    string Id,
    string Name,
    PlaceCategory Category,
    GeoPoint Location,
    string? Kind,
    double? Rating,
    int? RatingCount,
    string? PriceLevel,
    string? Address,
    string? Description,
    string? ImageUrl,
    string? Website,
    IReadOnlyList<string> OpeningHours,
    string Source);
