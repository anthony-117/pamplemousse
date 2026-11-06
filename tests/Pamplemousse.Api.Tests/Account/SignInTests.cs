using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Pamplemousse.Api.Tests.Account;

/// <summary>Every refusal of contracts/accounts-api.md (spec 009, FR-007, SC-002), on a route that needs a sign-in.</summary>
public class SignInTests(TestApiFactory factory) : IClassFixture<TestApiFactory>
{
    private static readonly Uri Countries = new("/api/v1/countries", UriKind.Relative);

    private async Task<HttpResponseMessage> GetAsync(Uri uri, string? token)
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = token is null ? null : TestTokens.Bearer(token);
        return await client.GetAsync(uri);
    }

    internal static async Task<string?> CodeOf(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    [Fact]
    public async Task A_valid_sign_in_is_let_through()
    {
        var response = await GetAsync(Countries, TestTokens.For());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task No_sign_in_is_refused_with_sign_in_required()
    {
        var response = await GetAsync(Countries, token: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("sign-in-required", await CodeOf(response));
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
    }

    public static TheoryData<string, string> BadTokens => new()
    {
        { "expired", TestTokens.For(issuedAt: DateTimeOffset.UtcNow.AddHours(-2)) },
        { "another project", TestTokens.For(audience: "someone-elses-project") },
        { "another issuer", TestTokens.For(issuer: "https://securetoken.google.com/someone-elses-project") },
        { "forged", TestTokens.For(key: TestTokens.OtherKey) },
        { "edited", TestTokens.For()[..^2] + "AA" },
        { "no user id", TestTokens.For(uid: null) },
        { "not a token", "hello" },
    };

    [Theory]
    [MemberData(nameof(BadTokens))]
    public async Task A_bad_sign_in_is_refused_without_saying_why(string why, string token)
    {
        var response = await GetAsync(Countries, token);

        Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, why);
        Assert.Equal("sign-in-required", await CodeOf(response));
        Assert.DoesNotContain("expire", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public async Task An_unconfirmed_email_is_refused_with_email_not_confirmed(bool? verified)
    {
        var response = await GetAsync(Countries, TestTokens.For(verified: verified));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("email-not-confirmed", await CodeOf(response));
    }

    [Fact]
    public async Task An_unknown_route_is_a_404_once_signed_in()
    {
        var response = await GetAsync(new Uri("/api/v1/does-not-exist", UriKind.Relative), TestTokens.For());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/system/ping")]
    [InlineData("/alive")]
    [InlineData("/health")]
    [InlineData("/openapi/v1.json")]
    public async Task Open_routes_need_no_sign_in(string path)
    {
        var response = await GetAsync(new Uri(path, UriKind.Relative), token: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_browser_can_read_a_refusal()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = null;
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:5555");

        var response = await client.GetAsync(Countries);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("http://localhost:5555", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }
}

/// <summary>Without a Firebase project, nothing is let through (research R-6).</summary>
public sealed class SignInNotConfiguredTests : IDisposable
{
    private readonly TestApiFactory _factory = new() { SignInProject = "" };

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Every_protected_route_answers_503_naming_the_variable()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/v1/me", UriKind.Relative));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("sign-in-not-configured", await SignInTests.CodeOf(response));
        Assert.Contains("FIREBASE_PROJECT_ID", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ping_still_answers()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/v1/system/ping", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
