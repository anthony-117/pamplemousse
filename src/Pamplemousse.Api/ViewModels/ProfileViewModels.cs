namespace Pamplemousse.Api.ViewModels;

/// <summary>A traveller's travel profile (spec 009, FR-009).</summary>
public sealed record ProfileViewModel(
    string DisplayName,
    string Email,
    string Passport,
    string HomeCurrency,
    HomeCityViewModel? HomeCity,
    WeightsViewModel Preferences,
    string Pace,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Where the traveller's flights leave from.</summary>
public sealed record HomeCityViewModel(string Id, string Name, string CountryCode);
