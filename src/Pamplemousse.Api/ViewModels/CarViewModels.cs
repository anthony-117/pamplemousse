using Pamplemousse.Domain.Cars;

namespace Pamplemousse.Api.ViewModels;

public sealed record CarPlacesViewModel(IReadOnlyList<CarPlaceViewModel> Places, IReadOnlyList<string> Warnings);

public sealed record CarPlaceViewModel(string Id, PickUpKind Kind, string Name, string? Iata, GeoPointViewModel Location);

public sealed record CarSearchViewModel(
    CarDestinationViewModel Destination,
    CarSearchPlaceViewModel Place,
    RentalDatesViewModel Dates,
    int DriverAge,
    string Currency,
    CarSort Sort,
    int Page,
    int TotalItems,
    int TotalPages,
    DateTimeOffset? PricesFetchedAt,
    bool IsSavedPrice,
    bool MayHaveMore,
    IReadOnlyList<CarViewModel> Items,
    CarFilterOptionsViewModel FilterOptions,
    string? DriverNote,
    string? Message,
    string? SearchUrl,
    IReadOnlyList<string> Warnings);

public sealed record CarDestinationViewModel(string Id, string Name);

public sealed record CarSearchPlaceViewModel(string Id, PickUpKind Kind, string Name, string? Iata);

public sealed record RentalDatesViewModel(DateOnly From, DateOnly To, int Days, string PickUpTime, string ReturnTime);

public sealed record CarViewModel(
    string Id,
    string Model,
    string? OrSimilar,
    string Size,
    string? PhotoUrl,
    Gearbox? Gearbox,
    int? Seats,
    int? Doors,
    int? Bags,
    bool? AirCon,
    string? Mileage,
    bool? UnlimitedMileage,
    string? FuelPolicy,
    RentalCompanyViewModel Company,
    CarPickUpViewModel PickUp,
    CarPriceViewModel Price,
    bool? FreeCancellation,
    MoneyViewModel? Deposit,
    MoneyViewModel? DamageExcess,
    bool Sponsored,
    string BookingUrl,
    string Source);

public sealed record RentalCompanyViewModel(string Name, string? LogoUrl, double? Rating, int? ReviewCount);

public sealed record CarPickUpViewModel(string Name, PickUpType Type, string? Address, GeoPointViewModel Location, double DistanceKm);

public sealed record CarPriceViewModel(decimal Total, decimal PerDay, string Currency, bool PayNow);

public sealed record CarFilterOptionsViewModel(
    IReadOnlyList<FilterOptionViewModel> Sizes,
    IReadOnlyList<FilterOptionViewModel> Companies,
    IReadOnlyList<FilterOptionViewModel> PickUpTypes);

public sealed record FilterOptionViewModel(string Value, int Count);
