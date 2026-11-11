using System.Globalization;
using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Exceptions;
using Pamplemousse.Domain.Places;

namespace Pamplemousse.Domain.Itinerary;

/// <summary>One alternative and how far it is from the day's other items.</summary>
public sealed record DayAlternative(EditItem Item, double WalkKmFromDay);

/// <summary>What could take an item's place on a day, or fill a new slot (spec 010 FR-017). <see cref="Message"/> says why there are none.</summary>
public sealed record DayAlternativesResult(int Day, string? ItemId, AlternativeKind Kind, IReadOnlyList<DayAlternative> Items, string? Message);

/// <summary>
/// The day alternatives of spec 010: from the city's places of the kind and that date's events, not already in the
/// plan, nearest to the day's other items first, then in the source's order (best first). Nothing here calls a source.
/// </summary>
public static class DayAlternatives
{
    public const int Max = 6;

    public static DayAlternativesResult For(Plan plan, PlacePools pools, int day, string? itemId, AlternativeKind? kind)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(pools);
        var (planDay, item) = Target(plan, day, itemId);
        var chosen = kind ?? (item is null ? AlternativeKind.Sight : KindOf(item.Kind));
        if (item is not null && !Allowed(item.Kind).Contains(chosen))
        {
            throw new DomainException(ReplaceRule, "kind");
        }

        var items = Candidates(plan, pools, planDay, item, chosen).Take(Max).ToList();
        return new DayAlternativesResult(
            day,
            item?.Ref?.Id,
            chosen,
            items,
            items.Count == 0 ? string.Create(CultureInfo.InvariantCulture, $"No other {Label(chosen)} are known near day {day}.") : null);
    }

    /// <summary>
    /// The alternative with <paramref name="id"/> among every kind that may take <paramref name="itemId"/>'s place (or
    /// fill a new slot); a field error naming <c>id</c> when it isn't one.
    /// </summary>
    public static EditItem Find(Plan plan, PlacePools pools, int day, string? itemId, string? id, bool isEvent)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(pools);
        var (planDay, item) = Target(plan, day, itemId);
        var wanted = id?.Trim();
        var kinds = (item is null ? Enum.GetValues<AlternativeKind>() : Allowed(item.Kind))
            .Where(k => (k == AlternativeKind.Event) == isEvent);
        return kinds
            .SelectMany(k => Candidates(plan, pools, planDay, item, k))
            .Select(a => a.Item)
            .FirstOrDefault(a => string.Equals(a.Id, wanted, StringComparison.Ordinal))
            ?? throw new DomainException(item is null || isEvent == (item.Kind == ItemKind.Event)
                ? "Choose one of the day's alternatives."
                : ReplaceRule, item is null || isEvent == (item.Kind == ItemKind.Event) ? "id" : "kind");
    }

    private const string ReplaceRule = "A place can only be replaced by a place, a meal by a restaurant, an evening out by another, and an event by an event.";

    /// <summary>The day and, with <paramref name="itemId"/>, the item on it; field errors naming <c>day</c> or <c>itemId</c>.</summary>
    private static (PlanDay Day, PlanItem? Item) Target(Plan plan, int day, string? itemId)
    {
        if (day < 1 || day > plan.Built.Days.Count)
        {
            throw new DomainException("Choose a day of the plan.", "day");
        }

        var planDay = plan.Built.Days[day - 1];
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return (planDay, null);
        }

        var item = planDay.Items.FirstOrDefault(i =>
            i.Kind is ItemKind.Sight or ItemKind.Nature or ItemKind.Restaurant or ItemKind.Nightlife or ItemKind.Event
            && string.Equals(i.Ref?.Id, itemId.Trim(), StringComparison.Ordinal));
        return item is null ? throw new DomainException("Choose an item of this day.", "itemId") : (planDay, item);
    }

    private static AlternativeKind KindOf(ItemKind kind) => kind switch
    {
        ItemKind.Nature => AlternativeKind.Nature,
        ItemKind.Restaurant => AlternativeKind.Restaurant,
        ItemKind.Nightlife => AlternativeKind.Nightlife,
        ItemKind.Event => AlternativeKind.Event,
        _ => AlternativeKind.Sight,
    };

    /// <summary>A sight or nature place can become any place; a meal stays a restaurant, an evening out an evening out, an event an event.</summary>
    private static AlternativeKind[] Allowed(ItemKind kind) => kind switch
    {
        ItemKind.Sight or ItemKind.Nature => [AlternativeKind.Sight, AlternativeKind.Nature, AlternativeKind.Restaurant, AlternativeKind.Nightlife],
        ItemKind.Restaurant => [AlternativeKind.Restaurant],
        ItemKind.Nightlife => [AlternativeKind.Nightlife],
        _ => [AlternativeKind.Event],
    };

    private static string Label(AlternativeKind kind) => kind switch
    {
        AlternativeKind.Nature => "nature places",
        AlternativeKind.Restaurant => "restaurants",
        AlternativeKind.Nightlife => "places for an evening out",
        AlternativeKind.Event => "events",
        _ => "sights",
    };

    private static IEnumerable<DayAlternative> Candidates(Plan plan, PlacePools pools, PlanDay day, PlanItem? replaced, AlternativeKind kind)
    {
        // Already in the plan, brought by an edit, or one the traveller removed or replaced.
        var taken = new HashSet<string>(
            plan.Built.Days.SelectMany(d => d.Items).Select(i => i.Ref?.Id)
                .Concat(plan.Edits.Select(e => e.NewItem?.Id))
                .Concat(plan.Edits.Select(e => e.ItemId))
                .OfType<string>(),
            StringComparer.Ordinal);

        if (kind == AlternativeKind.Event)
        {
            return pools.Events
                .Where(e => e.Date == day.Date && !taken.Contains(e.Id))
                .Select((e, index) => (Alt: new DayAlternative(EditItem.Of(e), 0), Index: index, Time: e.Time ?? "99:99"))
                .OrderBy(x => x.Time, StringComparer.Ordinal)
                .ThenBy(x => x.Index)
                .Select(x => x.Alt);
        }

        var anchors = day.Items
            .Where(i => !ReferenceEquals(i, replaced) && i.Ref?.Location is not null)
            .Select(i => i.Ref!.Location!.Value)
            .ToList();
        if (anchors.Count == 0)
        {
            anchors.Add(plan.Offers.Hotel?.Offer.Hotel.Location ?? plan.Request.Destination.Centre);
        }

        IReadOnlyList<Place> source = kind switch
        {
            AlternativeKind.Nature => pools.Nature,
            AlternativeKind.Restaurant => pools.Restaurants,
            AlternativeKind.Nightlife => pools.Nightlife,
            _ => pools.Sights,
        };
        return source
            .Where(p => !taken.Contains(p.Id) && !string.IsNullOrWhiteSpace(p.Name))
            .Select((p, index) => (Alt: new DayAlternative(EditItem.Of(p), Nearest(anchors, p.Location)), Index: index))
            .OrderBy(x => x.Alt.WalkKmFromDay)
            .ThenBy(x => x.Index)
            .Select(x => x.Alt);
    }

    private static double Nearest(List<GeoPoint> anchors, GeoPoint to) => anchors.Min(a => PlanRules.WalkKm(a, to));
}
