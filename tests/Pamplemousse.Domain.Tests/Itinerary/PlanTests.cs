using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Exceptions;
using Pamplemousse.Domain.Itinerary;
using Pamplemousse.Domain.Ranking;
using Pamplemousse.Domain.Travellers;
using static Pamplemousse.Domain.Tests.Itinerary.PlanFixtures;

namespace Pamplemousse.Domain.Tests.Itinerary;

public class PlanTests
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

    private static Plan NewPlan() => Plan.Create("p1", Owner, Trip(), Alternatives(), Pools(), Now);

    [Fact]
    public void A_new_plan_is_a_draft_with_the_best_hotel_and_flight_and_no_car()
    {
        var plan = NewPlan();

        Assert.Equal("Rome, 5 days", plan.Name);
        Assert.Equal(PlanStatus.Draft, plan.Status);
        Assert.Equal("bk:1", plan.HotelId);
        Assert.Equal("tp:1", plan.FlightId);
        Assert.Null(plan.CarId);
        Assert.Equal(5, plan.Built.Days.Count);
        Assert.Equal(Now, plan.CreatedAt);
    }

    [Fact]
    public void A_one_day_plan_is_named_in_the_singular()
    {
        Assert.Equal("Rome, 1 day", Plan.DefaultName(Trip(days: 1)));
    }

    [Fact]
    public void Only_the_top_20_of_each_list_are_kept()
    {
        var many = Enumerable.Range(1, 30).Select(i => Hotel($"bk:{i}")).ToList();
        var plan = Plan.Create("p1", Owner, Trip(), Alternatives() with { Hotels = many }, Pools(), Now);

        Assert.Equal(PlanAlternatives.Max, plan.Alternatives.Hotels.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void A_name_needs_1_to_60_characters(string? name)
    {
        var ex = Assert.Throws<DomainException>(() => NewPlan().Rename(name, Now));
        Assert.Equal("name", ex.Field);
        Assert.Throws<DomainException>(() => NewPlan().Rename(new string('a', 61), Now));
    }

    [Fact]
    public void Rename_and_status_change_the_plan()
    {
        var later = Now.AddHours(1);
        var plan = NewPlan().Rename("  Rome with Maya ", later).WithStatus("Planned", later);

        Assert.Equal("Rome with Maya", plan.Name);
        Assert.Equal(PlanStatus.Planned, plan.Status);
        Assert.Equal(later, plan.UpdatedAt);
        Assert.Equal("status", Assert.Throws<DomainException>(() => plan.WithStatus("booked", later)).Field);
    }

    [Fact]
    public void Swapping_the_hotel_rebuilds_days_total_and_match()
    {
        var plan = NewPlan();
        var swapped = plan.Swap(OfferKind.Hotel, "bk:2", Pools(), Now);

        Assert.Equal("bk:2", swapped.HotelId);
        Assert.Equal(700, swapped.Built.Total.Amount);
        Assert.NotEqual(plan.Built.Match!.Total, swapped.Built.Match!.Total);
        Assert.NotEqual(plan.Built.Days.Sum(d => d.WalkKm), swapped.Built.Days.Sum(d => d.WalkKm));
    }

    [Fact]
    public void Swapping_the_flight_uses_the_new_fare()
    {
        var swapped = NewPlan().Swap(OfferKind.Flight, "tp:2", Pools(), Now);

        Assert.Equal("tp:2", swapped.FlightId);
        Assert.Equal(300, swapped.Built.Total.Flight);
    }

    [Fact]
    public void Only_an_offer_from_the_kept_list_can_be_swapped_in()
    {
        var ex = Assert.Throws<DomainException>(() => NewPlan().Swap(OfferKind.Hotel, "bk:99", Pools(), Now));
        Assert.Equal("offerId", ex.Field);
        Assert.Throws<DomainException>(() => NewPlan().Swap(OfferKind.Car, "car:1", Pools(), Now));
    }

    [Fact]
    public void A_car_is_added_from_the_kept_cars_then_removed()
    {
        var plan = NewPlan().WithCars([Car("car:1"), Car("car:2", 0.5, 90)], Now);

        var withCar = plan.Swap(OfferKind.Car, "car:2", Pools(), Now);
        Assert.Equal(90, withCar.Built.Total.Car);
        Assert.Equal(Now, withCar.Alternatives.CarsCheckedAt);

        var without = withCar.RemoveCar(Pools(), Now);
        Assert.Null(without.CarId);
        Assert.Null(without.Built.Total.Car);
    }

    [Fact]
    public void When_the_places_cant_be_fetched_a_swap_keeps_the_days()
    {
        var plan = NewPlan();
        var swapped = plan.Swap(OfferKind.Hotel, "bk:2", null, Now);

        Assert.Same(plan.Built.Days, swapped.Built.Days);
        Assert.Equal(700, swapped.Built.Total.Amount);
        Assert.Contains(swapped.Built.Warnings, w => w.StartsWith("The places couldn't be loaded", StringComparison.Ordinal));
    }

    [Fact]
    public void A_fare_without_a_time_uses_the_fastest_on_the_same_route()
    {
        var alternatives = Alternatives() with { Flights = [Flight("tp:1", minutes: null), Flight("tp:2", minutes: 180), Flight("tp:3", minutes: 210)] };
        var plan = Plan.Create("p1", Owner, Trip(), alternatives, Pools(), Now);

        Assert.Equal(180, plan.Offers.FallbackFlightMinutes);
    }

    [Fact]
    public void The_51st_plan_is_refused()
    {
        Plan.EnsureRoomFor(49);
        Assert.Throws<PlanLimitException>(() => Plan.EnsureRoomFor(50));
    }

    [Fact]
    public void A_trip_request_names_the_field_at_fault()
    {
        var dates = DateRange.ForTrip(From, From.AddDays(4), Today);
        Assert.Equal("adults", Assert.Throws<DomainException>(() => TripRequest.Create(Rome, dates, 9, "USD", null, Weights.Neutral, Pace.Balanced, null, TimeSpan.Zero)).Field);
        Assert.Equal("currency", Assert.Throws<DomainException>(() => TripRequest.Create(Rome, dates, 2, "US", null, Weights.Neutral, Pace.Balanced, null, TimeSpan.Zero)).Field);
        Assert.Equal("tripBudget", Assert.Throws<DomainException>(() => TripRequest.Create(Rome, dates, 2, "USD", null, Weights.Neutral, Pace.Balanced, 0, TimeSpan.Zero)).Field);

        var long31 = DateRange.ForTrip(From, From.AddDays(30), Today);
        Assert.Equal("to", Assert.Throws<DomainException>(() => TripRequest.Create(Rome, long31, 2, "USD", null, Weights.Neutral, Pace.Balanced, null, TimeSpan.Zero)).Field);

        var ok = TripRequest.Create(Rome, dates, null, " eur ", " ", Weights.Neutral, Pace.Packed, 900, TimeSpan.Zero);
        Assert.Equal((2, "EUR", (string?)null, 5), (ok.Adults, ok.Currency, ok.OriginId, ok.Days));
    }

    [Theory]
    [InlineData(null, Pace.Balanced)]
    [InlineData("relaxed", Pace.Relaxed)]
    [InlineData("PACKED", Pace.Packed)]
    [InlineData(" balanced ", Pace.Balanced)]
    public void Pace_reads_its_three_levels(string? text, Pace pace)
    {
        Assert.Equal(pace, PaceRules.Parse(text));
        Assert.Equal(pace.ToString().ToLowerInvariant(), pace.ToText());
    }

    [Fact]
    public void An_unknown_pace_names_the_field()
    {
        Assert.Equal("pace", Assert.Throws<DomainException>(() => PaceRules.Parse("fast")).Field);
    }
}
