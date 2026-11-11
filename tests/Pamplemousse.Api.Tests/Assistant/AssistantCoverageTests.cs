using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Domain.Assistant;
using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Destinations;
using Pamplemousse.Domain.Places;
using Pamplemousse.Domain.TravelEvents;
using Pamplemousse.Domain.Travellers;

namespace Pamplemousse.Api.Tests.Assistant;

/// <summary>
/// Spec 010 SC-001 (every card kind from one message each), SC-002 (a change by chat equals the same change through the
/// routes), FR-022a (a full chat) and SC-005 (no chat text in the logs). Paris gets nature places, restaurants and two
/// events on day 2 from fakes, ahead of the real (unconfigured) sources.
/// </summary>
public sealed class AssistantCoverageTests : IClassFixture<TestApiFactory>, IDisposable
{
    private const string Paris = "2988507";
    private const string Rome = "3169070";
    private static readonly Uri Chats = new("/api/v1/assistant/chats", UriKind.Relative);
    private static readonly GeoPoint Hotel = new(48.857, 2.30);
    private static readonly DateOnly Day2 = new(2027, 4, 11);

    private readonly TestApiFactory _base;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly LogCapture _logs = new();
    private readonly string _uid = "uid-" + Guid.NewGuid().ToString("N")[..10];

    public AssistantCoverageTests(TestApiFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _base = factory;
        factory.Hotels.Location = Hotel;
        factory.Chat.Reset();
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddLogging(l => l.AddProvider(_logs).SetMinimumLevel(LogLevel.Trace));
                // First of their category, so they win over the unconfigured OpenTripMap providers.
                services.Insert(0, ServiceDescriptor.Singleton<IPlaceProvider>(new FakePlaces(PlaceCategory.Nature, "park", 30)));
                services.Insert(0, ServiceDescriptor.Singleton<IPlaceProvider>(new FakePlaces(PlaceCategory.Restaurant, "food", 40)));
                services.AddSingleton<IEventProvider>(new FakeEvents());
            });
        });
    }

    public void Dispose()
    {
        _base.Chat.Reset();
        _factory.Dispose();
        _logs.Dispose();
    }

    private HttpClient As()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = TestTokens.Bearer(TestTokens.For(_uid));
        return client;
    }

    private static Uri At(string path) => new(path, UriKind.Relative);

    private static Dictionary<string, object?> Args(params (string Key, object? Value)[] args) =>
        args.ToDictionary(a => a.Key, a => a.Value, StringComparer.Ordinal);

    private static async Task<List<(string Name, JsonElement Data)>> SendAsync(HttpClient client, Uri path, string text)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(new { text, trip = new { destinationId = Paris, from = "2027-04-10", to = "2027-04-14", adults = 2, originId = Rome, currency = "USD" } }),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return [.. (await response.Content.ReadAsStringAsync()).Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Where(b => !b.StartsWith(':'))
            .Select(b =>
            {
                var lines = b.Split('\n');
                using var doc = JsonDocument.Parse(lines.First(l => l.StartsWith("data: ", StringComparison.Ordinal))["data: ".Length..]);
                return (lines[0]["event: ".Length..], doc.RootElement.Clone());
            })];
    }

    private static JsonElement Final(List<(string Name, JsonElement Data)> events) =>
        events.Single(e => string.Equals(e.Name, "final", StringComparison.Ordinal)).Data;

    private static IEnumerable<string> KindsOf(JsonElement cards) => cards.EnumerateArray().Select(c => c.GetProperty("kind").GetString() ?? "");

    private static async Task<string> BuildAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(At("/api/v1/plans"), new
        {
            destinationId = Paris,
            from = "2027-04-10",
            to = "2027-04-14",
            adults = 2,
            currency = "USD",
            originId = Rome,
            preferences = new { budget = 50, culture = 50, food = 50, nature = 70, nightlife = 20, comfort = 50 },
            pace = "balanced",
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
    }

    /// <summary>What a change must leave the same: the days, offers, total, match, values and edits; not ids or times.</summary>
    private static async Task<string> ShapeAsync(HttpClient client, string planId)
    {
        var p = await client.GetFromJsonAsync<JsonElement>($"/api/v1/plans/{planId}");
        string Id(string offer) => p.GetProperty(offer).ValueKind == JsonValueKind.Null ? "-" : p.GetProperty(offer).GetProperty("offer").GetProperty("id").GetString()!;
        var days = p.GetProperty("days").EnumerateArray().Select(d => string.Join(",", d.GetProperty("items").EnumerateArray().Select(i =>
            $"{i.GetProperty("kind").GetString()}:{i.GetProperty("start").GetString()}:{(i.GetProperty("ref").ValueKind == JsonValueKind.Null ? "" : i.GetProperty("ref").GetProperty("id").GetString())}")));
        return string.Join(" | ", [
            p.GetProperty("name").GetString(), p.GetProperty("status").GetString(), p.GetProperty("from").GetString(), p.GetProperty("to").GetString(),
            p.GetProperty("adults").GetInt32().ToString(CultureInfo.InvariantCulture), p.GetProperty("pace").GetString(),
            Id("hotel"), Id("flight"), Id("car"), p.GetProperty("total").GetProperty("amount").GetRawText(),
            p.GetProperty("match").ValueKind == JsonValueKind.Null ? "-" : p.GetProperty("match").GetProperty("percent").GetRawText(),
            p.GetProperty("edits").GetRawText(), .. days]);
    }

    [Fact]
    public async Task Every_card_kind_and_saved_items_are_reached_through_the_assistant()
    {
        using var client = As();
        var chat = _base.Chat;
        chat.Script.Add(("get_overview", Args(("cityId", Paris))));
        chat.Script.Add(("list_places", Args(("category", "nature"), ("cityId", Paris))));
        chat.Script.Add(("list_events", Args(("cityId", Paris))));
        chat.Script.Add(("search_flights", Args(("cityId", Paris), ("originCityId", Rome))));
        chat.Script.Add(("search_hotels", Args(("cityId", Paris))));
        chat.Script.Add(("search_cars", Args(("cityId", Paris))));
        chat.Script.Add(("build_plan", Args(("cityId", Paris))));

        var first = await SendAsync(client, Chats, "Everything for Paris");

        var final = Final(first);
        Assert.Equal<string>(["plan", "flight", "hotel", "car", "place", "event", "facts"], [.. KindsOf(final.GetProperty("cards")).Distinct(StringComparer.Ordinal)]);
        Assert.All(first.Where(e => string.Equals(e.Name, "step", StringComparison.Ordinal)), s => Assert.True(s.Data.GetProperty("ok").GetBoolean(), s.Data.GetProperty("summary").GetString()));
        var facts = final.GetProperty("cards").EnumerateArray().Single(c => string.Equals(c.GetProperty("kind").GetString(), "facts", StringComparison.Ordinal)).GetProperty("item");
        Assert.Equal(Paris, facts.GetProperty("destination").GetProperty("id").GetString());
        Assert.Equal("forecast", facts.GetProperty("weather").GetProperty("kind").GetString());

        var carList = await client.GetFromJsonAsync<JsonElement>($"/api/v1/destinations/{Paris}/cars?from=2027-04-10&to=2027-04-14&currency=USD&sort=match&budget=50&culture=50&food=50&nature=50&nightlife=50&comfort=50");
        var carCard = final.GetProperty("cards").EnumerateArray().First(c => string.Equals(c.GetProperty("kind").GetString(), "car", StringComparison.Ordinal));
        Assert.Equal(carList.GetProperty("items")[0].GetProperty("id").GetString(), carCard.GetProperty("id").GetString());

        var chatId = first[0].Data.GetProperty("chatId").GetString();
        chat.Script.Clear();
        chat.Script.Add(("save_item", Args(("kind", "place"), ("id", "park:1"))));
        chat.Script.Add(("list_saved", Args()));
        var second = await SendAsync(client, At($"/api/v1/assistant/chats/{chatId}/messages"), "Save the first park and show my saved items");
        var saved = second.Last(e => string.Equals(e.Name, "step", StringComparison.Ordinal)).Data;
        Assert.Equal("list_saved", saved.GetProperty("tool").GetString());
        Assert.Equal("park:1", saved.GetProperty("cards")[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task Ten_changes_by_chat_equal_the_same_changes_through_the_routes()
    {
        using var client = As();
        var byChat = await BuildAsync(client);
        var byRoute = await BuildAsync(client);
        Assert.Equal(await ShapeAsync(client, byRoute), await ShapeAsync(client, byChat));
        var chat = await SendAsync(client, Chats, "Let's change my plan");
        var chatId = chat[0].Data.GetProperty("chatId").GetString();
        var next = At($"/api/v1/assistant/chats/{chatId}/messages");

        var plan = await client.GetFromJsonAsync<JsonElement>($"/api/v1/plans/{byChat}");
        var day2 = plan.GetProperty("days")[1].GetProperty("items").EnumerateArray().ToList();
        var nature = day2.First(i => string.Equals(i.GetProperty("kind").GetString(), "nature", StringComparison.Ordinal)).GetProperty("ref").GetProperty("id").GetString()!;
        var ev = day2.Single(i => string.Equals(i.GetProperty("kind").GetString(), "event", StringComparison.Ordinal)).GetProperty("ref").GetProperty("id").GetString()!;
        var other = await client.GetFromJsonAsync<JsonElement>($"/api/v1/plans/{byChat}/days/2/alternatives?itemId={Uri.EscapeDataString(nature)}");
        var otherNature = other.GetProperty("items")[0].GetProperty("id").GetString()!;
        var cars = await client.GetFromJsonAsync<JsonElement>($"/api/v1/plans/{byRoute}/alternatives/cars");
        var car = cars.GetProperty("items")[0].GetProperty("id").GetString()!;
        Uri Plan(string id, string rest = "") => At($"/api/v1/plans/{id}{rest}");

        await SameAsync(client, next, byChat, byRoute, "Pick the cheaper hotel", [("swap_plan_offer", Args(("planId", byChat), ("kind", "hotel"), ("offerId", "bk:2")))], () => client.PutAsJsonAsync(Plan(byRoute, "/hotel"), new { offerId = "bk:2" }));
        await SameAsync(client, next, byChat, byRoute, "Take the other flight", [("swap_plan_offer", Args(("planId", byChat), ("kind", "flight"), ("offerId", "tp:dear")))], () => client.PutAsJsonAsync(Plan(byRoute, "/flight"), new { offerId = "tp:dear" }));
        await SameAsync(
            client, next, byChat, byRoute,
            "Add a car",
            [("list_plan_alternatives", Args(("planId", byChat), ("kind", "car"))), ("swap_plan_offer", Args(("planId", byChat), ("kind", "car"), ("offerId", car)))],
            () => client.PutAsJsonAsync(Plan(byRoute, "/car"), new { offerId = car }));
        await SameAsync(client, next, byChat, byRoute, "Drop the car", [("remove_plan_car", Args(("planId", byChat)))], () => client.DeleteAsync(Plan(byRoute, "/car")));
        await SameAsync(
            client, next, byChat, byRoute,
            "Swap that park on day 2 for another",
            [("change_day_item", Args(("planId", byChat), ("day", 2), ("action", "replace"), ("itemId", nature), ("newItemId", otherNature), ("newItemKind", "place")))],
            () => client.PostAsJsonAsync(Plan(byRoute, "/days/2/items"), new { kind = "place", id = otherNature, replaces = nature }));
        await SameAsync(
            client, next, byChat, byRoute,
            "Skip the event on day 2",
            [("change_day_item", Args(("planId", byChat), ("day", 2), ("action", "remove"), ("itemId", ev)))],
            () => client.DeleteAsync(Plan(byRoute, $"/days/2/items/{Uri.EscapeDataString(ev)}")));
        await SameAsync(
            client, next, byChat, byRoute,
            "Take the other show on day 2 instead",
            [("change_day_item", Args(("planId", byChat), ("day", 2), ("action", "add"), ("newItemId", "ev:late"), ("newItemKind", "event")))],
            () => client.PostAsJsonAsync(Plan(byRoute, "/days/2/items"), new { kind = "event", id = "ev:late" }));
        await SameAsync(client, next, byChat, byRoute, "Call it Paris with Maya", [("update_plan", Args(("planId", byChat), ("name", "Paris with Maya")))], () => client.PatchAsJsonAsync(Plan(byRoute), new { name = "Paris with Maya" }));
        await SameAsync(client, next, byChat, byRoute, "Mark it as planned", [("update_plan", Args(("planId", byChat), ("status", "planned")))], () => client.PatchAsJsonAsync(Plan(byRoute), new { status = "planned" }));
        await SameAsync(
            client, next, byChat, byRoute,
            "Make it 4 days for 3, relaxed",
            [("rebuild_plan", Args(("planId", byChat), ("to", "2027-04-13"), ("adults", 3), ("pace", "relaxed")))],
            () => client.PostAsJsonAsync(Plan(byRoute, "/rebuild"), new { to = "2027-04-13", adults = 3, pace = "relaxed" }));

        Assert.Contains("Paris with Maya | planned | 2027-04-10 | 2027-04-13 | 3 | relaxed", await ShapeAsync(client, byChat), StringComparison.Ordinal);
    }

    /// <summary>One change: by chat on one plan, by its route on the twin, then both plans must read the same.</summary>
    private async Task SameAsync(
        HttpClient client, Uri next, string byChat, string byRoute, string what, (string Tool, Dictionary<string, object?> Args)[] tools, Func<Task<HttpResponseMessage>> route)
    {
        _base.Chat.Script.Clear();
        foreach (var tool in tools)
        {
            _base.Chat.Script.Add(tool);
        }

        var events = await SendAsync(client, next, what);
        var seen = string.Join(" / ", _base.Chat.LastMessages.SelectMany(m => m.Contents.OfType<Microsoft.Extensions.AI.FunctionResultContent>()).Select(r => r.Result?.ToString()));
        Assert.True(Final(events).TryGetProperty("planChanged", out var changed), $"{what}: the tools answered {seen}");
        Assert.Equal(byChat, changed.GetString());
        Assert.True((await route()).IsSuccessStatusCode, what);
        Assert.Equal(await ShapeAsync(client, byRoute), await ShapeAsync(client, byChat));
    }

    [Fact]
    public async Task A_chat_of_200_messages_is_full()
    {
        using var client = As();
        var events = await SendAsync(client, Chats, "Hi");
        var chatId = events[0].Data.GetProperty("chatId").GetString()!;
        var store = _factory.Services.GetRequiredService<IChatStore>();
        var chat = (await store.GetAsync(new TravellerId(_uid), chatId, CancellationToken.None))!;
        await store.SaveAsync(chat with { MessageCount = ChatRules.MaxMessages }, null, CancellationToken.None);

        using var request = new HttpRequestMessage(HttpMethod.Post, At($"/api/v1/assistant/chats/{chatId}/messages")) { Content = JsonContent.Create(new { text = "One more" }) };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(("chat-full", ChatRules.FullMessage), (problem.GetProperty("code").GetString(), problem.GetProperty("detail").GetString()));
    }

    [Fact]
    public async Task No_chat_text_tool_arguments_or_answers_reach_the_logs()
    {
        using var client = As();
        _base.Chat.Script.Add(("search_hotels", Args(("cityId", Paris), ("maxPrice", 777777))));
        _base.Chat.Answer = "Zanzibarquokka answer text";

        await SendAsync(client, Chats, "Xylophonewombat question text");

        Assert.NotEmpty(_logs.Lines);
        Assert.Contains(_logs.Lines, l => l.Contains("answered with", StringComparison.Ordinal));
        Assert.DoesNotContain(_logs.Lines, l => l.Contains("Xylophonewombat", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(_logs.Lines, l => l.Contains("Zanzibarquokka", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(_logs.Lines, l => l.Contains("777777", StringComparison.Ordinal));
    }

    private sealed class FakePlaces(PlaceCategory category, string prefix, int count) : IPlaceProvider
    {
        public string Source => prefix;

        public PlaceCategory Category => category;

        public Task<IReadOnlyList<Place>> SearchAsync(Destination destination, int limit, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Place>>([.. Enumerable.Range(1, count).Take(limit).Select(i => new Place(
                $"{prefix}:{i.ToString(CultureInfo.InvariantCulture)}",
                $"{prefix} {i.ToString(CultureInfo.InvariantCulture)}",
                category,
                new GeoPoint(Hotel.Lat + (((i * 7) % 11) - 5) * 0.002, Hotel.Lng + (((i * 5) % 13) - 6) * 0.002),
                null, 4.5, 100, null, null, null, null, null, [], prefix))]);

        public Task<Place?> GetAsync(string providerId, CancellationToken ct) => Task.FromResult<Place?>(null);
    }

    private sealed class FakeEvents : IEventProvider
    {
        public Task<IReadOnlyList<TravelEvent>> SearchAsync(Destination destination, DateOnly firstDay, DateOnly lastDay, int limit, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TravelEvent>>(
            [
                new("ev:early", "Jazz at the Sunset", Day2, "20:00", "Sunset", "Music", new Money(30, "USD"), null, "https://tickets.test/1", "test"),
                new("ev:late", "Late show", Day2, "21:00", "Olympia", "Music", new Money(45, "USD"), null, "https://tickets.test/2", "test"),
            ]);
    }

    /// <summary>Every log line the API writes, formatted, with its exception.</summary>
    private sealed class LogCapture : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _lines = new();

        public IReadOnlyList<string> Lines => [.. _lines];

        public ILogger CreateLogger(string categoryName) => new Logger(_lines);

        public void Dispose()
        {
        }

        private sealed class Logger(ConcurrentQueue<string> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                lines.Enqueue(formatter(state, exception) + " " + exception);
        }
    }
}
