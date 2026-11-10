using Pamplemousse.Domain.Exceptions;
using Pamplemousse.Domain.Itinerary;
using Pamplemousse.Domain.Ranking;

namespace Pamplemousse.Domain.Travellers;

/// <summary>
/// What a traveller keeps between trips and devices (spec 009, FR-009): new trips start from it, and changing a trip
/// never changes it (FR-012). The email comes from the sign-in and can't be changed here.
/// </summary>
public sealed record TravelProfile(
    TravellerId Traveller,
    string DisplayName,
    string Email,
    string Passport,
    string HomeCurrency,
    string? HomeCityId,
    Weights Preferences,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public const int MaxNameLength = 60;
    public const string DefaultPassport = "LB";
    public const string DefaultCurrency = "USD";
    private const string FallbackName = "Traveller";

    /// <summary>How full a new plan's days are (spec 008, FR-006).</summary>
    public Pace Pace { get; init; } = Pace.Balanced;

    /// <summary>The profile made on a first sign-in (FR-010): the sign-in's name, else the start of the email, and the app's defaults.</summary>
    public static TravelProfile New(TravellerId traveller, string? name, string? email, DateTimeOffset now)
    {
        var fromEmail = email?.Split('@')[0];
        var display = FirstUsable(name) ?? FirstUsable(fromEmail) ?? FallbackName;
        return new TravelProfile(traveller, display, email ?? "", DefaultPassport, DefaultCurrency, null, Weights.Neutral, now, now);
    }

    /// <summary>Replaces the editable fields; throws naming the first field at fault (spec Edge Cases).</summary>
    public TravelProfile Update(ProfileChanges changes, ProfileLookups lookups, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(lookups);

        var name = changes.DisplayName?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength)
        {
            throw new DomainException($"Use 1 to {MaxNameLength} characters.", "displayName");
        }

        var passport = changes.Passport?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(passport) || !lookups.IsCountry(passport))
        {
            throw new DomainException("Choose a country from the list.", "passport");
        }

        var currency = changes.HomeCurrency?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(currency) || !lookups.IsCurrency(currency))
        {
            throw new DomainException("Choose a currency from the list.", "homeCurrency");
        }

        var city = string.IsNullOrWhiteSpace(changes.HomeCityId) ? null : changes.HomeCityId.Trim();
        if (city is not null && !lookups.IsCity(city))
        {
            throw new DomainException("Choose a city from the list.", "homeCityId");
        }

        var weights = Weights.Create(
            Required(changes.Budget, "budget"),
            Required(changes.Culture, "culture"),
            Required(changes.Food, "food"),
            Required(changes.Nature, "nature"),
            Required(changes.Nightlife, "nightlife"),
            Required(changes.Comfort, "comfort"));

        return this with
        {
            DisplayName = name,
            Passport = passport,
            HomeCurrency = currency,
            HomeCityId = city,
            Preferences = weights,
            Pace = PaceRules.Parse(changes.Pace),
            UpdatedAt = now,
        };
    }

    private static string? FirstUsable(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed[..Math.Min(trimmed.Length, MaxNameLength)];
    }

    private static int Required(int? value, string field) =>
        value ?? throw new DomainException("Use a whole number from 0 to 100.", field);
}

/// <summary>The editable fields of a profile, as sent; checked by <see cref="TravelProfile.Update"/>.</summary>
public sealed record ProfileChanges(
    string? DisplayName,
    string? Passport,
    string? HomeCurrency,
    string? HomeCityId,
    int? Budget,
    int? Culture,
    int? Food,
    int? Nature,
    int? Nightlife,
    int? Comfort,
    string? Pace = null);

/// <summary>Answers from the reference data, passed in so Domain keeps no dependency.</summary>
public sealed record ProfileLookups(Func<string, bool> IsCountry, Func<string, bool> IsCurrency, Func<string, bool> IsCity);
