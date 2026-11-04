using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Application.Assistant.Queries.AskAssistant;

namespace Pamplemousse.Api.Tests.Assistant;

public sealed class AssistantEndpointsTests : IClassFixture<TestApiFactory>
{
    private const string Url = "/api/v1/assistant/messages";
    private const string Rome = "3169070";

    private readonly TestApiFactory _factory;
    private readonly HttpClient _client;
    private readonly string _from = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(60).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private readonly string _to = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(64).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public AssistantEndpointsTests(TestApiFactory factory)
    {
        _factory = factory;
        _factory.Chat.Error = null;
        _factory.Chat.ToolToCall = null;
        _factory.Chat.Answer = "Hello from the fake model.";
        _client = factory.CreateClient();
    }

    private Task<HttpResponseMessage> PostAsync(object body) => _client.PostAsJsonAsync(Url, body);

    private object Question(string text = "Do I need a visa?") => new
    {
        messages = new[] { new { role = "traveller", text } },
        context = new { destinationId = Rome, from = _from, to = _to, adults = 2, passport = "LB", currency = "USD" },
    };

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task Answers_with_text_and_a_card_for_the_tool_that_ran()
    {
        _factory.Chat.ToolToCall = "get_overview";
        _factory.Chat.Answer = "With a Lebanese passport you need a visa.";

        var reply = await JsonAsync(await PostAsync(Question()));

        Assert.Equal("With a Lebanese passport you need a visa.", reply.GetProperty("text").GetString());
        var card = Assert.Single(reply.GetProperty("cards").EnumerateArray().ToList());
        Assert.Equal("overview", card.GetProperty("kind").GetString());
        Assert.Equal((Rome, "About Rome", _from, _to), (card.GetProperty("destinationId").GetString(), card.GetProperty("title").GetString(), card.GetProperty("from").GetString(), card.GetProperty("to").GetString()));
        Assert.Equal(JsonValueKind.Null, card.GetProperty("category").ValueKind);
        Assert.Equal(0, reply.GetProperty("warnings").GetArrayLength());
    }

    [Fact]
    public async Task Works_without_any_trip_context()
    {
        var reply = await JsonAsync(await PostAsync(new { messages = new[] { new { role = "traveller", text = "Hi" } } }));

        Assert.Equal("Hello from the fake model.", reply.GetProperty("text").GetString());
        Assert.Equal(0, reply.GetProperty("cards").GetArrayLength());
    }

    [Fact]
    public async Task A_busy_model_is_a_warning_not_an_error()
    {
        _factory.Chat.Error = new AssistantUnavailableException("503 high demand");

        var reply = await JsonAsync(await PostAsync(Question()));

        Assert.Equal("", reply.GetProperty("text").GetString());
        Assert.Equal(AskAssistantHandler.BusyWarning, Assert.Single(reply.GetProperty("warnings").EnumerateArray().ToList()).GetString());
    }

    [Fact]
    public async Task A_missing_key_is_a_warning_naming_the_variable()
    {
        _factory.Chat.Error = new ProviderNotConfiguredException("Assistant", "GEMINI_API_KEY");

        var reply = await JsonAsync(await PostAsync(Question()));

        Assert.Equal(
            "Assistant is not configured. Set GEMINI_API_KEY in the environment or .env file.",
            Assert.Single(reply.GetProperty("warnings").EnumerateArray().ToList()).GetString());
    }

    public static TheoryData<string, string> BadRequests => new()
    {
        { """{"messages":[]}""", "messages" },
        { """{"messages":[{"role":"assistant","text":"Hi"}]}""", "messages" },
        { """{"messages":[{"role":"traveller","text":"   "}]}""", "messages" },
        { """{"messages":[{"role":"bot","text":"Hi"}]}""", "messages" },
        { """{"messages":[{"role":"traveller","text":"Hi"}],"context":{"adults":12}}""", "context.adults" },
        { """{"messages":[{"role":"traveller","text":"Hi"}],"context":{"currency":"DOLLARS"}}""", "context.currency" },
    };

    [Theory]
    [MemberData(nameof(BadRequests))]
    public async Task Bad_input_is_a_400_naming_the_field(string body, string field)
    {
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await _client.PostAsync(Url, content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _), $"no error for {field}");
    }

    [Fact]
    public async Task More_than_twenty_messages_is_a_400()
    {
        var messages = Enumerable.Repeat(new { role = "traveller", text = "Hi" }, 21).ToArray();

        var response = await PostAsync(new { messages });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_city_is_a_404()
    {
        var response = await PostAsync(new { messages = new[] { new { role = "traveller", text = "Hi" } }, context = new { destinationId = "1" } });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
