using Pamplemousse.Domain.Common;

namespace Pamplemousse.Domain.Cars;

public enum Gearbox
{
    Manual,
    Automatic,
}

/// <summary>Where at the pick-up place the car waits (FR-003).</summary>
public enum PickUpType
{
    Terminal,
    Shuttle,
    RentalCentre,
    TrainStation,
    Downtown,
    Other,
}

public sealed record RentalCompany(string Name, string? LogoUrl, double? Rating, int? ReviewCount);

/// <summary>The rental desk. <see cref="DistanceKm"/> is from the destination's centre.</summary>
public sealed record PickUpPoint(string Name, PickUpType Type, string? Address, GeoPoint Location, double DistanceKm);

/// <summary>The normal price for the whole rental (FR-004), with its price per day.</summary>
public sealed record CarPrice(decimal Total, decimal PerDay, string Currency, bool PayNow)
{
    /// <summary>
    /// The price Booking.com shows to anyone not signed in: <paramref name="normal"/> when the source gives one, else
    /// <paramref name="price"/> (research R-4). Null for a missing, zero or negative price, so the car is left out.
    /// </summary>
    public static CarPrice? For(decimal? normal, decimal? price, int days, string currency, bool payNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        var total = normal is > 0 ? normal : price;
        if (total is not > 0)
        {
            return null;
        }

        var rounded = Math.Round(total.Value, 2, MidpointRounding.AwayFromZero);
        return new CarPrice(rounded, Math.Round(rounded / Math.Max(1, days), 2, MidpointRounding.AwayFromZero), currency, payNow);
    }
}

/// <summary>
/// One car to rent for a search. <see cref="Rank"/> is the source's recommended order, from 1. <see cref="SourceUrl"/> is the
/// source's own link for the car, kept but not shown, since it opens "page not found" today (research R-6).
/// </summary>
public sealed record CarOffer(
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
    RentalCompany Company,
    PickUpPoint PickUp,
    CarPrice Price,
    bool? FreeCancellation,
    Money? Deposit,
    Money? DamageExcess,
    bool Sponsored,
    int Rank,
    string? SourceUrl)
{
    /// <summary>
    /// The cars worth showing, in the source's order: the source sends the same model from the same company and desk more than
    /// once (80 of 500 in Rome), so only the cheapest of each is kept (spec edge case).
    /// </summary>
    public static IReadOnlyList<CarOffer> Distinct(IEnumerable<CarOffer> offers)
    {
        ArgumentNullException.ThrowIfNull(offers);
        return [.. offers
            .GroupBy(o => (Model: o.Model.ToUpperInvariant(), Company: o.Company.Name.ToUpperInvariant(), Desk: o.PickUp.Name.ToUpperInvariant()))
            .Select(g => g.OrderBy(o => o.Price.Total).ThenBy(o => o.Rank).First())
            .OrderBy(o => o.Rank)];
    }
}

/// <summary>
/// One saved call to the source (ADR 0017, FR-012): what was asked, when, every car found, and the source's whole answer
/// gzip-compressed (FR-008b). Never changed afterwards.
/// </summary>
public sealed record CarSnapshot(
    string SearchKey,
    string DestinationId,
    string PlaceId,
    DateOnly From,
    DateOnly To,
    int DriverAge,
    string Currency,
    DateTimeOffset FetchedAt,
    bool MayHaveMore,
    IReadOnlyList<CarOffer> Cars,
    byte[] Raw);
