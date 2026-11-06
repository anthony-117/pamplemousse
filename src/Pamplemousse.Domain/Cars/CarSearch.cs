using System.Globalization;
using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Exceptions;

namespace Pamplemousse.Domain.Cars;

public enum PickUpKind
{
    Centre,
    Airport,
}

/// <summary>Where the car is picked up and returned (spec 004, FR-001/FR-002): the city centre or one of the city's airports.</summary>
public sealed record PickUpPlace(string Id, PickUpKind Kind, string Name, string? Iata, GeoPoint Location)
{
    public const string CentreId = "centre";

    public static PickUpPlace Centre(GeoPoint location) => new(CentreId, PickUpKind.Centre, "City centre", null, location);

    public static PickUpPlace Airport(string iata, string name, GeoPoint location)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(iata);
        return new PickUpPlace(iata.ToUpperInvariant(), PickUpKind.Airport, name, iata.ToUpperInvariant(), location);
    }
}

/// <summary>A city's car pick-up airports, as last looked up at the source (stored on the city, spec FR-013).</summary>
public sealed record CarAirports(string DestinationId, IReadOnlyList<PickUpPlace> Airports, DateTimeOffset CheckedAt);

/// <summary>
/// What the traveller asked for: one car, picked up and returned at the same place at 10:00 on the trip's first and last day.
/// Only <see cref="Create"/> makes one, so every instance is valid.
/// </summary>
public sealed record CarSearch
{
    public const string DefaultCurrency = "USD";
    public const int DefaultDriverAge = 30;
    public const int MinDriverAge = 18;
    public const int MaxDriverAge = 99;

    /// <summary>Under this age, or over <see cref="SeniorFrom"/>, rental companies may charge extra.</summary>
    public const int YoungUntil = 25;

    public const int SeniorFrom = 70;

    /// <summary>Pick-up and return time (spec clarification: 10:00 both ways).</summary>
    public static readonly TimeOnly PickUpTime = new(10, 0);

    private CarSearch(string destinationId, PickUpPlace place, DateRange dates, int driverAge, string currency)
    {
        DestinationId = destinationId;
        Place = place;
        Dates = dates;
        DriverAge = driverAge;
        Currency = currency;
    }

    public string DestinationId { get; }

    public PickUpPlace Place { get; }

    /// <summary>Pick-up on <see cref="DateRange.From"/>, return on <see cref="DateRange.To"/>.</summary>
    public DateRange Dates { get; }

    public int DriverAge { get; }

    public string Currency { get; }

    /// <summary>Rental days: the days between pick-up and return, and at least one for a same-day trip.</summary>
    public int Days => Math.Max(1, Dates.To.DayNumber - Dates.From.DayNumber);

    /// <summary>Identifies the search for caching, the lock and history. Sorting, filters and page are not part of it.</summary>
    public string Key => string.Create(
        CultureInfo.InvariantCulture, $"{DestinationId}:{Place.Id}:{Dates.From:yyyyMMdd}:{Dates.To:yyyyMMdd}:{DriverAge}:{Currency}");

    /// <summary>The note for young and senior drivers (FR-015); null otherwise.</summary>
    public string? DriverNote => DriverAge < YoungUntil
        ? "The rental company may charge a young driver fee at pick-up."
        : DriverAge > SeniorFrom ? "The rental company may charge a senior driver fee at pick-up." : null;

    public static CarSearch Create(string destinationId, PickUpPlace place, DateRange dates, int? driverAge, string? currency)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationId);
        ArgumentNullException.ThrowIfNull(place);
        ArgumentNullException.ThrowIfNull(dates);

        var age = driverAge ?? DefaultDriverAge;
        if (age is < MinDriverAge or > MaxDriverAge)
        {
            throw new DomainException($"The driver's age must be between {MinDriverAge} and {MaxDriverAge}.", "driverAge");
        }

        var code = string.IsNullOrWhiteSpace(currency) ? DefaultCurrency : currency.Trim().ToUpperInvariant();
        if (code.Length != 3 || !code.All(char.IsAsciiLetter))
        {
            throw new DomainException("Currency must be a 3-letter code, such as USD.", "currency");
        }

        return new CarSearch(destinationId, place, dates, age, code);
    }
}
