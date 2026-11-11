using Pamplemousse.Application.Abstractions;
using Pamplemousse.Application.Ranking;
using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Exceptions;
using Pamplemousse.Domain.Itinerary;
using Pamplemousse.Domain.Ranking;
using Pamplemousse.Domain.Travellers;

namespace Pamplemousse.Application.Itinerary;

/// <summary>The day alternatives of spec 010 FR-017. <see cref="Kind"/> is sight, nature, restaurant, nightlife or event.</summary>
public sealed record GetDayAlternativesQuery(TravellerId Owner, string Id, int Day, string? ItemId, string? Kind);

/// <summary>The plan, for its city, and the alternatives.</summary>
public sealed record DayAlternativesAnswer(Plan Plan, DayAlternativesResult Alternatives);

/// <summary>Reads the plan and the city's free, cached place lists; no paid source is called.</summary>
public sealed class GetDayAlternativesHandler(IPlanStore store, PlanInputs inputs) : IQueryHandler<GetDayAlternativesQuery, DayAlternativesAnswer>
{
    public async Task<DayAlternativesAnswer> HandleAsync(GetDayAlternativesQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var kind = DayEdits.ParseKind(query.Kind);
        var plan = await PlanLookup.FindAsync(store, query.Owner, query.Id, ct);
        var pools = await DayEdits.PlacesAsync(inputs, plan, ct);
        return new DayAlternativesAnswer(plan, DayAlternatives.For(plan, pools, query.Day, query.ItemId, kind));
    }
}

public enum DayChange
{
    AddOrReplace,
    Remove,
}

/// <summary>
/// A day edit (FR-016). For <see cref="DayChange.AddOrReplace"/>, <see cref="Kind"/> is place or event, <see cref="ItemId"/>
/// one of the day's alternatives and <see cref="Replaces"/> the item it replaces (none: an addition). For
/// <see cref="DayChange.Remove"/>, <see cref="ItemId"/> is the item removed.
/// </summary>
public sealed record ChangeDayItemCommand(TravellerId Owner, string Id, int Day, DayChange Change, string? Kind, string? ItemId, string? Replaces);

public sealed class ChangeDayItemHandler(IPlanStore store, PlanInputs inputs, TimeProvider clock) : ICommandHandler<ChangeDayItemCommand, Plan>
{
    public async Task<Plan> HandleAsync(ChangeDayItemCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var isEvent = command.Change == DayChange.AddOrReplace && command.Kind?.Trim().ToUpperInvariant() switch
        {
            "PLACE" => false,
            "EVENT" => true,
            _ => throw new DomainException("Use place or event.", "kind"),
        };
        var plan = await PlanLookup.FindAsync(store, command.Owner, command.Id, ct);
        var pools = await DayEdits.PlacesAsync(inputs, plan, ct);
        PlanEdit edit;
        if (command.Change == DayChange.Remove)
        {
            // Checks the day and that the item is on it, naming the field.
            var target = DayAlternatives.For(plan, pools, command.Day, command.ItemId, null).ItemId!;
            edit = PlanEdit.Remove(command.Day, target);
        }
        else
        {
            var replaces = string.IsNullOrWhiteSpace(command.Replaces) ? null : command.Replaces.Trim();
            var item = DayAlternatives.Find(plan, pools, command.Day, replaces, command.ItemId, isEvent);
            edit = replaces is null ? PlanEdit.Add(command.Day, item) : PlanEdit.Replace(command.Day, replaces, item);
        }

        plan = plan.ChangeDay(edit, pools, clock.GetUtcNow());
        await store.SaveAsync(plan, ct);
        return plan;
    }
}

/// <summary>Rebuild plan (FR-012a): a missing value keeps the plan's.</summary>
public sealed record RebuildPlanCommand(
    TravellerId Owner,
    string Id,
    DateOnly? From,
    DateOnly? To,
    int? Adults,
    string? OriginId,
    RankingInput? Preferences,
    string? Pace,
    decimal? TripBudget);

/// <summary>
/// Builds the plan again in place, as <see cref="BuildPlanHandler"/> builds a new one, keeping its id, name, status and
/// edits. Nothing is saved until the new plan is built, so a failure leaves the saved plan as it was.
/// </summary>
public sealed class RebuildPlanHandler(IPlanStore store, IReferenceData data, PlanInputs inputs, TimeProvider clock)
    : ICommandHandler<RebuildPlanCommand, Plan>
{
    public async Task<Plan> HandleAsync(RebuildPlanCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var now = clock.GetUtcNow();
        var plan = await PlanLookup.FindAsync(store, command.Owner, command.Id, ct);
        var old = plan.Request;
        var destination = data.FindDestination(old.Destination.Id)
            ?? throw new NotFoundException("The plan's city is no longer known.");
        var dates = DateRange.ForTrip(command.From ?? old.Dates.From, command.To ?? old.Dates.To, DateOnly.FromDateTime(now.UtcDateTime));
        var origin = string.IsNullOrWhiteSpace(command.OriginId) ? old.OriginId : command.OriginId.Trim();
        if (origin is not null && data.FindDestination(origin) is null)
        {
            throw new DomainException("Choose a departure city from the list.", "originId");
        }

        var p = command.Preferences;
        var w = old.Weights;
        var weights = Weights.Create(
            p?.Budget ?? w.Budget, p?.Culture ?? w.Culture, p?.Food ?? w.Food, p?.Nature ?? w.Nature, p?.Nightlife ?? w.Nightlife, p?.Comfort ?? w.Comfort);
        var trip = TripRequest.Create(
            old.Destination,
            dates,
            command.Adults ?? old.Adults,
            old.Currency,
            origin,
            weights,
            command.Pace is null ? old.Pace : PaceRules.Parse(command.Pace),
            command.TripBudget ?? old.TripBudget,
            PlanInputs.UtcOffsetOf(destination, dates.From));

        var gathered = await inputs.GatherAsync(trip, destination, ct);
        if (!gathered.Pools.HasPlaces)
        {
            throw new NoPlacesException();
        }

        plan = plan.Rebuilt(trip, gathered.Alternatives, gathered.Pools, now);
        await store.SaveAsync(plan, ct);
        return plan;
    }
}

internal static class DayEdits
{
    public static AlternativeKind? ParseKind(string? kind) => string.IsNullOrWhiteSpace(kind)
        ? null
        : Enum.TryParse<AlternativeKind>(kind.Trim(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new DomainException("Use sight, nature, restaurant, nightlife or event.", "kind");

    /// <summary>Day edits need the city's place lists (free and cached); without them nothing can be checked.</summary>
    public static async Task<PlacePools> PlacesAsync(PlanInputs inputs, Plan plan, CancellationToken ct) =>
        await inputs.PlacesAsync(plan.Request, ct)
            ?? throw new ServiceUnavailableException("The city's places couldn't be loaded. Try again in a moment.");
}
