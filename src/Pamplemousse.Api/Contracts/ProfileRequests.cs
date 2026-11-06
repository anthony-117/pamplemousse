namespace Pamplemousse.Api.Contracts;

/// <summary>
/// The whole profile form (contracts/accounts-api.md). Each field is checked by Domain, which names a missing one.
/// <see cref="Pace"/> is relaxed, balanced or packed, balanced when left out (spec 008).
/// </summary>
public sealed record UpdateProfileRequest(
    string? DisplayName,
    string? Passport,
    string? HomeCurrency,
    string? HomeCityId,
    PreferenceWeightsRequest? Preferences,
    string? Pace = null);

/// <summary>The six default preferences, 0 to 100 each.</summary>
public sealed record PreferenceWeightsRequest(int? Budget, int? Culture, int? Food, int? Nature, int? Nightlife, int? Comfort);
