using System.Globalization;
using Pamplemousse.Domain.Exceptions;
using Pamplemousse.Domain.Places;
using Pamplemousse.Domain.TravelEvents;

namespace Pamplemousse.Domain.Itinerary;

public enum EditAction
{
    Replace,
    Add,
    Remove,
}

/// <summary>What a day's alternatives are: the kinds the app's "Show me alternatives" offers (spec 010 FR-017).</summary>
public enum AlternativeKind
{
    Sight,
    Nature,
    Restaurant,
    Nightlife,
    Event,
}

/// <summary>
/// The item a replacement or an addition puts in a day: a place or an event, kept whole so a rebuild never needs the
/// source again (spec 008 FR-013).
/// </summary>
public sealed record EditItem(Place? Place, TravelEvent? Event)
{
    public static EditItem Of(Place place) => new(place ?? throw new ArgumentNullException(nameof(place)), null);

    public static EditItem Of(TravelEvent travelEvent) => new(null, travelEvent ?? throw new ArgumentNullException(nameof(travelEvent)));

    public string Id => Place?.Id ?? Event?.Id ?? "";

    public string Name => Place?.Name ?? Event?.Name ?? "";

    public RefKind Kind => Place is null ? RefKind.Event : RefKind.Place;
}

/// <summary>
/// One change the traveller made to a day (spec 010 FR-016). The builder applies it while it builds (research R-5): a
/// replacement takes the replaced item's place, an addition the day's next free slot, a removal leaves free time.
/// <see cref="ItemId"/> is the replaced or removed item; <see cref="NewItem"/> the replacement or addition.
/// </summary>
public sealed record PlanEdit(int Day, EditAction Action, string? ItemId, EditItem? NewItem)
{
    public static PlanEdit Replace(int day, string itemId, EditItem newItem) => new(day, EditAction.Replace, itemId, newItem);

    public static PlanEdit Add(int day, EditItem newItem) => new(day, EditAction.Add, null, newItem);

    public static PlanEdit Remove(int day, string itemId) => new(day, EditAction.Remove, itemId, null);
}

/// <summary>The day has no free slot for its pace (spec 010 FR-018). The API answers 422 <c>day-full</c>.</summary>
public sealed class DayFullException(string message) : DomainException(message)
{
    public static DayFullException For(int day, Pace pace) => new(string.Create(
        CultureInfo.InvariantCulture,
        $"Day {day} is full for a {pace.ToString().ToLowerInvariant()} pace. Replace a place instead."));
}
