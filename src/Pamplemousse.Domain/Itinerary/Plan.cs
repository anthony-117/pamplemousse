using Pamplemousse.Domain.Cars;
using Pamplemousse.Domain.Exceptions;
using Pamplemousse.Domain.Flights;
using Pamplemousse.Domain.Hotels;
using Pamplemousse.Domain.Ranking;
using Pamplemousse.Domain.Travellers;

namespace Pamplemousse.Domain.Itinerary;

public enum PlanStatus
{
    Draft,
    Planned,
}

public enum OfferKind
{
    Hotel,
    Flight,
    Car,
}

/// <summary>
/// The top of each ranked list the plan used, copied with their matches, so a swap never calls a source (research R-7).
/// <see cref="Cars"/> is null until a car is first added. The times say when the prices were checked (FR-013).
/// </summary>
public sealed record PlanAlternatives(
    IReadOnlyList<Ranked<HotelOffer>> Hotels,
    IReadOnlyList<Ranked<FlightOffer>> Flights,
    IReadOnlyList<Ranked<CarOffer>>? Cars,
    DateTimeOffset? HotelsCheckedAt,
    DateTimeOffset? FlightsCheckedAt,
    DateTimeOffset? CarsCheckedAt)
{
    public const int Max = 20;
}

/// <summary>A saved plan (spec 008, FR-012 to FR-015). Only its owner sees it.</summary>
public sealed record Plan(
    string Id,
    TravellerId Owner,
    string Name,
    PlanStatus Status,
    TripRequest Request,
    PlanAlternatives Alternatives,
    string? HotelId,
    string? FlightId,
    string? CarId,
    BuiltPlan Built,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public const int MaxPlans = 50;
    public const int MaxNameLength = 60;

    /// <summary>A new draft with the best hotel and flight and no car (FR-002, FR-012).</summary>
    public static Plan Create(string id, TravellerId owner, TripRequest request, PlanAlternatives alternatives, PlacePools pools, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(alternatives);
        var kept = alternatives with
        {
            Hotels = [.. alternatives.Hotels.Take(PlanAlternatives.Max)],
            Flights = [.. alternatives.Flights.Take(PlanAlternatives.Max)],
            Cars = alternatives.Cars is null ? null : [.. alternatives.Cars.Take(PlanAlternatives.Max)],
        };
        var plan = new Plan(
            id,
            owner,
            DefaultName(request),
            PlanStatus.Draft,
            request,
            kept,
            kept.Hotels.Count > 0 ? kept.Hotels[0].Offer.Hotel.Id : null,
            kept.Flights.Count > 0 ? kept.Flights[0].Offer.Id : null,
            null,
            null!,
            now,
            now);
        return plan with { Built = PlanBuilder.Build(request, plan.Offers, pools) };
    }

    /// <summary>"Rome, 5 days", or "Rome, 1 day".</summary>
    public static string DefaultName(TripRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var days = request.Days;
        var name = days == 1 ? $"{request.Destination.Name}, 1 day" : $"{request.Destination.Name}, {days.ToString(System.Globalization.CultureInfo.InvariantCulture)} days";
        return name.Length <= MaxNameLength ? name : name[..MaxNameLength];
    }

    /// <summary>Refuses a 51st plan (FR-015).</summary>
    public static void EnsureRoomFor(int existing)
    {
        if (existing >= MaxPlans)
        {
            throw new PlanLimitException();
        }
    }

    public PlanOffers Offers => new(
        Alternatives.Hotels.FirstOrDefault(h => string.Equals(h.Offer.Hotel.Id, HotelId, StringComparison.Ordinal)),
        Alternatives.Flights.FirstOrDefault(f => string.Equals(f.Offer.Id, FlightId, StringComparison.Ordinal)),
        Alternatives.Cars?.FirstOrDefault(c => string.Equals(c.Offer.Id, CarId, StringComparison.Ordinal)),
        FallbackFlightMinutes());

    public Plan Rename(string? name, DateTimeOffset now)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxNameLength)
        {
            throw new DomainException($"Use 1 to {MaxNameLength} characters.", "name");
        }

        return this with { Name = trimmed, UpdatedAt = now };
    }

    public Plan WithStatus(string? status, DateTimeOffset now) => this with
    {
        Status = status?.Trim().ToUpperInvariant() switch
        {
            "DRAFT" => PlanStatus.Draft,
            "PLANNED" => PlanStatus.Planned,
            _ => throw new DomainException("Use draft or planned.", "status"),
        },
        UpdatedAt = now,
    };

    /// <summary>The kept cars, the first time a car is added (research R-7).</summary>
    public Plan WithCars(IReadOnlyList<Ranked<CarOffer>> cars, DateTimeOffset checkedAt)
    {
        ArgumentNullException.ThrowIfNull(cars);
        return this with { Alternatives = Alternatives with { Cars = [.. cars.Take(PlanAlternatives.Max)], CarsCheckedAt = checkedAt } };
    }

    /// <summary>
    /// Swaps in another offer from the kept list and rebuilds the days around it (FR-011). With no
    /// <paramref name="pools"/> (the places couldn't be fetched), the days are kept and only the total and match change.
    /// </summary>
    public Plan Swap(OfferKind kind, string? offerId, PlacePools? pools, DateTimeOffset now)
    {
        var id = offerId?.Trim();
        var known = kind switch
        {
            OfferKind.Hotel => Alternatives.Hotels.Any(h => string.Equals(h.Offer.Hotel.Id, id, StringComparison.Ordinal)),
            OfferKind.Flight => Alternatives.Flights.Any(f => string.Equals(f.Offer.Id, id, StringComparison.Ordinal)),
            _ => Alternatives.Cars?.Any(c => string.Equals(c.Offer.Id, id, StringComparison.Ordinal)) == true,
        };
        if (!known)
        {
            throw new DomainException("Choose an offer from the list.", "offerId");
        }

        var changed = kind switch
        {
            OfferKind.Hotel => this with { HotelId = id },
            OfferKind.Flight => this with { FlightId = id },
            _ => this with { CarId = id },
        };
        return changed.Rebuild(pools, now);
    }

    public Plan RemoveCar(PlacePools? pools, DateTimeOffset now) => (this with { CarId = null }).Rebuild(pools, now);

    private Plan Rebuild(PlacePools? pools, DateTimeOffset now) => this with
    {
        Built = pools is null
            ? PlanBuilder.Reprice(Request, Offers, Built, "The places couldn't be loaded, so the days were kept as they were.")
            : PlanBuilder.Build(Request, Offers, pools),
        UpdatedAt = now,
    };

    /// <summary>The fastest known time of the same route among the kept fares (research R-5).</summary>
    private int? FallbackFlightMinutes()
    {
        var chosen = Alternatives.Flights.FirstOrDefault(f => string.Equals(f.Offer.Id, FlightId, StringComparison.Ordinal))?.Offer;
        if (chosen is null || chosen.DurationMinutes is not null)
        {
            return null;
        }

        return Alternatives.Flights
            .Select(f => f.Offer)
            .Where(f => string.Equals(f.OriginAirport, chosen.OriginAirport, StringComparison.Ordinal)
                && string.Equals(f.DestinationAirport, chosen.DestinationAirport, StringComparison.Ordinal))
            .Min(f => f.DurationMinutes);
    }
}

/// <summary>The traveller already keeps <see cref="Plan.MaxPlans"/> plans (FR-015).</summary>
public sealed class PlanLimitException() : DomainException($"You can keep up to {Plan.MaxPlans} plans. Delete one to make room.");
