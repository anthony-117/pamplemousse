using Pamplemousse.Domain.Hotels;

namespace Pamplemousse.Api.ViewModels;

public sealed record HotelSearchViewModel(
    HotelDestinationViewModel Destination,
    StayDatesViewModel Dates,
    int Adults,
    string Currency,
    HotelSort Sort,
    int Page,
    int TotalItems,
    int TotalPages,
    DateTimeOffset? PricesFetchedAt,
    bool IsSavedPrice,
    IReadOnlyList<HotelViewModel> Items,
    int? FiltersCheckedOn,
    string? Message,
    string? SearchUrl,
    IReadOnlyList<string> Warnings);

public sealed record HotelDestinationViewModel(string Id, string Name, string? SourceName);

public sealed record StayDatesViewModel(DateOnly From, DateOnly To, int Nights);

public sealed record HotelViewModel(
    string Id,
    string Name,
    string? PhotoUrl,
    int? Stars,
    double? ReviewScore,
    int? ReviewCount,
    GeoPointViewModel Location,
    double DistanceKm,
    bool NearCity,
    string? StayType,
    bool? FreeCancellation,
    StayPriceViewModel Price,
    string Source);

public sealed record StayPriceViewModel(decimal Total, decimal PerNight, string Currency);

public sealed record HotelDetailsViewModel(
    string Id,
    string Name,
    IReadOnlyList<string> PhotoUrls,
    int? Stars,
    bool? StarsEstimated,
    double? ReviewScore,
    int? ReviewCount,
    string? ReviewWord,
    GeoPointViewModel Location,
    string? Address,
    string? District,
    double? DistanceToCentreKm,
    string? StayType,
    bool? FreeCancellation,
    bool? BreakfastIncluded,
    string? CheckInFrom,
    string? CheckInUntil,
    string? CheckOutFrom,
    string? CheckOutUntil,
    IReadOnlyList<string> Facilities,
    IReadOnlyList<string> Languages,
    StayPriceViewModel? Price,
    DateTimeOffset? PriceFetchedAt,
    string BookingUrl,
    DateTimeOffset? DetailsFetchedAt,
    string Source,
    IReadOnlyList<string> Warnings);
