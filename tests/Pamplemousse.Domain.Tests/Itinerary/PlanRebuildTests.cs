using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Flights;
using Pamplemousse.Domain.Hotels;
using Pamplemousse.Domain.Itinerary;
using Pamplemousse.Domain.Ranking;
using Pamplemousse.Domain.Travellers;
using static Pamplemousse.Domain.Tests.Itinerary.PlanFixtures;

namespace Pamplemousse.Domain.Tests.Itinerary;

/// <summary>Spec 010 FR-012a and research R-5: a rebuild keeps the traveller's choices and day edits.</summary>
public class PlanRebuildTests
{
    private static readonly DateTimeOffset Now = new(2027, 3, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly TravellerId Owner = new("uid-1");

    private static Ranked<FlightOffer> Fare(string id, string number, double match) =>
        Flight(id, match) is var f ? f with { Offer = f.Offer with { FlightNumber = number } } : null!;

    private static PlanAlternatives Alternatives(
        IReadOnlyList<Ranked<HotelOffer>>? hotels = null,
        IReadOnlyList<Ranked<FlightOffer>>? flights = null) => new(
        hotels ?? [Hotel("bk:1", 0.9, 500), Hotel("bk:2", 0.7, 300, at: new GeoPoint(41.92, 12.47))],
        flights ?? [Fare("tp:1", "AZ1", 0.8), Fare("tp:2", "ME231", 0.6)],
        null,
        Now,
        Now,
        null);

    private static Plan NewPlan(PlacePools pools) => Plan.Create("p1", Owner, Trip(), Alternatives(), pools, Now);

    private static Plan Chosen(PlacePools pools) =>
        NewPlan(pools).Swap(OfferKind.Hotel, "bk:2", pools, Now).Swap(OfferKind.Flight, "tp:2", pools, Now);

    private static bool OnDay(Plan plan, int day, string id) =>
        plan.Built.Days[day - 1].Items.Any(i => string.Equals(i.Ref?.Id, id, StringComparison.Ordinal));

    [Fact]
    public void The_hotel_and_flight_the_traveller_swapped_in_are_kept()
    {
        var pools = Pools();

        var rebuilt = Chosen(pools).Rebuilt(Trip(adults: 3), Alternatives(), pools, Now);

        Assert.Equal(("bk:2", "tp:2"), (rebuilt.HotelId, rebuilt.FlightId));
        Assert.Equal(3, rebuilt.Request.Adults);
    }

    [Fact]
    public void Offers_the_traveller_left_as_they_were_are_chosen_again_as_the_best()
    {
        var pools = Pools();
        var better = Alternatives(
            hotels: [Hotel("bk:2", 0.95, 300), Hotel("bk:1", 0.9, 500)],
            flights: [Fare("tp:2", "ME231", 0.9), Fare("tp:1", "AZ1", 0.8)]);

        var rebuilt = NewPlan(pools).Rebuilt(Trip(weights: W(budget: 100)), better, pools, Now);

        Assert.Equal(("bk:2", "tp:2"), (rebuilt.HotelId, rebuilt.FlightId));
    }

    [Fact]
    public void A_choice_no_longer_offered_gives_way_to_the_best_with_a_note()
    {
        var pools = Pools();
        var without = Alternatives(hotels: [Hotel("bk:1", 0.9, 500), Hotel("bk:3", 0.6, 280)], flights: [Fare("tp:1", "AZ1", 0.8)]);

        var rebuilt = Chosen(pools).Rebuilt(Trip(), without, pools, Now);

        Assert.Equal(("bk:1", "tp:1"), (rebuilt.HotelId, rebuilt.FlightId));
        Assert.Contains("The hotel you chose isn't offered for this trip any more, so the best match was chosen.", rebuilt.Built.Notes, StringComparer.Ordinal);
        Assert.Contains("The flight you chose isn't offered for this trip any more, so the best match was chosen.", rebuilt.Built.Notes, StringComparer.Ordinal);
    }

    [Fact]
    public void For_new_dates_the_same_flight_is_found_by_its_number()
    {
        var pools = Pools();
        var later = Alternatives(flights: [Fare("tp:3", "AZ1", 0.9), Fare("tp:4", "ME231", 0.7)]);

        var rebuilt = Chosen(pools).Rebuilt(Trip(days: 4), later, pools, Now);

        Assert.Equal("tp:4", rebuilt.FlightId);
    }

    [Fact]
    public void A_car_is_kept_for_the_same_dates_and_removed_with_a_note_for_new_ones()
    {
        var pools = Pools();
        var withCar = NewPlan(pools).WithCars([Car("car:1"), Car("car:2", 0.5, 90)], Now).Swap(OfferKind.Car, "car:2", pools, Now);

        var sameDates = withCar.Rebuilt(Trip(adults: 1), Alternatives(), pools, Now);
        var newDates = withCar.Rebuilt(Trip(days: 4), Alternatives(), pools, Now);

        Assert.Equal("car:2", sameDates.CarId);
        Assert.NotNull(sameDates.Offers.Car);
        Assert.Null(newDates.CarId);
        Assert.Contains("Car prices depend on the dates, so your car was removed. Add one again for the new dates.", newDates.Built.Notes, StringComparer.Ordinal);
    }

    [Theory]
    [InlineData(100, 0)]
    [InlineData(0, 100)]
    [InlineData(50, 50)]
    public void A_replaced_place_stays_on_its_day_when_the_rebuild_chooses_other_places(int culture, int nature)
    {
        var pools = Pools();
        var plan = NewPlan(pools);
        var old = plan.Built.Days[1].Items.First(i => i.Kind == ItemKind.Sight);
        var park = DayAlternatives.For(plan, pools, 2, old.Ref!.Id, AlternativeKind.Nature).Items[0].Item;
        var changed = plan.ChangeDay(PlanEdit.Replace(2, old.Ref.Id, park), pools, Now);

        var rebuilt = changed.Rebuilt(Trip(weights: W(culture: culture, nature: nature)), Alternatives(), pools, Now);

        Assert.True(OnDay(rebuilt, 2, park.Id), "the traveller's place is still on day 2");
        Assert.DoesNotContain(rebuilt.Built.Days.SelectMany(d => d.Items), i => string.Equals(i.Ref?.Id, old.Ref.Id, StringComparison.Ordinal));
        Assert.Single(rebuilt.Edits);
        Assert.DoesNotContain("Some of your changes to the days no longer fit the plan and were dropped.", rebuilt.Built.Notes, StringComparer.Ordinal);
    }

    [Fact]
    public void A_removal_and_a_restaurant_choice_last_through_a_rebuild()
    {
        var pools = Pools();
        var plan = NewPlan(pools);
        var gone = plan.Built.Days[2].Items.First(i => i.Kind == ItemKind.Sight).Ref!.Id;
        var lunch = plan.Built.Days[1].Items.First(i => i.Kind == ItemKind.Restaurant).Ref!.Id;
        var restaurant = DayAlternatives.For(plan, pools, 2, lunch, null).Items[0].Item;
        var changed = plan.ChangeDay(PlanEdit.Remove(3, gone), pools, Now).ChangeDay(PlanEdit.Replace(2, lunch, restaurant), pools, Now);

        var rebuilt = changed.Rebuilt(Trip(weights: W(culture: 100, nature: 0, food: 90)), Alternatives(), pools, Now);

        Assert.False(OnDay(rebuilt, 3, gone));
        Assert.True(OnDay(rebuilt, 2, restaurant.Id), "the chosen restaurant is still on day 2");
        Assert.Equal(2, rebuilt.Edits.Count);
    }

    [Fact]
    public void The_same_rebuild_gives_the_same_plan()
    {
        var pools = Pools();
        var plan = Chosen(pools);
        var old = plan.Built.Days[1].Items.First(i => i.Kind == ItemKind.Sight);
        var park = DayAlternatives.For(plan, pools, 2, old.Ref!.Id, AlternativeKind.Nature).Items[0].Item;
        var changed = plan.ChangeDay(PlanEdit.Replace(2, old.Ref.Id, park), pools, Now);
        var trip = Trip(weights: W(culture: 100, nature: 0));

        var once = changed.Rebuilt(trip, Alternatives(), pools, Now);
        var twice = changed.Rebuilt(trip, Alternatives(), pools, Now);

        Assert.Equal(
            once.Built.Days.SelectMany(d => d.Items).Select(i => (i.Ref?.Id, i.Start)),
            twice.Built.Days.SelectMany(d => d.Items).Select(i => (i.Ref?.Id, i.Start)));
    }
}
