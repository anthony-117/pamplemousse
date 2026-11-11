using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Exceptions;
using Pamplemousse.Domain.Itinerary;
using Pamplemousse.Domain.Places;
using Pamplemousse.Domain.Travellers;
using static Pamplemousse.Domain.Tests.Itinerary.PlanFixtures;

namespace Pamplemousse.Domain.Tests.Itinerary;

/// <summary>Spec 010 FR-016 to FR-018 and research R-5: day edits are inputs of the builder.</summary>
public class PlanEditTests
{
    private static readonly DateTimeOffset Now = new(2027, 3, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly TravellerId Owner = new("uid-1");

    private static PlanAlternatives Alternatives() => new(
        [Hotel("bk:1", 0.9, 500), Hotel("bk:2", 0.7, 300, at: new GeoPoint(41.92, 12.47))],
        [Flight("tp:1", 0.8), Flight("tp:2", 0.6, 150)],
        null,
        Now,
        Now,
        null);

    private static PlacePools TheseEvents() => Pools(events: [Event("ev:1", From.AddDays(1), "20:00", 30), Event("ev:2", From.AddDays(1), "21:00", 40)]);

    private static Plan NewPlan(PlacePools? pools = null, Pace pace = Pace.Balanced) =>
        Plan.Create("p1", Owner, Trip(pace: pace), Alternatives(), pools ?? Pools(), Now);

    private static PlanItem FirstSight(Plan plan, int day) => plan.Built.Days[day - 1].Items.First(i => i.Kind == ItemKind.Sight);

    private static IEnumerable<string?> Ids(Plan plan) => plan.Built.Days.SelectMany(d => d.Items).Select(i => i.Ref?.Id);

    private static bool Has(Plan plan, string id) => Ids(plan).Contains(id, StringComparer.Ordinal);

    private static bool Is(PlanItem item, string id) => string.Equals(item.Ref?.Id, id, StringComparison.Ordinal);

    [Fact]
    public void A_replacement_takes_the_item_s_place_and_the_day_is_timed_again()
    {
        var pools = Pools();
        var plan = NewPlan(pools);
        var old = FirstSight(plan, 2);
        var alternative = DayAlternatives.For(plan, pools, 2, old.Ref!.Id, AlternativeKind.Nature).Items[0].Item;

        var changed = plan.ChangeDay(PlanEdit.Replace(2, old.Ref.Id, alternative), pools, Now);

        var day = changed.Built.Days[1];
        Assert.False(Has(changed, old.Ref.Id));
        var added = Assert.Single(day.Items, i => Is(i, alternative.Id));
        Assert.Equal(ItemKind.Nature, added.Kind);
        Assert.Equal(PlanRules.NatureMinutes, added.Minutes);
        Assert.Single(changed.Edits);
        Assert.True(day.Items.Zip(day.Items.Skip(1)).All(p => p.First.End <= p.Second.Start), "items never overlap");
    }

    [Fact]
    public void A_removal_leaves_free_time_and_is_not_refilled()
    {
        var pools = Pools();
        var plan = NewPlan(pools);
        var old = FirstSight(plan, 2);
        var places = plan.Built.Days[1].Items.Count(IsPlace);

        var changed = plan.ChangeDay(PlanEdit.Remove(2, old.Ref!.Id), pools, Now);

        Assert.False(Has(changed, old.Ref.Id));
        Assert.Equal(places - 1, changed.Built.Days[1].Items.Count(IsPlace));
        Assert.Contains(changed.Built.Days[1].Items, i => i.Kind == ItemKind.FreeTime);
    }

    [Fact]
    public void An_addition_needs_a_free_slot_for_the_pace()
    {
        var pools = Pools();
        var plan = NewPlan(pools);
        var alternative = DayAlternatives.For(plan, pools, 2, null, AlternativeKind.Sight).Items[0].Item;

        var error = Assert.Throws<DayFullException>(() => plan.ChangeDay(PlanEdit.Add(2, alternative), pools, Now));
        Assert.Equal("Day 2 is full for a balanced pace. Replace a place instead.", error.Message);

        // A removal frees the slot.
        var freed = plan.ChangeDay(PlanEdit.Remove(2, FirstSight(plan, 2).Ref!.Id), pools, Now);
        var added = freed.ChangeDay(PlanEdit.Add(2, alternative), pools, Now);
        Assert.Contains(added.Built.Days[1].Items, i => Is(i, alternative.Id));
        Assert.Equal(2, added.Edits.Count);
    }

    [Fact]
    public void A_meal_is_replaced_by_a_restaurant_and_the_reason_rule_is_named()
    {
        var pools = Pools();
        var plan = NewPlan(pools);
        var lunch = plan.Built.Days[1].Items.First(i => i.Kind == ItemKind.Restaurant);
        var restaurant = DayAlternatives.For(plan, pools, 2, lunch.Ref!.Id, null);
        Assert.Equal(AlternativeKind.Restaurant, restaurant.Kind);

        var changed = plan.ChangeDay(PlanEdit.Replace(2, lunch.Ref.Id, restaurant.Items[0].Item), pools, Now);
        Assert.Contains(changed.Built.Days[1].Items, i => i.Name.EndsWith(restaurant.Items[0].Item.Name, StringComparison.Ordinal));

        var error = Assert.Throws<DomainException>(() => DayAlternatives.For(plan, pools, 2, lunch.Ref.Id, AlternativeKind.Sight));
        Assert.Equal("kind", error.Field);
    }

    [Fact]
    public void An_event_is_replaced_by_another_event_of_that_date()
    {
        var pools = TheseEvents();
        var plan = NewPlan(pools);
        var ev = plan.Built.Days[1].Items.Single(i => i.Kind == ItemKind.Event);
        Assert.Equal("ev:1", ev.Ref!.Id);

        var other = DayAlternatives.For(plan, pools, 2, "ev:1", null);
        Assert.Equal("ev:2", Assert.Single(other.Items).Item.Id);

        var changed = plan.ChangeDay(PlanEdit.Replace(2, "ev:1", other.Items[0].Item), pools, Now);
        Assert.Equal("ev:2", changed.Built.Days[1].Items.Single(i => i.Kind == ItemKind.Event).Ref!.Id);
        Assert.Equal(40 * 2, changed.Built.Total.Events);
    }

    [Fact]
    public void Edits_last_through_a_swap_and_the_same_input_gives_the_same_plan()
    {
        var pools = Pools();
        var plan = NewPlan(pools);
        var old = FirstSight(plan, 3);
        var nature = DayAlternatives.For(plan, pools, 3, old.Ref!.Id, AlternativeKind.Nature).Items[0].Item;
        var changed = plan.ChangeDay(PlanEdit.Replace(3, old.Ref.Id, nature), pools, Now);

        var swapped = changed.Swap(OfferKind.Hotel, "bk:2", pools, Now);
        var again = changed.Swap(OfferKind.Hotel, "bk:2", pools, Now);

        Assert.Equal(changed.Edits, swapped.Edits);
        Assert.Equal(
            swapped.Built.Days.SelectMany(d => d.Items).Select(i => (i.Ref?.Id, i.Start)),
            again.Built.Days.SelectMany(d => d.Items).Select(i => (i.Ref?.Id, i.Start)));
    }

    [Fact]
    public void A_rebuild_for_fewer_days_drops_the_edits_of_days_that_are_gone_with_a_note()
    {
        var pools = Pools();
        var plan = NewPlan(pools);
        var old = FirstSight(plan, 4);
        var nature = DayAlternatives.For(plan, pools, 4, old.Ref!.Id, AlternativeKind.Nature).Items[0].Item;
        var changed = plan.ChangeDay(PlanEdit.Replace(4, old.Ref.Id, nature), pools, Now).Rename("Rome with Maya", Now);

        var rebuilt = changed.Rebuilt(Trip(days: 3), Alternatives(), pools, Now);

        Assert.Equal(("p1", "Rome with Maya"), (rebuilt.Id, rebuilt.Name));
        Assert.Equal(3, rebuilt.Built.Days.Count);
        Assert.Empty(rebuilt.Edits);
        Assert.Contains("Some of your changes to the days no longer fit the plan and were dropped.", rebuilt.Built.Notes, StringComparer.Ordinal);
    }

    [Fact]
    public void Replacing_an_added_item_changes_the_addition_and_removing_it_drops_it()
    {
        var pools = Pools();
        var plan = NewPlan(pools);
        var freed = plan.ChangeDay(PlanEdit.Remove(2, FirstSight(plan, 2).Ref!.Id), pools, Now);
        var first = DayAlternatives.For(freed, pools, 2, null, AlternativeKind.Sight).Items[0].Item;
        var added = freed.ChangeDay(PlanEdit.Add(2, first), pools, Now);
        var second = DayAlternatives.For(added, pools, 2, first.Id, AlternativeKind.Sight).Items[0].Item;

        var replaced = added.ChangeDay(PlanEdit.Replace(2, first.Id, second), pools, Now);
        Assert.Equal(2, replaced.Edits.Count);
        Assert.Equal(second.Id, replaced.Edits[^1].NewItem!.Id);
        Assert.Equal(EditAction.Add, replaced.Edits[^1].Action);

        var dropped = replaced.ChangeDay(PlanEdit.Remove(2, second.Id), pools, Now);
        Assert.Single(dropped.Edits);
        Assert.False(Has(dropped, second.Id));
    }

    [Fact]
    public void Alternatives_are_nearest_first_at_most_six_and_never_already_in_the_plan()
    {
        var pools = Pools();
        var plan = NewPlan(pools);
        var old = FirstSight(plan, 2);

        var result = DayAlternatives.For(plan, pools, 2, old.Ref!.Id, null);

        Assert.Equal(AlternativeKind.Sight, result.Kind);
        Assert.Equal(DayAlternatives.Max, result.Items.Count);
        Assert.DoesNotContain(result.Items, a => Has(plan, a.Item.Id));
        Assert.Equal(result.Items.Select(a => a.WalkKmFromDay).Order(), result.Items.Select(a => a.WalkKmFromDay));
    }

    [Fact]
    public void Alternatives_name_the_field_at_fault_and_say_why_there_are_none()
    {
        var pools = Pools(nightlife: 0);
        var plan = NewPlan(pools);

        Assert.Equal("day", Assert.Throws<DomainException>(() => DayAlternatives.For(plan, pools, 9, null, null)).Field);
        Assert.Equal("itemId", Assert.Throws<DomainException>(() => DayAlternatives.For(plan, pools, 2, "nope", null)).Field);
        var none = DayAlternatives.For(plan, pools, 2, null, AlternativeKind.Nightlife);
        Assert.Empty(none.Items);
        Assert.Equal("No other places for an evening out are known near day 2.", none.Message);

        Assert.Equal("id", Assert.Throws<DomainException>(() => DayAlternatives.Find(plan, pools, 2, null, "sight:1", isEvent: false)).Field);
        Assert.Equal("day", Assert.Throws<DomainException>(() => plan.ChangeDay(PlanEdit.Remove(7, "x"), pools, Now)).Field);
    }

    [Fact]
    public void Find_returns_the_alternative_whatever_its_kind()
    {
        var pools = Pools();
        var plan = NewPlan(pools);
        var old = FirstSight(plan, 2);
        var nature = DayAlternatives.For(plan, pools, 2, old.Ref!.Id, AlternativeKind.Nature).Items[0].Item;

        var found = DayAlternatives.Find(plan, pools, 2, old.Ref.Id, nature.Id, isEvent: false);

        Assert.Equal(PlaceCategory.Nature, found.Place!.Category);
    }
}
