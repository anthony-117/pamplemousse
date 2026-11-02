using Pamplemousse.Domain.Places;

namespace Pamplemousse.Api.ViewModels;

public sealed record PlacesViewModel(PlaceCategory Category, IReadOnlyList<PlaceViewModel> Items, IReadOnlyList<string> Warnings);

public sealed record PlaceViewModel(
    string Id,
    string Name,
    PlaceCategory Category,
    GeoPointViewModel Location,
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

public sealed record GeoPointViewModel(double Lat, double Lng);
