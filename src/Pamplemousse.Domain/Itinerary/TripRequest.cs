using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Exceptions;
using Pamplemousse.Domain.Ranking;

namespace Pamplemousse.Domain.Itinerary;

/// <summary>How full the days are (spec 008, FR-006). It sets the places a full day holds; ranking ignores it.</summary>
public enum Pace
{
    Relaxed,
    Balanced,
    Packed,
}

public static class PaceRules
{
    public static int PlacesPerDay(this Pace pace) => pace switch
    {
        Pace.Relaxed => 2,
        Pace.Packed => 4,
        _ => 3,
    };

    /// <summary>"relaxed", "balanced" or "packed" in any case; Balanced when left out.</summary>
    public static Pace Parse(string? value, string field = "pace") => value?.Trim().ToUpperInvariant() switch
    {
        null or "" => Pace.Balanced,
        "RELAXED" => Pace.Relaxed,
        "BALANCED" => Pace.Balanced,
        "PACKED" => Pace.Packed,
        _ => throw new DomainException("Use relaxed, balanced or packed.", field),
    };

    public static string ToText(this Pace pace) => pace switch
    {
        Pace.Relaxed => "relaxed",
        Pace.Packed => "packed",
        _ => "balanced",
    };
}

/// <summary>The city a plan is for. <see cref="Centre"/> stands in for the hotel when there is none.</summary>
public sealed record PlanDestination(string Id, string Name, string CountryCode, GeoPoint Centre);

/// <summary>
/// What the traveller asked a plan for (FR-001). <see cref="UtcOffset"/> is the destination's offset during the trip, so
/// flight times can be read as local times.
/// </summary>
public sealed record TripRequest(
    PlanDestination Destination,
    DateRange Dates,
    int Adults,
    string Currency,
    string? OriginId,
    Weights Weights,
    Pace Pace,
    decimal? TripBudget,
    TimeSpan UtcOffset)
{
    public const int MaxDays = 30;
    public const int MaxAdults = 8;

    public int Days => Dates.To.DayNumber - Dates.From.DayNumber + 1;

    public Preferences Preferences => new(Weights, TripBudget);

    /// <summary>Checks the parts <see cref="DateRange"/> and <see cref="Weights"/> don't, naming the field at fault.</summary>
    public static TripRequest Create(
        PlanDestination destination,
        DateRange dates,
        int? adults,
        string? currency,
        string? originId,
        Weights weights,
        Pace pace,
        decimal? tripBudget,
        TimeSpan utcOffset)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(dates);
        ArgumentNullException.ThrowIfNull(weights);
        if (dates.To.DayNumber - dates.From.DayNumber + 1 > MaxDays)
        {
            throw new DomainException($"A plan covers 1 to {MaxDays} days.", "to");
        }

        var count = adults ?? 2;
        if (count is < 1 or > MaxAdults)
        {
            throw new DomainException($"Use 1 to {MaxAdults} adults.", "adults");
        }

        var code = (currency ?? "USD").Trim().ToUpperInvariant();
        if (code.Length != 3 || !code.All(char.IsAsciiLetter))
        {
            throw new DomainException("Use a 3-letter currency code.", "currency");
        }

        if (tripBudget is <= 0)
        {
            throw new DomainException("The trip budget must be more than 0.", "tripBudget");
        }

        var origin = string.IsNullOrWhiteSpace(originId) ? null : originId.Trim();
        return new TripRequest(destination, dates, count, code, origin, weights, pace, tripBudget, utcOffset);
    }
}
