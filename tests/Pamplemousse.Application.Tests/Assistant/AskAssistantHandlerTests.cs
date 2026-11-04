using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Application.Assistant;
using Pamplemousse.Application.Assistant.Queries.AskAssistant;
using Pamplemousse.Application.Destinations;
using Pamplemousse.Application.Destinations.Queries.GetOverview;
using Pamplemousse.Application.Destinations.Queries.ListEvents;
using Pamplemousse.Application.Destinations.Queries.ListPlaces;
using Pamplemousse.Application.Destinations.Queries.SearchDestinations;
using Pamplemousse.Application.Flights.Queries.SearchFlights;
using Pamplemousse.Application.Hotels.Queries.SearchHotels;
using Pamplemousse.Application.Tests.Destinations;
using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Destinations;
using Pamplemousse.Domain.Exceptions;
using Pamplemousse.Domain.Flights;
using Pamplemousse.Domain.Hotels;
using Pamplemousse.Domain.Places;
using Pamplemousse.Domain.TravelEvents;

namespace Pamplemousse.Application.Tests.Assistant;

public sealed class AskAssistantHandlerTests : IDisposable
{
    private const string Rome = "3169070";
    private const string Paris = "2988507";
    private static readonly DateOnly Today = new(2026, 10, 27);
    private static readonly AssistantTripInput RomeTrip = new(Rome, new DateOnly(2027, 4, 10), new DateOnly(2027, 4, 15), 2, "LB", "USD", null);
    private static readonly DestinationSummary RomeSummary = new(Rome, "Rome", "Lazio", "IT", "Italy", null, 41.89, 12.51, 2_318_895, "Europe/Rome");

    private readonly ScriptedChatClient _model = new();
    private readonly Fakes _data = new();
    private readonly FakeHandler<SearchDestinationsQuery, IReadOnlyList<DestinationSummary>> _cities = new(_ => [RomeSummary]);
    private readonly FakeHandler<ListPlacesQuery, PlacesResponse> _places = new(_ => new PlacesResponse(PlaceCategory.Restaurant, [], ["Restaurants is unavailable right now."]));
    private readonly FakeHandler<ListEventsQuery, EventsResponse> _events = new(q => new EventsResponse(Dates(q.From, q.To), [Concert], []));
    private readonly FakeHandler<GetOverviewQuery, OverviewResponse> _overview;
    private readonly FakeHandler<SearchFlightsQuery, SearchFlightsResponse> _flights;
    private readonly FakeHandler<SearchHotelsQuery, SearchHotelsResponse> _hotels;

    public AskAssistantHandlerTests()
    {
        _overview = new(q => new OverviewResponse(
            RomeSummary, Fakes.Italy, Dates(q.From, q.To), null, null, null,
            new VisaRequirement(q.Passport!, "IT", "visa required", null), null, ["Weather is unavailable right now."]));
        _flights = new(q => new SearchFlightsResponse(
            new FlightEndpoint(q.OriginId, "Paris", "PAR", "PAR", false, null), new FlightEndpoint(q.DestinationId, "Rome", "ROM", "ROM", false, null),
            Dates(q.From, q.To), TripType.RoundTrip, false, q.Currency!, [Fare(82m), Fare(95m)], "Recent prices", null, null, []));
        _hotels = new(q => new SearchHotelsResponse(
            new HotelDestination(q.DestinationId, "Rome", null), Dates(q.From, q.To), 5, q.Adults ?? 2, q.Currency!, HotelSort.Recommended, 1, 1, 1,
            DateTimeOffset.UnixEpoch, false, [Stay(q)], "Booking.com", null, null, null, []));
    }

    private static readonly TravelEvent Concert = new("tm:1", "Opera night", new DateOnly(2027, 4, 11), "20:00", "Teatro", "Music", new Money(40m, "EUR"), null, "https://x", "test");

    private static DateRange Dates(DateOnly? from, DateOnly? to) => DateRange.ForTrip(from, to, Today);

    private static FlightOffer Fare(decimal price) => new(
        "f", "AZ", null, "CDG", "FCO", DateTimeOffset.UnixEpoch, null, 0, null, 120, new Money(price, "USD"), "https://x", DateMatch.Exact, "test");

    private static HotelOffer Stay(SearchHotelsQuery q) => new(
        new Hotel { Id = "bk:1", Name = "Hotel Artemide", Location = new GeoPoint(41.9, 12.49), Stars = 4, ReviewScore = 8.9 },
        new HotelPrice(612m, 122.4m, q.Currency!, q.From!.Value, q.To!.Value, q.Adults ?? 2, DateTimeOffset.UnixEpoch), 1, 1.234, false);

    private AskAssistantHandler Handler(IChatClient? model = null) => new(
        model ?? new FunctionInvokingChatClient(_model),
        new AssistantTools(_data, _cities, _overview, _places, _events, _flights, _hotels),
        _data,
        new FixedClock(),
        NullLogger<AskAssistantHandler>.Instance);

    public void Dispose() => _model.Dispose();

    private static AskAssistantQuery Ask(string text, AssistantTripInput? trip = null) => new([new AssistantMessage("traveller", text)], trip);

    [Fact]
    public async Task Answers_from_a_tool_and_adds_a_card_for_its_screen()
    {
        _model.CallTool("get_overview").Answer("You need a visa.");

        var reply = await Handler().HandleAsync(Ask("Do I need a visa?", RomeTrip), CancellationToken.None);

        Assert.Equal("You need a visa.", reply.Text);
        var query = Assert.Single(_overview.Queries);
        Assert.Equal((Rome, "LB", "USD", new DateOnly(2027, 4, 10), new DateOnly(2027, 4, 15)), (query.DestinationId, query.Passport, query.Currency, query.From, query.To));
        Assert.Contains("visa required", _model.LastToolResult(), StringComparison.Ordinal);
        Assert.Equal(
            [new AssistantCard(CardKind.Overview, Rome, "About Rome", "10 – 15 Apr · visa, weather, safety", new DateOnly(2027, 4, 10), new DateOnly(2027, 4, 15))],
            reply.Cards);
        Assert.Equal(["Weather is unavailable right now."], reply.Warnings);
    }

    [Fact]
    public async Task Sends_the_rules_and_the_trip_then_the_conversation_with_its_roles()
    {
        _model.Answer("Hello!");
        var query = new AskAssistantQuery(
            [new AssistantMessage("traveller", "Hi"), new AssistantMessage("assistant", "Hello, where to?"), new AssistantMessage("traveller", "Rome")], RomeTrip);

        await Handler().HandleAsync(query, CancellationToken.None);

        var sent = Assert.Single(_model.Calls);
        Assert.Equal([ChatRole.System, ChatRole.User, ChatRole.Assistant, ChatRole.User], sent.Select(m => m.Role));
        Assert.Contains("Rome, Italy (city id 3169070)", sent[0].Text, StringComparison.Ordinal);
        Assert.Contains("dates 2027-04-10 to 2027-04-15; 2 adults; passport LB; currency USD", sent[0].Text, StringComparison.Ordinal);
        Assert.Contains("Departure city: unknown.", sent[0].Text, StringComparison.Ordinal);
        Assert.Contains("Today is 2026-10-27", sent[0].Text, StringComparison.Ordinal);
        Assert.Equal(["find_city", "get_overview", "list_places", "list_events", "search_flights", "search_hotels"], _model.Options!.Tools!.Select(t => t.Name), StringComparer.Ordinal);
        Assert.Empty(_overview.Queries);
    }

    [Fact]
    public async Task Values_in_the_question_override_the_trip_for_that_search()
    {
        _model.CallTool("search_hotels", new(StringComparer.Ordinal) { ["adults"] = 3, ["from"] = "2027-04-12", ["sort"] = "price" }).Answer("Hotel Artemide, 612 USD.");

        var reply = await Handler().HandleAsync(Ask("Cheapest hotel for 3 from the 12th?", RomeTrip), CancellationToken.None);

        var query = Assert.Single(_hotels.Queries);
        Assert.Equal((Rome, 3, "price", new DateOnly(2027, 4, 12), new DateOnly(2027, 4, 17)), (query.DestinationId, query.Adults, query.Sort, query.From, query.To));
        var card = Assert.Single(reply.Cards);
        Assert.Equal((CardKind.Hotels, "Hotels in Rome", "12 – 17 Apr · 3 adults", 3), (card.Kind, card.Title, card.Subtitle, card.Adults));
        using var result = JsonDocument.Parse(_model.LastToolResult());
        Assert.Equal(612m, result.RootElement.GetProperty("hotels")[0].GetProperty("totalPrice").GetDecimal());
    }

    [Fact]
    public async Task The_same_search_twice_gives_one_card()
    {
        _model.CallTool("list_events").CallTool("list_events").Answer("Opera night on the 11th.");

        var reply = await Handler().HandleAsync(Ask("What's on?", RomeTrip), CancellationToken.None);

        Assert.Equal(2, _events.Queries.Count);
        var card = Assert.Single(reply.Cards);
        Assert.Equal((CardKind.Overview, "Events in Rome", "10 – 15 Apr"), (card.Kind, card.Title, card.Subtitle));
    }

    [Fact]
    public async Task A_city_found_by_name_is_used_without_a_trip()
    {
        _model.CallTool("find_city", new(StringComparer.Ordinal) { ["name"] = "Rome" })
            .CallTool("list_places", new(StringComparer.Ordinal) { ["cityId"] = Rome, ["category"] = "restaurant" })
            .Answer("Restaurants are unavailable right now.");

        var reply = await Handler().HandleAsync(Ask("Good restaurants in Rome?"), CancellationToken.None);

        Assert.Equal("Rome", Assert.Single(_cities.Queries).Term);
        Assert.Equal((Rome, "restaurant"), (_places.Queries[0].DestinationId, _places.Queries[0].Category));
        var card = Assert.Single(reply.Cards);
        Assert.Equal((CardKind.Places, "Restaurants in Rome", PlaceCategory.Restaurant), (card.Kind, card.Title, card.Category));
        Assert.Null(card.From);
        Assert.Equal(["Restaurants is unavailable right now."], reply.Warnings);
    }

    [Fact]
    public async Task Flights_carry_the_departure_city_and_the_cheapest_price_on_their_card()
    {
        _model.CallTool("search_flights", new(StringComparer.Ordinal) { ["originCityId"] = Paris }).Answer("From 82 USD.");

        var reply = await Handler().HandleAsync(Ask("Flights from Paris?", RomeTrip), CancellationToken.None);

        var query = Assert.Single(_flights.Queries);
        Assert.Equal((Paris, Rome, "roundTrip", "USD"), (query.OriginId, query.DestinationId, query.TripType, query.Currency));
        var card = Assert.Single(reply.Cards);
        Assert.Equal((CardKind.Flights, "Flights from Paris to Rome", "10 – 15 Apr · from 82 USD", Paris), (card.Kind, card.Title, card.Subtitle, card.OriginId));
    }

    [Fact]
    public async Task Flights_without_a_departure_city_ask_the_model_to_ask()
    {
        _model.CallTool("search_flights").Answer("Where do you fly from?");

        var reply = await Handler().HandleAsync(Ask("Flights?", RomeTrip), CancellationToken.None);

        Assert.Empty(_flights.Queries);
        Assert.Contains("No departure city", _model.LastToolResult(), StringComparison.Ordinal);
        Assert.Equal(("Where do you fly from?", 0), (reply.Text, reply.Cards.Count));
    }

    [Fact]
    public async Task A_tool_without_any_city_tells_the_model_to_find_one()
    {
        _model.CallTool("get_overview").Answer("Which city?");

        var reply = await Handler().HandleAsync(Ask("Is it safe?"), CancellationToken.None);

        Assert.Empty(_overview.Queries);
        Assert.Contains("Call find_city first", _model.LastToolResult(), StringComparison.Ordinal);
        Assert.Empty(reply.Cards);
    }

    [Fact]
    public async Task A_broken_rule_inside_a_tool_goes_back_to_the_model_and_the_answer_still_arrives()
    {
        var unknownCity = new FakeHandler<GetOverviewQuery, OverviewResponse>(_ => throw new NotFoundException("Destination '1' was not found."));
        var handler = new AskAssistantHandler(
            new FunctionInvokingChatClient(_model), new AssistantTools(_data, _cities, unknownCity, _places, _events, _flights, _hotels), _data, new FixedClock(), NullLogger<AskAssistantHandler>.Instance);
        _model.CallTool("get_overview", new(StringComparer.Ordinal) { ["cityId"] = "1" }).Answer("I couldn't find that city.");

        var reply = await handler.HandleAsync(Ask("Visa for city 1?"), CancellationToken.None);

        Assert.Contains("Destination '1' was not found.", _model.LastToolResult(), StringComparison.Ordinal);
        Assert.Equal("I couldn't find that city.", reply.Text);
        Assert.Empty(reply.Cards);
    }

    [Fact]
    public async Task Without_a_key_the_answer_is_a_warning_naming_the_variable()
    {
        _model.Fail(new ProviderNotConfiguredException("Assistant", "GEMINI_API_KEY"));

        var reply = await Handler().HandleAsync(Ask("Hi"), CancellationToken.None);

        Assert.Equal(("", 0), (reply.Text, reply.Cards.Count));
        Assert.Contains("GEMINI_API_KEY", Assert.Single(reply.Warnings), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_busy_model_gives_the_busy_warning_and_drops_cards_from_the_failed_answer()
    {
        _model.CallTool("get_overview").Fail(new AssistantUnavailableException("503"));

        var reply = await Handler().HandleAsync(Ask("Visa?", RomeTrip), CancellationToken.None);

        Assert.Equal(("", 0), (reply.Text, reply.Cards.Count));
        Assert.Equal([AskAssistantHandler.BusyWarning], reply.Warnings);
    }

    [Fact]
    public async Task An_empty_answer_says_there_was_no_answer()
    {
        _model.Answer("  ");

        var reply = await Handler().HandleAsync(Ask("Hi"), CancellationToken.None);

        Assert.Equal([AskAssistantHandler.NoAnswerWarning], reply.Warnings);
    }

    [Fact]
    public async Task An_unknown_trip_city_is_not_found_and_a_broken_conversation_is_rejected()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            Handler().HandleAsync(Ask("Hi", RomeTrip with { DestinationId = "1" }), CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            Handler().HandleAsync(Ask("Hi", RomeTrip with { OriginId = "1" }), CancellationToken.None));
        await Assert.ThrowsAsync<DomainException>(() =>
            Handler().HandleAsync(new AskAssistantQuery([new AssistantMessage("assistant", "Hi")], null), CancellationToken.None));
        Assert.Empty(_model.Calls);
    }
}
