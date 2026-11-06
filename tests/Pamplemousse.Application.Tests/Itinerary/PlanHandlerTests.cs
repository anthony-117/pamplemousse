using Microsoft.Extensions.Logging.Abstractions;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Application.Itinerary;
using Pamplemousse.Application.Ranking;
using Pamplemousse.Application.Tests.Destinations;
using Pamplemousse.Domain.Exceptions;
using Pamplemousse.Domain.Itinerary;
using Pamplemousse.Domain.Tests.Itinerary;
using Pamplemousse.Domain.Travellers;

namespace Pamplemousse.Application.Tests.Itinerary;

/// <summary>Build, read, change, swap and delete a plan (spec 008, Stories 1 to 3).</summary>
public class PlanHandlerTests
{
    private static readonly TravellerId Fares = new("uid-fares");
    private static readonly TravellerId Maya = new("uid-maya");

    private readonly FakePlanStore _store = new();
    private readonly FakeProfiles _profiles = new();
    private readonly PlanData _data = new();
    private readonly FakeHotelSearch _hotels = new();
    private readonly FakeFlightSearch _flights = new();
    private readonly FakeCarSearch _cars = new();
    private readonly List<FakePlaceLists> _places = FakePlaceLists.All();
    private readonly Fakes _free = new();
    private readonly FixedClock _clock = new();

    private PlanInputs Inputs => new(_data, _hotels, _flights, _cars, _places, _free, _free, _clock, NullLogger<PlanInputs>.Instance);

    private static BuildPlanCommand Command(
        TravellerId? owner = null,
        string destination = "3169070",
        string? origin = "276781",
        string? pace = null,
        RankingInput? preferences = null) =>
        new(owner ?? Fares, destination, new DateOnly(2027, 4, 10), new DateOnly(2027, 4, 14), 2, "usd", origin, preferences, pace, null);

    private Task<Plan> BuildAsync(BuildPlanCommand? command = null) =>
        new BuildPlanHandler(_store, _profiles, _data, Inputs, _clock).HandleAsync(command ?? Command(), CancellationToken.None);

    [Fact]
    public async Task A_plan_is_built_from_the_best_hotel_and_flight_and_saved_as_a_draft()
    {
        var plan = await BuildAsync();

        Assert.Equal(("Rome, 5 days", PlanStatus.Draft, "bk:1", "tp:1"), (plan.Name, plan.Status, plan.HotelId, plan.FlightId));
        Assert.Null(plan.CarId);
        Assert.Equal(5, plan.Built.Days.Count);
        Assert.Equal(FakeHotelSearch.FetchedAt, plan.Alternatives.HotelsCheckedAt);
        Assert.Equal("Booking.com", plan.Alternatives.HotelSource);
        Assert.Same(plan, await _store.GetAsync(Fares, plan.Id, CancellationToken.None));
        Assert.Equal(0, _cars.Calls);
    }

    [Fact]
    public async Task The_ranked_searches_are_asked_by_match_with_the_trip_preferences()
    {
        await BuildAsync(Command(preferences: new RankingInput(90, 90, null, 20, 20, null, null)));

        Assert.Equal("match", _hotels.Last?.Sort);
        Assert.Equal(new RankingInput(90, 90, 50, 20, 20, 50, null), _hotels.Last?.Ranking);
        Assert.Equal("USD", _hotels.Last?.Currency);
    }

    [Fact]
    public async Task Rome_flight_times_are_read_at_Rome_summer_time()
    {
        var plan = await BuildAsync();

        Assert.Equal(TimeSpan.FromHours(2), plan.Request.UtcOffset);
    }

    [Fact]
    public async Task Without_a_departure_city_there_is_no_flight_search()
    {
        var plan = await BuildAsync(Command(origin: null));

        Assert.Null(plan.FlightId);
        Assert.Equal(0, _flights.Calls);
        Assert.Contains(plan.Built.Notes, n => n.StartsWith("Choose a departure city", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Pace_left_out_is_the_profile_pace()
    {
        await _profiles.SaveAsync(TravelProfile.New(Fares, "Fares", null, _clock.GetUtcNow()) with { Pace = Pace.Packed }, CancellationToken.None);

        Assert.Equal(Pace.Packed, (await BuildAsync()).Request.Pace);
        Assert.Equal(Pace.Relaxed, (await BuildAsync(Command(pace: "relaxed"))).Request.Pace);
        Assert.Equal(Pace.Balanced, (await BuildAsync(Command(owner: Maya))).Request.Pace);
    }

    [Theory]
    [InlineData("1", "276781", null, "destinationId")]
    [InlineData("3169070", "1", null, "originId")]
    [InlineData("3169070", "276781", "fast", "pace")]
    public async Task A_bad_value_names_the_field(string destination, string origin, string? pace, string field)
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() => BuildAsync(Command(destination: destination, origin: origin, pace: pace)));

        Assert.Equal(field, ex.Field);
        Assert.Equal(0, _hotels.Calls);
    }

    [Fact]
    public async Task The_51st_plan_is_refused_before_any_source_is_asked()
    {
        for (var i = 0; i < Plan.MaxPlans; i++)
        {
            await _store.AddAsync(Plan.Create($"p{i}", Fares, PlanFixtures.Trip(), new([], [], null, null, null, null), PlanFixtures.Pools(), _clock.GetUtcNow()), CancellationToken.None);
        }

        await Assert.ThrowsAsync<PlanLimitException>(() => BuildAsync());
        Assert.Equal(0, _hotels.Calls);
    }

    [Fact]
    public async Task No_places_at_all_means_no_plan()
    {
        _places.ForEach(p => p.Fail = true);

        await Assert.ThrowsAsync<NoPlacesException>(() => BuildAsync());
        Assert.Equal(0, _store.Saves);
    }

    [Fact]
    public async Task A_failing_hotel_source_still_gives_a_plan_with_a_warning()
    {
        _hotels.Error = new HttpRequestException("down");

        var plan = await BuildAsync();

        Assert.Null(plan.HotelId);
        Assert.Contains(plan.Built.Warnings, w => string.Equals(w, "Hotels is unavailable right now.", StringComparison.Ordinal));
        Assert.Contains(plan.Built.Warnings, w => w.StartsWith("No hotel was found", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Another_travellers_plan_is_not_found()
    {
        var plan = await BuildAsync();

        await Assert.ThrowsAsync<NotFoundException>(() => new GetPlanHandler(_store).HandleAsync(new GetPlanQuery(Maya, plan.Id), CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(() => new DeletePlanHandler(_store).HandleAsync(new DeletePlanCommand(Maya, plan.Id), CancellationToken.None));
        Assert.Equal(plan, await new GetPlanHandler(_store).HandleAsync(new GetPlanQuery(Fares, plan.Id), CancellationToken.None));
    }

    [Fact]
    public async Task The_list_shows_the_travellers_own_plans_and_the_most_they_can_keep()
    {
        await BuildAsync();
        await BuildAsync(Command(owner: Maya));

        var list = await new ListPlansHandler(_store).HandleAsync(new ListPlansQuery(Fares), CancellationToken.None);

        Assert.Equal((1, Plan.MaxPlans), (list.Count, list.Max));
        Assert.Equal(("Rome", 900m, "USD"), (list.Items[0].DestinationName, list.Items[0].Total, list.Items[0].Currency));
        Assert.NotNull(list.Items[0].MatchPercent);
    }

    [Fact]
    public async Task A_plan_is_renamed_and_planned()
    {
        var plan = await BuildAsync();
        var handler = new ChangePlanHandler(_store, _clock);

        var changed = await handler.HandleAsync(new ChangePlanCommand(Fares, plan.Id, "Rome with Maya", "planned"), CancellationToken.None);

        Assert.Equal(("Rome with Maya", PlanStatus.Planned), (changed.Name, changed.Status));
        Assert.Equal("name", (await Assert.ThrowsAsync<DomainException>(() => handler.HandleAsync(new ChangePlanCommand(Fares, plan.Id, null, null), CancellationToken.None))).Field);
    }

    [Fact]
    public async Task A_deleted_plan_is_gone()
    {
        var plan = await BuildAsync();

        await new DeletePlanHandler(_store).HandleAsync(new DeletePlanCommand(Fares, plan.Id), CancellationToken.None);

        await Assert.ThrowsAsync<NotFoundException>(() => new GetPlanHandler(_store).HandleAsync(new GetPlanQuery(Fares, plan.Id), CancellationToken.None));
    }

    [Fact]
    public async Task Swapping_the_hotel_calls_no_paid_source_and_rebuilds_the_days()
    {
        var plan = await BuildAsync();
        var placeCalls = _places.Sum(p => p.Calls);

        var swapped = await new SwapOfferHandler(_store, Inputs, _clock).HandleAsync(new SwapOfferCommand(Fares, plan.Id, OfferKind.Hotel, "bk:2"), CancellationToken.None);

        Assert.Equal("bk:2", swapped.HotelId);
        Assert.Equal(1, _hotels.Calls);
        Assert.Equal(1, _flights.Calls);
        Assert.True(_places.Sum(p => p.Calls) > placeCalls);
        Assert.Equal(swapped, await _store.GetAsync(Fares, plan.Id, CancellationToken.None));
    }

    [Fact]
    public async Task An_offer_not_in_the_kept_list_names_offerId_and_fetches_nothing()
    {
        var plan = await BuildAsync();
        var placeCalls = _places.Sum(p => p.Calls);

        var ex = await Assert.ThrowsAsync<DomainException>(() => new SwapOfferHandler(_store, Inputs, _clock)
            .HandleAsync(new SwapOfferCommand(Fares, plan.Id, OfferKind.Hotel, "bk:99"), CancellationToken.None));

        Assert.Equal("offerId", ex.Field);
        Assert.Equal(placeCalls, _places.Sum(p => p.Calls));
    }

    [Fact]
    public async Task When_the_places_fail_a_swap_keeps_the_days()
    {
        var plan = await BuildAsync();
        _places.ForEach(p => p.Fail = true);

        var swapped = await new SwapOfferHandler(_store, Inputs, _clock).HandleAsync(new SwapOfferCommand(Fares, plan.Id, OfferKind.Flight, "tp:2"), CancellationToken.None);

        Assert.Same(plan.Built.Days, swapped.Built.Days);
        Assert.Contains(swapped.Built.Warnings, w => w.StartsWith("The places couldn't be loaded", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_first_car_list_runs_the_car_search_once_then_a_car_is_added_and_removed()
    {
        var plan = await BuildAsync();
        var alternatives = new GetAlternativesHandler(_store, Inputs, _clock);
        var swaps = new SwapOfferHandler(_store, Inputs, _clock);

        var first = await alternatives.HandleAsync(new GetAlternativesQuery(Fares, plan.Id, OfferKind.Car), CancellationToken.None);
        await alternatives.HandleAsync(new GetAlternativesQuery(Fares, plan.Id, OfferKind.Car), CancellationToken.None);
        Assert.Equal(1, _cars.Calls);
        Assert.Equal(("Booking.com", "https://cars.example.test"), (first.Alternatives.CarSource, first.Alternatives.CarBookingUrl));

        var withCar = await swaps.HandleAsync(new SwapOfferCommand(Fares, plan.Id, OfferKind.Car, "car:2"), CancellationToken.None);
        Assert.Equal(90, withCar.Built.Total.Car);

        var without = await swaps.HandleAsync(new SwapOfferCommand(Fares, plan.Id, OfferKind.Car, null, RemoveCar: true), CancellationToken.None);
        Assert.Null(without.CarId);
        Assert.Equal(1, _cars.Calls);
    }

    [Fact]
    public async Task Hotel_and_flight_lists_never_call_a_source()
    {
        var plan = await BuildAsync();

        await new GetAlternativesHandler(_store, Inputs, _clock).HandleAsync(new GetAlternativesQuery(Fares, plan.Id, OfferKind.Hotel), CancellationToken.None);

        Assert.Equal((1, 1, 0), (_hotels.Calls, _flights.Calls, _cars.Calls));
    }

    [Fact]
    public async Task A_failing_car_search_is_unavailable_and_nothing_is_kept()
    {
        var plan = await BuildAsync();
        _cars.Fail = true;

        await Assert.ThrowsAsync<ServiceUnavailableException>(() => new GetAlternativesHandler(_store, Inputs, _clock)
            .HandleAsync(new GetAlternativesQuery(Fares, plan.Id, OfferKind.Car), CancellationToken.None));

        Assert.Null((await _store.GetAsync(Fares, plan.Id, CancellationToken.None))?.Alternatives.Cars);
    }
}
