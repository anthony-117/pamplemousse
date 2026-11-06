using Pamplemousse.Application.Abstractions;
using Pamplemousse.Application.Ranking;
using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Exceptions;
using Pamplemousse.Domain.Itinerary;
using Pamplemousse.Domain.Ranking;
using Pamplemousse.Domain.Travellers;

namespace Pamplemousse.Application.Itinerary;

/// <summary>No places at all could be found, so there is nothing to build days from (spec Edge Cases). The API answers 422.</summary>
public sealed class NoPlacesException() : InvalidOperationException("No places could be found for this destination, so no plan can be made. Try again later.");

/// <summary>
/// What the traveller asks a plan for (FR-001). A preference left out is 50 (spec 007). <see cref="Pace"/> left out is the
/// profile's pace, or balanced.
/// </summary>
public sealed record BuildPlanCommand(
    TravellerId Owner,
    string? DestinationId,
    DateOnly? From,
    DateOnly? To,
    int? Adults,
    string? Currency,
    string? OriginId,
    RankingInput? Preferences,
    string? Pace,
    decimal? TripBudget);

/// <summary>Builds a plan and saves it as a draft (spec 008, Story 1).</summary>
public sealed class BuildPlanHandler(IPlanStore store, ITravelProfileStore profiles, IReferenceData data, PlanInputs inputs, TimeProvider clock)
    : ICommandHandler<BuildPlanCommand, Plan>
{
    public async Task<Plan> HandleAsync(BuildPlanCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var now = clock.GetUtcNow();
        var destination = (string.IsNullOrWhiteSpace(command.DestinationId) ? null : data.FindDestination(command.DestinationId.Trim()))
            ?? throw new DomainException("Choose a destination from the list.", "destinationId");
        var dates = DateRange.ForTrip(command.From, command.To, DateOnly.FromDateTime(now.UtcDateTime));
        var origin = string.IsNullOrWhiteSpace(command.OriginId) ? null : command.OriginId.Trim();
        if (origin is not null && data.FindDestination(origin) is null)
        {
            throw new DomainException("Choose a departure city from the list.", "originId");
        }

        var p = command.Preferences;
        var weights = Weights.Create(p?.Budget, p?.Culture, p?.Food, p?.Nature, p?.Nightlife, p?.Comfort);
        var pace = command.Pace is null
            ? (await profiles.GetAsync(command.Owner, ct))?.Pace ?? Pace.Balanced
            : PaceRules.Parse(command.Pace);
        var trip = TripRequest.Create(
            new PlanDestination(destination.Id, destination.Name, destination.CountryCode, destination.Location),
            dates,
            command.Adults,
            command.Currency,
            origin,
            weights,
            pace,
            command.TripBudget,
            PlanInputs.UtcOffsetOf(destination, dates.From));

        // Checked before any source is asked, so a refused plan costs nothing.
        Plan.EnsureRoomFor(await store.CountAsync(command.Owner, ct));

        var gathered = await inputs.GatherAsync(trip, destination, ct);
        if (!gathered.Pools.HasPlaces)
        {
            throw new NoPlacesException();
        }

        var plan = Plan.Create(Guid.NewGuid().ToString("N"), command.Owner, trip, gathered.Alternatives, gathered.Pools, now);
        await store.AddAsync(plan, ct);
        return plan;
    }
}

public sealed record GetPlanQuery(TravellerId Owner, string Id);

/// <summary>A plan as saved (FR-013): no source is called.</summary>
public sealed class GetPlanHandler(IPlanStore store) : IQueryHandler<GetPlanQuery, Plan>
{
    public Task<Plan> HandleAsync(GetPlanQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        return PlanLookup.FindAsync(store, query.Owner, query.Id, ct);
    }
}

public sealed record ListPlansQuery(TravellerId Owner);

public sealed record PlanList(IReadOnlyList<PlanSummary> Items, int Count, int Max);

/// <summary>The traveller's plans, newest change first (FR-012).</summary>
public sealed class ListPlansHandler(IPlanStore store) : IQueryHandler<ListPlansQuery, PlanList>
{
    public async Task<PlanList> HandleAsync(ListPlansQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var items = await store.ListAsync(query.Owner, ct);
        return new PlanList(items, items.Count, Plan.MaxPlans);
    }
}

/// <summary>A new name, a new status, or both.</summary>
public sealed record ChangePlanCommand(TravellerId Owner, string Id, string? Name, string? Status);

public sealed class ChangePlanHandler(IPlanStore store, TimeProvider clock) : ICommandHandler<ChangePlanCommand, Plan>
{
    public async Task<Plan> HandleAsync(ChangePlanCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Name is null && command.Status is null)
        {
            throw new DomainException("Send a new name, a new status, or both.", "name");
        }

        var plan = await PlanLookup.FindAsync(store, command.Owner, command.Id, ct);
        var now = clock.GetUtcNow();
        if (command.Name is not null)
        {
            plan = plan.Rename(command.Name, now);
        }

        if (command.Status is not null)
        {
            plan = plan.WithStatus(command.Status, now);
        }

        await store.SaveAsync(plan, ct);
        return plan;
    }
}

public sealed record DeletePlanCommand(TravellerId Owner, string Id);

public sealed class DeletePlanHandler(IPlanStore store) : ICommandHandler<DeletePlanCommand, bool>
{
    public async Task<bool> HandleAsync(DeletePlanCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        return await store.DeleteAsync(command.Owner, command.Id, ct)
            ? true
            : throw new NotFoundException("The plan was not found.");
    }
}

public sealed record GetAlternativesQuery(TravellerId Owner, string Id, OfferKind Kind);

/// <summary>
/// The kept ranked list of a kind (research R-7). For cars, the first call runs the ranked car search once and keeps its
/// top 20 with the plan, so later calls and swaps never call the source.
/// </summary>
public sealed class GetAlternativesHandler(IPlanStore store, PlanInputs inputs, TimeProvider clock) : IQueryHandler<GetAlternativesQuery, Plan>
{
    public async Task<Plan> HandleAsync(GetAlternativesQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var plan = await PlanLookup.FindAsync(store, query.Owner, query.Id, ct);
        if (query.Kind != OfferKind.Car || plan.Alternatives.Cars is not null)
        {
            return plan;
        }

        var cars = await inputs.CarsAsync(plan.Request, ct);
        plan = plan.WithCars(cars.Cars, clock.GetUtcNow(), cars.Source, cars.BookingUrl);
        await store.SaveAsync(plan, ct);
        return plan;
    }
}

/// <summary>Swaps in an offer of the kept list (FR-011); a null <see cref="OfferId"/> with <see cref="OfferKind.Car"/> removes the car.</summary>
public sealed record SwapOfferCommand(TravellerId Owner, string Id, OfferKind Kind, string? OfferId, bool RemoveCar = false);

/// <summary>
/// Swaps the hotel, flight or car and rebuilds the days, total and match by the same rules. No paid source is called: the
/// offers are the plan's copies, and only the free, cached place lists are fetched again. If they fail, the days are kept.
/// </summary>
public sealed class SwapOfferHandler(IPlanStore store, PlanInputs inputs, TimeProvider clock) : ICommandHandler<SwapOfferCommand, Plan>
{
    public async Task<Plan> HandleAsync(SwapOfferCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var plan = await PlanLookup.FindAsync(store, command.Owner, command.Id, ct);
        var now = clock.GetUtcNow();
        if (command.RemoveCar)
        {
            if (plan.CarId is null)
            {
                return plan;
            }

            plan = plan.RemoveCar(await inputs.PlacesAsync(plan.Request, ct), now);
        }
        else
        {
            // Checked before the places are fetched, so a wrong id answers at once.
            plan.EnsureOffer(command.Kind, command.OfferId);
            plan = plan.Swap(command.Kind, command.OfferId, await inputs.PlacesAsync(plan.Request, ct), now);
        }

        await store.SaveAsync(plan, ct);
        return plan;
    }
}

internal static class PlanLookup
{
    /// <summary>The owner's plan; another traveller's plan is not found either (FR-014).</summary>
    public static async Task<Plan> FindAsync(IPlanStore store, TravellerId owner, string id, CancellationToken ct) =>
        (string.IsNullOrWhiteSpace(id) ? null : await store.GetAsync(owner, id.Trim(), ct))
            ?? throw new NotFoundException("The plan was not found.");
}
