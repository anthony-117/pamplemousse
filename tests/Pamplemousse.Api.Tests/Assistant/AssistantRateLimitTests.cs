using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Pamplemousse.Api.Tests.Assistant;

/// <summary>Spec 010 FR-031: sending is limited per account and overall; reading chats and other routes are not.</summary>
public sealed class AssistantRateLimitTests : IDisposable
{
    private const string Url = "/api/v1/assistant/messages";

    private readonly TestApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private static object Question() => new
    {
        messages = new[] { new { role = "traveller", text = "Do I need a visa?" } },
        context = new
        {
            from = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(60).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            to = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(64).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        },
    };

    private HttpClient As(string uid, string? ip = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = TestTokens.Bearer(TestTokens.For(uid));
        if (ip is not null)
        {
            client.DefaultRequestHeaders.Add("X-Forwarded-For", ip);
        }

        return client;
    }

    private static async Task<HttpStatusCode[]> SendAsync(HttpClient client, int count, string url = Url)
    {
        var codes = new HttpStatusCode[count];
        for (var i = 0; i < count; i++)
        {
            codes[i] = url.EndsWith("/chats", StringComparison.Ordinal)
                ? (await client.PostAsJsonAsync(url, new { text = "Hi" })).StatusCode
                : (await client.PostAsJsonAsync(url, Question())).StatusCode;
        }

        return codes;
    }

    [Fact]
    public async Task An_account_past_its_minute_limit_gets_429_with_Retry_After_while_another_account_on_the_same_network_still_sends()
    {
        _factory.Limits.PerClientPerMinute = 2;
        using var fares = As("uid-fares", "198.51.100.10");
        using var maya = As("uid-maya", "198.51.100.10");

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK], await SendAsync(fares, 2));
        var rejected = await fares.PostAsJsonAsync(Url, Question());
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        var retryAfter = int.Parse(Assert.Single(rejected.Headers.GetValues("Retry-After")), CultureInfo.InvariantCulture);
        Assert.InRange(retryAfter, 1, 60);
        var problem = await rejected.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Too many messages to the assistant. Wait a minute and try again.", problem.GetProperty("detail").GetString());

        Assert.Equal([HttpStatusCode.OK], await SendAsync(maya, 1));
    }

    [Fact]
    public async Task The_day_limit_holds_even_when_the_minute_limit_is_not_reached()
    {
        _factory.Limits.PerClientPerDay = 2;
        using var fares = As("uid-fares");

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], await SendAsync(fares, 3));
    }

    [Fact]
    public async Task All_accounts_together_share_one_minute_limit()
    {
        _factory.Limits.AllClientsPerMinute = 2;

        Assert.Equal([HttpStatusCode.OK], await SendAsync(As("uid-a"), 1));
        Assert.Equal([HttpStatusCode.OK], await SendAsync(As("uid-b"), 1));
        Assert.Equal([HttpStatusCode.TooManyRequests], await SendAsync(As("uid-c"), 1));
    }

    [Fact]
    public async Task A_new_chat_counts_as_a_send_but_reading_chats_and_other_routes_do_not()
    {
        _factory.Limits.PerClientPerMinute = 1;
        using var client = As("uid-reader");

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.TooManyRequests], await SendAsync(client, 2, "/api/v1/assistant/chats"));
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(new Uri("/api/v1/assistant/chats", UriKind.Relative))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(new Uri("/api/v1/system/ping", UriKind.Relative))).StatusCode);
        }
    }
}
