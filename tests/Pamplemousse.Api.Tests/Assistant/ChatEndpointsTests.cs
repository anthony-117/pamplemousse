using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Application.Assistant.Queries.AskAssistant;

namespace Pamplemousse.Api.Tests.Assistant;

/// <summary>
/// The chats and the answer stream of spec 010 (contracts/chat-api.md), through HTTP with a scripted model: the real
/// tool loop, use cases, stores and cards run in between. The hotels are by the Eiffel Tower, so a Paris plan has a sight.
/// </summary>
public sealed class ChatEndpointsTests : IClassFixture<TestApiFactory>, IDisposable
{
    private const string Paris = "2988507";
    private const string Rome = "3169070";
    private static readonly Uri Chats = new("/api/v1/assistant/chats", UriKind.Relative);

    private readonly TestApiFactory _factory;
    private readonly string _uid = "uid-" + Guid.NewGuid().ToString("N")[..10];

    public ChatEndpointsTests(TestApiFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
        factory.Hotels.Location = new Domain.Common.GeoPoint(48.857, 2.30);
        factory.Chat.Reset();
    }

    public void Dispose() => _factory.Chat.Reset();

    private HttpClient As(string? uid = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = TestTokens.Bearer(TestTokens.For(uid ?? _uid));
        return client;
    }

    private static Uri At(string path) => new(path, UriKind.Relative);

    private static Dictionary<string, object?> Args(params (string Key, object? Value)[] args) =>
        args.ToDictionary(a => a.Key, a => a.Value, StringComparer.Ordinal);

    private static object Message(string text, object? trip = null) => new { text, trip = trip ?? new { adults = 2, currency = "USD" } };

    private static HttpRequestMessage Post(Uri path, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        return request;
    }

    /// <summary>The events of a whole stream, in order.</summary>
    private static async Task<List<(string Name, JsonElement Data)>> StreamAsync(HttpClient client, Uri path, object body)
    {
        using var request = Post(path, body);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        return Parse(await response.Content.ReadAsStringAsync());
    }

    private static List<(string Name, JsonElement Data)> Parse(string text) =>
        [.. text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Where(block => !block.StartsWith(':'))
            .Select(block =>
            {
                var lines = block.Split('\n');
                var name = lines.First(l => l.StartsWith("event: ", StringComparison.Ordinal))["event: ".Length..];
                var data = lines.First(l => l.StartsWith("data: ", StringComparison.Ordinal))["data: ".Length..];
                using var doc = JsonDocument.Parse(data);
                return (name, doc.RootElement.Clone());
            })];

    /// <summary>Reads a stream until its <c>chat</c> event; the response stays open.</summary>
    private static async Task<(HttpResponseMessage Response, string ChatId)> OpenAsync(HttpClient client, Uri path, object body)
    {
        using var request = Post(path, body);
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var reader = new StreamReader(await response.Content.ReadAsStreamAsync());
        while (await reader.ReadLineAsync() is { } line)
        {
            if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                using var doc = JsonDocument.Parse(line["data: ".Length..]);
                return (response, doc.RootElement.GetProperty("chatId").GetString()!);
            }
        }

        throw new InvalidOperationException("The stream ended before its chat event.");
    }

    private static async Task<JsonElement> ProblemOf(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task A_first_message_builds_a_plan_and_the_step_comes_before_the_answer()
    {
        using var client = As();
        _factory.Chat.Script.Add(("build_plan", Args(("cityId", Paris), ("from", "2027-04-10"), ("to", "2027-04-14"), ("originCityId", Rome))));
        _factory.Chat.Answer = "Here is your Paris plan.";

        var events = await StreamAsync(client, Chats, Message("Plan 5 days in Paris for two from Rome"));

        Assert.Equal<string>(["chat", "step", "final"], [.. events.Select(e => e.Name)]);
        var (_, started) = events[0];
        Assert.Equal("Plan 5 days in Paris for two from Rome", started.GetProperty("title").GetString());
        Assert.Equal("0001", started.GetProperty("messageId").GetString());
        var step = events[1].Data;
        Assert.Equal(("build_plan", true), (step.GetProperty("tool").GetString(), step.GetProperty("ok").GetBoolean()));
        Assert.StartsWith("Built your plan", step.GetProperty("summary").GetString(), StringComparison.Ordinal);
        var card = step.GetProperty("cards")[0];
        Assert.Equal(("plan", Paris, "Paris"), (card.GetProperty("kind").GetString(), card.GetProperty("destinationId").GetString(), card.GetProperty("destinationName").GetString()));

        var final = events[2].Data;
        Assert.Equal("Here is your Paris plan.", final.GetProperty("text").GetString());
        Assert.Equal("0002", final.GetProperty("messageId").GetString());
        var planId = final.GetProperty("planChanged").GetString();
        Assert.Equal(card.GetProperty("id").GetString(), planId);
        Assert.Contains("Add a car to my plan", final.GetProperty("suggestions").EnumerateArray().Select(s => s.GetString()), StringComparer.Ordinal);

        var plans = await client.GetFromJsonAsync<JsonElement>("/api/v1/plans");
        Assert.Equal(planId, plans.GetProperty("items")[0].GetProperty("id").GetString());

        var chatId = started.GetProperty("chatId").GetString();
        var chat = await client.GetFromJsonAsync<JsonElement>($"/api/v1/assistant/chats/{chatId}");
        var messages = chat.GetProperty("messages");
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal(("traveller", "assistant"), (messages[0].GetProperty("role").GetString(), messages[1].GetProperty("role").GetString()));
        Assert.Equal("build_plan", messages[1].GetProperty("steps")[0].GetProperty("tool").GetString());
        Assert.Equal("plan", messages[1].GetProperty("cards")[0].GetProperty("kind").GetString());
        Assert.False(chat.GetProperty("busy").GetBoolean());
    }

    [Fact]
    public async Task Hotel_cards_are_the_hotels_list_items_ranked_with_their_match()
    {
        using var client = As();
        _factory.Chat.Script.Add(("search_hotels", Args(("cityId", Paris), ("from", "2027-04-10"), ("to", "2027-04-14"))));

        var events = await StreamAsync(client, Chats, Message("Hotels in Paris"));

        var step = events.Single(e => string.Equals(e.Name, "step", StringComparison.Ordinal)).Data;
        Assert.Equal("Searched hotels in Paris · 2 found, ranked for you", step.GetProperty("summary").GetString());
        Assert.Equal(2, step.GetProperty("total").GetInt32());
        var card = step.GetProperty("cards")[0];
        Assert.Equal("hotel", card.GetProperty("kind").GetString());
        var list = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/destinations/{Paris}/hotels?from=2027-04-10&to=2027-04-14&adults=2&currency=USD&sort=match&budget=50&culture=50&food=50&nature=50&nightlife=50&comfort=50");
        var listed = list.GetProperty("items")[0];
        Assert.Equal(listed.GetProperty("id").GetString(), card.GetProperty("id").GetString());
        Assert.Equal(listed.GetProperty("price").GetRawText(), card.GetProperty("item").GetProperty("price").GetRawText());
        Assert.Equal(listed.GetProperty("match").GetProperty("percent").GetInt32(), card.GetProperty("item").GetProperty("match").GetProperty("percent").GetInt32());
        Assert.Equal("hotel", events.Single(e => string.Equals(e.Name, "final", StringComparison.Ordinal)).Data.GetProperty("cards")[0].GetProperty("kind").GetString());
    }

    [Fact]
    public async Task The_next_message_sees_the_history_and_saves_a_hotel_from_a_card()
    {
        using var client = As();
        _factory.Chat.Script.Add(("search_hotels", Args(("cityId", Paris), ("from", "2027-04-10"), ("to", "2027-04-14"))));
        var first = await StreamAsync(client, Chats, Message("Hotels in Paris"));
        var chatId = first[0].Data.GetProperty("chatId").GetString();
        _factory.Chat.Script.Clear();
        _factory.Chat.Script.Add(("save_item", Args(("kind", "hotel"), ("id", "bk:1"))));

        var next = await StreamAsync(client, At($"/api/v1/assistant/chats/{chatId}/messages"), new { text = "Save the first one" });

        Assert.Equal(chatId, next[0].Data.GetProperty("chatId").GetString());
        Assert.Equal("0003", next[0].Data.GetProperty("messageId").GetString());
        Assert.Contains(_factory.Chat.LastMessages, m => string.Equals(m.Text, "Hotels in Paris", StringComparison.Ordinal));
        var saved = await client.GetFromJsonAsync<JsonElement>("/api/v1/saved");
        var item = saved.GetProperty("items")[0];
        Assert.Equal(("hotel", "bk:1", Paris), (item.GetProperty("kind").GetString(), item.GetProperty("id").GetString(), item.GetProperty("destinationId").GetString()));
        Assert.Equal("bk:1", item.GetProperty("item").GetProperty("id").GetString());
    }

    [Fact]
    public async Task Refusals_come_before_the_stream_as_problem_details()
    {
        using var client = As();
        using var other = As("uid-maya-" + Guid.NewGuid().ToString("N")[..6]);

        using (var empty = Post(Chats, new { text = " " }))
        {
            Assert.True((await ProblemOf(await client.SendAsync(empty), HttpStatusCode.BadRequest)).GetProperty("errors").TryGetProperty("text", out _));
        }

        using (var adults = Post(Chats, Message("Hi", new { adults = 9 })))
        {
            Assert.True((await ProblemOf(await client.SendAsync(adults), HttpStatusCode.BadRequest)).GetProperty("errors").TryGetProperty("trip.adults", out _));
        }

        using (var unknown = Post(At("/api/v1/assistant/chats/nope/messages"), new { text = "Hi" }))
        {
            Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(unknown)).StatusCode);
        }

        var events = await StreamAsync(client, Chats, Message("Hi"));
        var chatId = events[0].Data.GetProperty("chatId").GetString();
        using var theirs = Post(At($"/api/v1/assistant/chats/{chatId}/messages"), new { text = "Hi" });
        Assert.Equal(HttpStatusCode.NotFound, (await other.SendAsync(theirs)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(At($"/api/v1/assistant/chats/{chatId}"))).StatusCode);
    }

    [Fact]
    public async Task A_second_send_while_one_answers_is_chat_busy_and_a_closed_stream_still_saves()
    {
        using var client = As();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _factory.Chat.Gate = gate;
        _factory.Chat.Answer = "Saved even though nobody was listening.";

        var (response, chatId) = await OpenAsync(client, Chats, Message("Plan Paris"));
        response.Dispose();

        using (var second = Post(At($"/api/v1/assistant/chats/{chatId}/messages"), new { text = "And Rome?" }))
        {
            Assert.Equal("chat-busy", (await ProblemOf(await client.SendAsync(second), HttpStatusCode.Conflict)).GetProperty("code").GetString());
        }

        Assert.True((await client.GetFromJsonAsync<JsonElement>($"/api/v1/assistant/chats/{chatId}")).GetProperty("busy").GetBoolean());
        gate.SetResult();

        JsonElement chat = default;
        for (var i = 0; i < 50; i++)
        {
            chat = await client.GetFromJsonAsync<JsonElement>($"/api/v1/assistant/chats/{chatId}");
            if (chat.GetProperty("messages").GetArrayLength() == 2)
            {
                break;
            }

            await Task.Delay(100);
        }

        Assert.Equal("Saved even though nobody was listening.", chat.GetProperty("messages")[1].GetProperty("text").GetString());
        Assert.False(chat.GetProperty("busy").GetBoolean());
    }

    [Fact]
    public async Task A_busy_or_unset_model_ends_with_a_warning_never_an_error()
    {
        using var client = As();
        _factory.Chat.Error = new AssistantUnavailableException("503 high demand");

        var busy = await StreamAsync(client, Chats, Message("Hi"));
        var final = busy.Single(e => string.Equals(e.Name, "final", StringComparison.Ordinal)).Data;
        Assert.Equal("", final.GetProperty("text").GetString());
        Assert.Equal(AskAssistantHandler.BusyWarning, final.GetProperty("warnings")[0].GetString());
        Assert.Equal(0, final.GetProperty("suggestions").GetArrayLength());

        _factory.Chat.Error = new ProviderNotConfiguredException("Assistant", "GEMINI_API_KEY");
        var unset = await StreamAsync(client, Chats, Message("Hi"));
        Assert.Contains("GEMINI_API_KEY", unset.Single(e => string.Equals(e.Name, "final", StringComparison.Ordinal)).Data.GetProperty("warnings")[0].GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Recents_page_rename_search_and_delete()
    {
        using var client = As();
        await StreamAsync(client, Chats, Message("Jazz bars in Lisbon tonight"));
        await StreamAsync(client, Chats, Message("Museums in Rome"));
        var third = await StreamAsync(client, Chats, Message("Café crème in Paris"));
        var chatId = third[0].Data.GetProperty("chatId").GetString();

        var page = await client.GetFromJsonAsync<JsonElement>("/api/v1/assistant/chats?limit=2");
        Assert.Equal(2, page.GetProperty("items").GetArrayLength());
        Assert.Equal(90, page.GetProperty("keepDays").GetInt32());
        Assert.Equal(chatId, page.GetProperty("items")[0].GetProperty("id").GetString());
        var next = page.GetProperty("next").GetString();
        var rest = await client.GetFromJsonAsync<JsonElement>($"/api/v1/assistant/chats?limit=2&before={Uri.EscapeDataString(next!)}");
        Assert.Equal(1, rest.GetProperty("items").GetArrayLength());
        Assert.True(rest.GetProperty("next").ValueKind == JsonValueKind.Null);

        var renamed = await client.PatchAsJsonAsync(At($"/api/v1/assistant/chats/{chatId}"), new { title = "Paris with Maya" });
        Assert.Equal("Paris with Maya", (await renamed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
        Assert.True((await ProblemOf(await client.PatchAsJsonAsync(At($"/api/v1/assistant/chats/{chatId}"), new { title = "" }), HttpStatusCode.BadRequest)).GetProperty("errors").TryGetProperty("title", out _));

        var jazz = await client.GetFromJsonAsync<JsonElement>("/api/v1/assistant/chats/search?q=JAZZ");
        Assert.Equal("Jazz bars in Lisbon tonight", Assert.Single(jazz.GetProperty("chats").EnumerateArray()).GetProperty("title").GetString());
        Assert.Equal(0, (await client.GetFromJsonAsync<JsonElement>("/api/v1/assistant/chats/search?q=jaz")).GetProperty("chats").GetArrayLength());
        Assert.Equal(1, (await client.GetFromJsonAsync<JsonElement>("/api/v1/assistant/chats/search?q=cafe")).GetProperty("chats").GetArrayLength());
        Assert.Equal(1, (await client.GetFromJsonAsync<JsonElement>("/api/v1/assistant/chats/search?q=maya")).GetProperty("chats").GetArrayLength());

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(At($"/api/v1/assistant/chats/{chatId}"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(At($"/api/v1/assistant/chats/{chatId}"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync(At($"/api/v1/assistant/chats/{chatId}"))).StatusCode);
    }
}
