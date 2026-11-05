using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Pamplemousse.Api.Tests.Assistant;

/// <summary>Spec 003, FR-013: the assistant is limited per client (the IP Caddy forwards) and overall; other endpoints are not.</summary>
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

    private HttpClient ClientAt(string ip)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", ip);
        return client;
    }

    private static async Task<HttpStatusCode[]> SendAsync(HttpClient client, int count)
    {
        var codes = new HttpStatusCode[count];
        for (var i = 0; i < count; i++)
        {
            codes[i] = (await client.PostAsJsonAsync(Url, Question())).StatusCode;
        }

        return codes;
    }

    [Fact]
    public async Task A_client_past_its_minute_limit_gets_429_with_Retry_After_while_another_client_still_gets_answers()
    {
        _factory.Limits.PerClientPerMinute = 2;
        var first = ClientAt("203.0.113.7");

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK], await SendAsync(first, 2));
        var rejected = await first.PostAsJsonAsync(Url, Question());

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        var retryAfter = int.Parse(Assert.Single(rejected.Headers.GetValues("Retry-After")), CultureInfo.InvariantCulture);
        Assert.InRange(retryAfter, 1, 60);
        var problem = await rejected.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Too many messages to the assistant. Wait a minute and try again.", problem.GetProperty("detail").GetString());

        Assert.Equal([HttpStatusCode.OK], await SendAsync(ClientAt("198.51.100.20"), 1));
    }

    [Fact]
    public async Task The_day_limit_holds_even_when_the_minute_limit_is_not_reached()
    {
        _factory.Limits.PerClientPerDay = 2;

        Assert.Equal(
            [HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests],
            await SendAsync(ClientAt("203.0.113.7"), 3));
    }

    [Fact]
    public async Task All_clients_together_share_one_minute_limit()
    {
        _factory.Limits.AllClientsPerMinute = 2;

        Assert.Equal([HttpStatusCode.OK], await SendAsync(ClientAt("203.0.113.1"), 1));
        Assert.Equal([HttpStatusCode.OK], await SendAsync(ClientAt("203.0.113.2"), 1));
        Assert.Equal([HttpStatusCode.TooManyRequests], await SendAsync(ClientAt("203.0.113.3"), 1));
    }

    [Fact]
    public async Task Other_endpoints_are_not_limited()
    {
        _factory.Limits.PerClientPerMinute = 1;
        var client = ClientAt("203.0.113.7");
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.TooManyRequests], await SendAsync(client, 2));

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(new Uri("/api/v1/system/ping", UriKind.Relative))).StatusCode);
        }
    }

    [Fact]
    public async Task Without_a_forwarded_address_the_proxy_itself_is_the_client()
    {
        _factory.Limits.PerClientPerMinute = 1;
        var direct = _factory.CreateClient();

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.TooManyRequests], await SendAsync(direct, 2));
        Assert.Equal([HttpStatusCode.OK], await SendAsync(ClientAt("203.0.113.7"), 1));
    }
}
