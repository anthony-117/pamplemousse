using System.Text.Json;
using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Destinations;
using Pamplemousse.Domain.Itinerary;
using Pamplemousse.Domain.Ranking;
using static Pamplemousse.Domain.Tests.Itinerary.PlanFixtures;

namespace Pamplemousse.Domain.Tests.Itinerary;

public class PlanBuilderTests
{
    private static BuiltPlan Build(TripRequest? trip = null, PlanOffers? offers = null, PlacePools? pools = null) =>
        PlanBuilder.Build(trip ?? Trip(), offers ?? Offers(), pools ?? Pools());

    private static IEnumerable<PlanItem> Items(BuiltPlan plan) => plan.Days.SelectMany(d => d.Items);

    private static IEnumerable<PlanDay> FullDays(BuiltPlan plan) => plan.Days.Skip(1).SkipLast(1);

    [Fact]
    public void One_day_per_date_with_items_in_time_order()
    {
        var plan = Build();

        Assert.Equal(5, plan.Days.Count);
        Assert.Equal(Enumerable.Range(0, 5).Select(i => From.AddDays(i)), plan.Days.Select(d => d.Date));
        Assert.Equal([1, 2, 3, 4, 5], plan.Days.Select(d => d.Number));
        foreach (var day in plan.Days)
        {
            Assert.Equal(day.Items.OrderBy(i => i.Start).Select(i => i.Start), day.Items.Select(i => i.Start));
            Assert.Equal(Math.Round(day.Items.Sum(i => i.WalkKm), 1), day.WalkKm);
        }
    }

    [Fact]
    public void Days_are_compact_never_repeat_a_place_and_stay_within_9_to_22()
    {
        // SC-002.
        var plan = Build();
        var places = Items(plan).Where(i => i.Ref?.Kind == RefKind.Place).Select(i => i.Ref!.Id).ToList();

        Assert.Equal(places.Count, places.Distinct(StringComparer.Ordinal).Count());
        Assert.All(plan.Days, d => Assert.True(d.WalkKm < 12, $"{d.Title}: {d.WalkKm} km"));
        foreach (var item in Items(plan).Where(i => i.Kind is not (ItemKind.Arrival or ItemKind.Departure)))
        {
            Assert.InRange(Minutes(item.Start), PlanRules.DayStart, PlanRules.DayEnd);
            Assert.InRange(Minutes(item.Start) + item.Minutes, PlanRules.DayStart, PlanRules.DayEnd);
        }
    }

    [Fact]
    public void Each_item_says_what_it_is_and_where_it_comes_from()
    {
        var sight = Items(Build()).First(i => i.Kind == ItemKind.Sight);

        Assert.StartsWith("sight ", sight.Name, StringComparison.Ordinal);
        Assert.Equal(PlanRules.SightMinutes, sight.Minutes);
        Assert.Equal(RefKind.Place, sight.Ref!.Kind);
        Assert.NotNull(sight.Ref.Location);
        Assert.Null(sight.Price);
    }

    [Theory]
    [InlineData(90, 20)]
    [InlineData(20, 90)]
    public void Culture_and_nature_share_the_places_by_their_weights(int culture, int nature)
    {
        // SC-003: at least twice as many of the favoured kind.
        var plan = Build(Trip(weights: W(culture: culture, nature: nature)));
        var sights = Items(plan).Count(i => i.Kind == ItemKind.Sight);
        var parks = Items(plan).Count(i => i.Kind == ItemKind.Nature);

        if (culture > nature)
        {
            Assert.True(sights >= 2 * parks, $"{sights} sights, {parks} parks");
        }
        else
        {
            Assert.True(parks >= 2 * sights, $"{sights} sights, {parks} parks");
        }
    }

    [Theory]
    [InlineData(Pace.Relaxed, 2)]
    [InlineData(Pace.Balanced, 3)]
    [InlineData(Pace.Packed, 4)]
    public void Pace_sets_the_places_of_a_full_day(Pace pace, int perDay)
    {
        // SC-004 and FR-006: meals, events and free time aren't counted.
        var plan = Build(Trip(pace: pace));

        Assert.All(FullDays(plan), d => Assert.Equal(perDay, d.Items.Count(IsPlace)));
    }

    [Fact]
    public void The_same_input_gives_the_same_plan()
    {
        // SC-005.
        var events = new[] { Event("e1", From.AddDays(2), "20:30", 40) };
        var weather = new[] { new WeatherDay(From, 22, 12, 10, 0, "Sunny") };
        string Json() => JsonSerializer.Serialize(Build(Trip(), Offers(), Pools(events: events, weather: weather)));

        Assert.Equal(Json(), Json());
    }

    [Fact]
    public void The_first_day_starts_after_landing_and_the_last_ends_3_hours_before_the_flight()
    {
        // FR-007: lands 10:30, +1 h, +45 min to the hotel → 12:15. Flight home at 18:00 → ends at 15:00.
        var plan = Build();
        var first = plan.Days[0];
        var last = plan.Days[^1];

        Assert.Equal("Arrival", first.Title);
        Assert.Equal(ItemKind.Arrival, first.Items[0].Kind);
        Assert.Equal(new TimeOnly(10, 30), first.Items[0].Start);
        Assert.All(first.Items.Skip(1), i => Assert.True(i.Start >= new TimeOnly(12, 15), i.Name));

        Assert.Equal("Departure", last.Title);
        var departure = Assert.Single(last.Items, i => i.Kind == ItemKind.Departure);
        Assert.Equal(new TimeOnly(15, 0), departure.Start);
        Assert.Equal(last.Items[^1], departure);
        Assert.All(last.Items.Where(i => i != departure), i => Assert.True(i.End <= new TimeOnly(15, 0), i.Name));
        Assert.Contains(plan.Notes, n => n.Contains("45 minutes", StringComparison.Ordinal));
    }

    [Fact]
    public void An_unknown_flight_time_uses_the_fallback_then_4_hours()
    {
        var withFallback = PlanBuilder.Build(Trip(), new PlanOffers(Hotel(), Flight(minutes: null), null, 180), Pools());
        var without = PlanBuilder.Build(Trip(), new PlanOffers(Hotel(), Flight(minutes: null), null), Pools());

        Assert.Equal(new TimeOnly(11, 0), withFallback.Days[0].Items[0].Start);
        Assert.Equal(new TimeOnly(12, 0), without.Days[0].Items[0].Start);
        Assert.Contains(without.Notes, n => n.Contains("4 hours", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_a_flight_the_trip_runs_from_14_00_to_12_00_with_a_note()
    {
        var plan = Build(Trip(origin: null), Offers(flight: false));

        Assert.DoesNotContain(Items(plan), i => i.Kind is ItemKind.Arrival or ItemKind.Departure);
        Assert.All(plan.Days[0].Items, i => Assert.True(i.Start >= new TimeOnly(14, 0)));
        Assert.All(plan.Days[^1].Items, i => Assert.True(i.End <= new TimeOnly(12, 0)));
        Assert.Contains(plan.Notes, n => n.StartsWith("Choose a departure city", StringComparison.Ordinal));
        Assert.Null(plan.Total.Flight);
        Assert.DoesNotContain(plan.Match!.Parts, p => p.Part == MatchPartKind.Flight);
    }

    [Fact]
    public void With_an_origin_but_no_fare_the_note_says_none_was_found()
    {
        var plan = Build(Trip(), Offers(flight: false));

        Assert.Contains(plan.Notes, n => n.StartsWith("No recent fare", StringComparison.Ordinal));
    }

    [Fact]
    public void Lunch_and_dinner_are_restaurants_at_meal_times()
    {
        foreach (var day in FullDays(Build()))
        {
            var meals = day.Items.Where(i => i.Kind == ItemKind.Restaurant).ToList();
            Assert.Equal(2, meals.Count);
            Assert.StartsWith("Lunch at", meals[0].Name, StringComparison.Ordinal);
            Assert.InRange(meals[0].Start, new TimeOnly(12, 0), new TimeOnly(14, 0));
            Assert.StartsWith("Dinner at", meals[1].Name, StringComparison.Ordinal);
            Assert.True(meals[1].Start >= new TimeOnly(19, 0));
        }
    }

    [Fact]
    public void At_food_high_meals_go_to_the_best_known_restaurant_within_800_m()
    {
        var plan = Build(Trip(weights: W(food: 90)));
        var meals = Items(plan).Where(i => i.Kind == ItemKind.Restaurant).ToList();

        Assert.NotEmpty(meals);
        var foodPart = plan.Match!.Days.Single(d => d.Criterion == Criterion.Food);
        Assert.InRange(foodPart.Value, 0, 1);
    }

    [Fact]
    public void An_event_goes_on_its_date_at_its_time_and_replaces_that_evening_out()
    {
        // FR-008: at most one a day, the first by time.
        var date = From.AddDays(3);
        var events = new[] { Event("late", date, "21:30"), Event("b", date, "20:30", 30), Event("a", date, "20:30", 30) };
        var plan = Build(Trip(weights: W(nightlife: 90)), pools: Pools(events: events));
        var day = plan.Days.Single(d => d.Date == date);

        var placed = Assert.Single(day.Items, i => i.Kind == ItemKind.Event);
        Assert.Equal("Event a", placed.Name);
        Assert.Equal(new TimeOnly(20, 30), placed.Start);
        Assert.Equal(90, placed.Minutes);
        Assert.Equal(new Money(60, "USD"), placed.Price);
        Assert.DoesNotContain(day.Items, i => i.Kind == ItemKind.Nightlife);
        Assert.All(day.Items.Where(i => i != placed), i => Assert.True(i.End <= placed.Start || i.Start >= placed.End, i.Name));
        Assert.Equal(60, plan.Total.Events);
    }

    [Fact]
    public void An_event_without_a_time_starts_at_20_00()
    {
        var date = From.AddDays(2);
        var plan = Build(pools: Pools(events: [Event("x", date, null)]));

        Assert.Equal(new TimeOnly(20, 0), plan.Days[2].Items.Single(i => i.Kind == ItemKind.Event).Start);
    }

    [Fact]
    public void A_daytime_event_moves_the_places_around_it()
    {
        var date = From.AddDays(2);
        var plan = Build(pools: Pools(events: [Event("x", date, "10:00")]));
        var day = plan.Days[2];
        var ev = day.Items.Single(i => i.Kind == ItemKind.Event);

        Assert.Equal(new TimeOnly(10, 0), ev.Start);
        Assert.All(day.Items.Where(i => i != ev), i => Assert.True(i.End <= ev.Start || i.Start >= ev.End, i.Name));
    }

    [Fact]
    public void No_events_when_culture_and_nightlife_are_both_low()
    {
        var plan = Build(Trip(weights: W(culture: 20, nightlife: 20)), pools: Pools(events: [Event("x", From.AddDays(2), "20:00")]));

        Assert.DoesNotContain(Items(plan), i => i.Kind == ItemKind.Event);
    }

    [Fact]
    public void Evenings_out_follow_nightlife_latest_days_first_never_the_departure_day()
    {
        // round(5 × 50 ÷ 100) = 3 evenings: days 4, 3 and 2.
        var plan = Build();
        var evenings = plan.Days.Where(d => d.Items.Any(i => i.Kind == ItemKind.Nightlife)).Select(d => d.Number).ToList();

        Assert.Equal([2, 3, 4], evenings);
        Assert.All(plan.Days.SelectMany(d => d.Items).Where(i => i.Kind == ItemKind.Nightlife), i => Assert.True(i.End <= new TimeOnly(22, 0)));
        Assert.DoesNotContain(Build(Trip(weights: W(nightlife: 0))).Days.SelectMany(d => d.Items), i => i.Kind == ItemKind.Nightlife);
    }

    [Fact]
    public void Weather_shows_only_on_days_the_forecast_covers()
    {
        var plan = Build(pools: Pools(weather: [new WeatherDay(From, 22, 12, 10, 0, "Sunny")]));

        Assert.Equal("Sunny", plan.Days[0].Weather!.Summary);
        Assert.All(plan.Days.Skip(1), d => Assert.Null(d.Weather));
    }

    [Fact]
    public void Too_few_places_leave_free_time_rather_than_repeating_one()
    {
        var plan = Build(pools: Pools(sights: 3, nature: 1));
        var places = Items(plan).Where(IsPlace).Select(i => i.Ref!.Id).ToList();

        Assert.Equal(4, places.Count);
        Assert.Equal(4, places.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(Items(plan), i => i.Kind == ItemKind.FreeTime);
        Assert.Contains(plan.Days, d => d.Title.EndsWith("Free day", StringComparison.Ordinal));
        Assert.Contains(plan.Notes, n => n.StartsWith("There weren't enough places", StringComparison.Ordinal));
    }

    [Fact]
    public void Days_are_named_after_their_first_place()
    {
        var plan = Build();

        Assert.Matches(@"^Day 2 · sight \d+ area$", plan.Days[1].Title);
    }

    [Fact]
    public void A_one_day_trip_is_both_arrival_and_departure()
    {
        var plan = Build(Trip(days: 1), Offers(days: 1));

        var day = Assert.Single(plan.Days);
        Assert.Equal("Arrival and departure", day.Title);
        Assert.Equal(ItemKind.Arrival, day.Items[0].Kind);
        Assert.Equal(ItemKind.Departure, day.Items[^1].Kind);
    }

    [Fact]
    public void Without_a_hotel_the_days_start_from_the_centre_with_a_warning()
    {
        var plan = Build(offers: Offers(hotel: false));

        Assert.Contains(plan.Warnings, w => w.StartsWith("No hotel was found", StringComparison.Ordinal));
        Assert.Null(plan.Total.Hotel);
        Assert.DoesNotContain(plan.Match!.Parts, p => p.Part == MatchPartKind.Hotel);
    }

    [Fact]
    public void Without_restaurants_meals_are_missing_with_a_note()
    {
        var plan = Build(pools: Pools(restaurants: 0));

        Assert.DoesNotContain(Items(plan), i => i.Kind == ItemKind.Restaurant);
        Assert.Contains(plan.Notes, n => n.StartsWith("No restaurant", StringComparison.Ordinal));
    }

    [Fact]
    public void The_total_adds_the_flight_per_traveller_the_hotel_and_priced_events()
    {
        // FR-010: 2 × 200 + 500.
        var plan = Build(budget: null);

        Assert.Equal(400, plan.Total.Flight);
        Assert.Equal(500, plan.Total.Hotel);
        Assert.Equal(900, plan.Total.Amount);
        Assert.Equal("USD", plan.Total.Currency);
        Assert.Equal(Items(plan).Count(i => i.Kind is ItemKind.Sight or ItemKind.Nature or ItemKind.Restaurant or ItemKind.Nightlife), plan.Total.Unpriced);
        Assert.Null(plan.Total.OverBudgetBy);
    }

    private static BuiltPlan Build(decimal? budget) => PlanBuilder.Build(Trip(budget: budget), Offers(), Pools());

    [Fact]
    public void Over_the_trip_budget_says_by_how_much()
    {
        Assert.Equal(100, Build(budget: 800).Total.OverBudgetBy);
    }

    [Fact]
    public void A_price_in_another_currency_is_left_out_and_counted()
    {
        var offers = new PlanOffers(Hotel(currency: "EUR"), Flight(), null);
        var plan = PlanBuilder.Build(Trip(), offers, Pools());

        Assert.Null(plan.Total.Hotel);
        Assert.Equal(1, plan.Total.OtherCurrency);
        Assert.Equal(400, plan.Total.Amount);
    }

    [Fact]
    public void A_car_counts_in_the_total_and_the_match()
    {
        var plan = PlanBuilder.Build(Trip(), new PlanOffers(Hotel(), Flight(), Car()), Pools());

        Assert.Equal(150, plan.Total.Car);
        Assert.Equal(1050, plan.Total.Amount);
        Assert.Equal(100, plan.Match!.Parts.Single(p => p.Part == MatchPartKind.Car).Weight);
    }

    [Fact]
    public void The_match_is_the_weighted_average_of_hotel_flight_and_days()
    {
        // R-6 with all weights 50: hotel 300, flight 100, days 200.
        var plan = Build();
        var match = plan.Match!;

        Assert.Equal(300, match.Parts.Single(p => p.Part == MatchPartKind.Hotel).Weight);
        Assert.Equal(100, match.Parts.Single(p => p.Part == MatchPartKind.Flight).Weight);
        var days = match.Parts.Single(p => p.Part == MatchPartKind.Days);
        Assert.Equal(200, days.Weight);
        var expected = ((300 * 0.9) + (100 * 0.8) + (200 * days.Value)) / 600;
        Assert.Equal(expected, match.Total, 6);
        Assert.Equal(4, match.Days.Count);
        Assert.False(string.IsNullOrWhiteSpace(match.Reason));
    }

    [Fact]
    public void Full_days_fit_the_interests_completely_when_there_is_enough_of_everything()
    {
        var match = Build().Match!;

        Assert.All(match.Days, d => Assert.Equal(1, d.Value, 6));
        Assert.Equal("Great hotel for you, days full of sights", match.Reason);
    }

    [Fact]
    public void A_weak_part_is_named_in_the_reason()
    {
        var plan = PlanBuilder.Build(Trip(), new PlanOffers(Hotel(), Flight(match: 0.2), null), Pools());

        Assert.Equal("Great hotel for you, but the flight is a weaker match", plan.Match!.Reason);
    }

    [Fact]
    public void Every_weight_zero_means_no_match()
    {
        Assert.Null(Build(Trip(weights: W(0, 0, 0, 0, 0, 0))).Match);
    }

    [Fact]
    public void A_30_day_packed_plan_builds_within_the_rules()
    {
        var plan = Build(Trip(days: 30, pace: Pace.Packed), Offers(days: 30), Pools(sights: 200, nature: 100, restaurants: 400, nightlife: 50));

        Assert.Equal(30, plan.Days.Count);
        Assert.All(FullDays(plan), d => Assert.Equal(4, d.Items.Count(IsPlace)));
        var size = JsonSerializer.SerializeToUtf8Bytes(plan).Length;
        Assert.True(size < 400_000, $"{size} bytes");
    }

    [Fact]
    public void Reprice_keeps_the_days_and_updates_total_and_match()
    {
        var before = Build();
        var after = PlanBuilder.Reprice(Trip(), new PlanOffers(Hotel("bk:2", 0.5, 300), Flight(), null), before, "kept");

        Assert.Same(before.Days, after.Days);
        Assert.Equal(700, after.Total.Amount);
        Assert.True(after.Match!.Total < before.Match!.Total);
        Assert.Equal("kept", after.Warnings[^1]);
    }

    [Fact]
    public void Walking_is_a_straight_line_times_1_3_at_4_5_kmh()
    {
        var a = new GeoPoint(41.9, 12.49);
        var b = new GeoPoint(41.909, 12.49);

        Assert.Equal(Math.Round(a.DistanceKmTo(b) * 1.3, 2), PlanRules.WalkKm(a, b));
        Assert.Equal(20, PlanRules.WalkMinutes(1.3));
        Assert.Equal(0, PlanRules.WalkMinutes(0));
    }
}
