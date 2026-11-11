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
/// <see cref="Cars"/> is null until a car is first added. The times say when the prices were checked (FR-013). The sources
/// and the car booking page are kept to show the offers as their lists do.
/// </summary>
public sealed record PlanAlternatives(
    IReadOnlyList<Ranked<HotelOffer>> Hotels,
    IReadOnlyList<Ranked<FlightOffer>> Flights,
    IReadOnlyList<Ranked<CarOffer>>? Cars,
    DateTimeOffset? HotelsCheckedAt,
    DateTimeOffset? FlightsCheckedAt,
    DateTimeOffset? CarsCheckedAt,
    string? HotelSource = null,
    string? CarSource = null,
    string? CarBookingUrl = null)
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
    DateTimeOffset UpdatedAt,
    IReadOnlyList<PlanEdit>? DayEdits = null)
{
    /// <summary>The traveller's day edits, in the order made, kept through every rebuild (spec 010, research R-5).</summary>
    public IReadOnlyList<PlanEdit> Edits => DayEdits ?? [];

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
    public Plan WithCars(IReadOnlyList<Ranked<CarOffer>> cars, DateTimeOffset checkedAt, string? source = null, string? bookingUrl = null)
    {
        ArgumentNullException.ThrowIfNull(cars);
        return this with
        {
            Alternatives = Alternatives with
            {
                Cars = [.. cars.Take(PlanAlternatives.Max)],
                CarsCheckedAt = checkedAt,
                CarSource = source,
                CarBookingUrl = bookingUrl,
            },
        };
    }

    /// <summary>
    /// Swaps in another offer from the kept list and rebuilds the days around it (FR-011). With no
    /// <paramref name="pools"/> (the places couldn't be fetched), the days are kept and only the total and match change.
    /// </summary>
    public Plan Swap(OfferKind kind, string? offerId, PlacePools? pools, DateTimeOffset now)
    {
        EnsureOffer(kind, offerId);
        var id = offerId!.Trim();
        var changed = kind switch
        {
            OfferKind.Hotel => this with { HotelId = id },
            OfferKind.Flight => this with { FlightId = id },
            _ => this with { CarId = id },
        };
        return changed.Rebuild(pools, now);
    }

    /// <summary>Throws, naming <c>offerId</c>, unless the offer is among the kept alternatives of its kind.</summary>
    public void EnsureOffer(OfferKind kind, string? offerId)
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
    }

    public Plan RemoveCar(PlacePools? pools, DateTimeOffset now) => (this with { CarId = null }).Rebuild(pools, now);

    private Plan Rebuild(PlacePools? pools, DateTimeOffset now)
    {
        if (pools is null)
        {
            return this with
            {
                Built = PlanBuilder.Reprice(Request, Offers, Built, "The places couldn't be loaded, so the days were kept as they were."),
                UpdatedAt = now,
            };
        }

        var built = PlanBuilder.Build(Request, Offers, pools, Edits);
        return this with { Built = built, DayEdits = built.Applied, UpdatedAt = now };
    }

    /// <summary>
    /// The plan built again in place for a new <paramref name="request"/> and its offers (spec 010 FR-012a): the id, name,
    /// status and day edits stay, and so do the traveller's choices. A hotel or flight they swapped in (not the best of
    /// the old list) is kept while the new lists still offer it, a car while the dates are the same (its prices depend on
    /// them); otherwise the offer is chosen as <c>POST /plans</c> chooses it, with a note when a choice was lost.
    /// </summary>
    public Plan Rebuilt(TripRequest request, PlanAlternatives alternatives, PlacePools pools, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(alternatives);
        ArgumentNullException.ThrowIfNull(pools);
        var fresh = Create(Id, Owner, request, alternatives, pools, now);
        var notes = new List<string>();
        var hotel = KeptHotel(fresh.Alternatives, notes);
        var flight = KeptFlight(fresh.Alternatives, notes);
        var keepCar = CarId is not null && Alternatives.Cars is not null && request.Dates == Request.Dates;
        if (CarId is not null && !keepCar)
        {
            notes.Add("Car prices depend on the dates, so your car was removed. Add one again for the new dates.");
        }

        var chosen = fresh with
        {
            HotelId = hotel ?? fresh.HotelId,
            FlightId = flight ?? fresh.FlightId,
            CarId = keepCar ? CarId : null,
            Alternatives = keepCar
                ? fresh.Alternatives with
                {
                    Cars = Alternatives.Cars,
                    CarsCheckedAt = Alternatives.CarsCheckedAt,
                    CarSource = Alternatives.CarSource,
                    CarBookingUrl = Alternatives.CarBookingUrl,
                }
                : fresh.Alternatives,
        };
        var built = PlanBuilder.Build(request, chosen.Offers, pools, Edits);
        return chosen with
        {
            Name = Name,
            Status = Status,
            CreatedAt = CreatedAt,
            Built = built with { Notes = [.. built.Notes, .. notes] },
            DayEdits = built.Applied,
        };
    }

    /// <summary>The hotel the traveller swapped in, when the new list still has it; null to take the best.</summary>
    private string? KeptHotel(PlanAlternatives fresh, List<string> notes)
    {
        if (HotelId is null || Alternatives.Hotels.Count == 0 || string.Equals(Alternatives.Hotels[0].Offer.Hotel.Id, HotelId, StringComparison.Ordinal))
        {
            return null;
        }

        if (fresh.Hotels.Any(h => string.Equals(h.Offer.Hotel.Id, HotelId, StringComparison.Ordinal)))
        {
            return HotelId;
        }

        notes.Add("The hotel you chose isn't offered for this trip any more, so the best match was chosen.");
        return null;
    }

    /// <summary>
    /// The flight the traveller swapped in, when the new list still has it: the same fare, or for new dates the same
    /// airline, flight number and airports. Null to take the best.
    /// </summary>
    private string? KeptFlight(PlanAlternatives fresh, List<string> notes)
    {
        var old = Alternatives.Flights.FirstOrDefault(f => string.Equals(f.Offer.Id, FlightId, StringComparison.Ordinal))?.Offer;
        if (old is null || string.Equals(Alternatives.Flights[0].Offer.Id, FlightId, StringComparison.Ordinal))
        {
            return null;
        }

        var same = fresh.Flights.Select(f => f.Offer).FirstOrDefault(f => string.Equals(f.Id, old.Id, StringComparison.Ordinal))
            ?? fresh.Flights.Select(f => f.Offer).FirstOrDefault(f => old.FlightNumber is not null
                && string.Equals(f.Airline, old.Airline, StringComparison.Ordinal)
                && string.Equals(f.FlightNumber, old.FlightNumber, StringComparison.Ordinal)
                && string.Equals(f.OriginAirport, old.OriginAirport, StringComparison.Ordinal)
                && string.Equals(f.DestinationAirport, old.DestinationAirport, StringComparison.Ordinal));
        if (same is not null)
        {
            return same.Id;
        }

        notes.Add("The flight you chose isn't offered for this trip any more, so the best match was chosen.");
        return null;
    }

    /// <summary>
    /// Adds a day edit and builds the plan again with it (spec 010 FR-016). An edit on the same item replaces the earlier
    /// one: replacing an item the traveller added changes that addition, removing it drops the addition. A new edit that
    /// can't take effect throws: <see cref="DayFullException"/> for an addition, a field error otherwise.
    /// </summary>
    public Plan ChangeDay(PlanEdit edit, PlacePools pools, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(edit);
        ArgumentNullException.ThrowIfNull(pools);
        if (edit.Day < 1 || edit.Day > Built.Days.Count)
        {
            throw new DomainException("Choose a day of the plan.", "day");
        }

        var (edits, made) = Normalised(edit);
        var built = PlanBuilder.Build(Request, Offers, pools, edits);
        if (made is not null && !built.Applied.Contains(made))
        {
            throw made.Action == EditAction.Add
                ? DayFullException.For(edit.Day, Request.Pace)
                : new DomainException("That item doesn't fit this day any more. Choose another.", "id");
        }

        return this with { Built = built, DayEdits = built.Applied, UpdatedAt = now };
    }

    /// <summary>The edits with <paramref name="edit"/> folded in, and the edit that must take effect (none when an
    /// addition was simply dropped).</summary>
    private (List<PlanEdit> Edits, PlanEdit? Made) Normalised(PlanEdit edit)
    {
        var edits = new List<PlanEdit>(Edits);
        var target = edit.ItemId;
        if (target is null)
        {
            edits.Add(edit);
            return (edits, edit);
        }

        // An item the traveller brought in: change or drop the edit that brought it.
        var bringer = edits.FindIndex(e => e.Day == edit.Day && string.Equals(e.NewItem?.Id, target, StringComparison.Ordinal));
        if (bringer >= 0)
        {
            var earlier = edits[bringer];
            edits.RemoveAt(bringer);
            PlanEdit? changed = null;
            if (edit.Action == EditAction.Replace)
            {
                changed = earlier with { NewItem = edit.NewItem };
            }
            else if (earlier.Action == EditAction.Replace)
            {
                // The replacement goes; the original item stays removed.
                changed = PlanEdit.Remove(edit.Day, earlier.ItemId!);
            }

            if (changed is not null)
            {
                edits.Add(changed);
            }

            return (edits, changed);
        }

        edits.RemoveAll(e => e.Day == edit.Day && string.Equals(e.ItemId, target, StringComparison.Ordinal));
        edits.Add(edit);
        return (edits, edit);
    }

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
