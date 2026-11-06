using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pamplemousse.Api.ViewModels;

namespace Pamplemousse.Api.Tests.Account;

/// <summary>GET, PUT and DELETE /api/v1/me (spec 009, Stories 3 and 4).</summary>
public class MeEndpointsTests(TestApiFactory factory) : IClassFixture<TestApiFactory>
{
    private static readonly Uri Me = new("/api/v1/me", UriKind.Relative);

    // Each test signs in as its own traveller, so tests sharing the API never see each other's profile.
    private readonly string _uid = "uid-" + Guid.NewGuid().ToString("N")[..10];

    private HttpClient As(string? uid = null, string? email = "fares@example.com", string? name = "Fares Haddad", DateTimeOffset? authTime = null)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = TestTokens.Bearer(TestTokens.For(uid ?? _uid, email: email, name: name, authTime: authTime));
        return client;
    }

    private static object Form(string passport = "LB", string? city = "276781", int? budget = 90) => new
    {
        displayName = "Fares H",
        passport,
        homeCurrency = "EUR",
        homeCityId = city,
        preferences = new { budget, culture = 90, food = 50, nature = 50, nightlife = 20, comfort = 50 },
    };

    [Fact]
    public async Task The_first_read_makes_the_profile_from_the_sign_in_and_the_defaults()
    {
        using var client = As(name: null, email: "maya.k@example.com");

        var profile = await client.GetFromJsonAsync<ProfileViewModel>(Me);

        Assert.NotNull(profile);
        Assert.Equal(("maya.k", "maya.k@example.com", "LB", "USD"), (profile.DisplayName, profile.Email, profile.Passport, profile.HomeCurrency));
        Assert.Null(profile.HomeCity);
        Assert.Equal(new WeightsViewModel(50, 50, 50, 50, 50, 50), profile.Preferences);
    }

    [Fact]
    public async Task An_update_is_saved_and_read_back_with_the_home_city()
    {
        using var client = As();

        var put = await client.PutAsJsonAsync(Me, Form());
        var profile = await client.GetFromJsonAsync<ProfileViewModel>(Me);

        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.NotNull(profile);
        Assert.Equal(("Fares H", "LB", "EUR"), (profile.DisplayName, profile.Passport, profile.HomeCurrency));
        Assert.Equal(("276781", "LB"), (profile.HomeCity?.Id, profile.HomeCity?.CountryCode));
        Assert.Equal(new WeightsViewModel(90, 90, 50, 50, 20, 50), profile.Preferences);
        Assert.Equal("fares@example.com", profile.Email);
    }

    [Theory]
    [InlineData("XX", "276781", 90, "passport")]
    [InlineData("LB", "999999999", 90, "homeCityId")]
    [InlineData("LB", "276781", 101, "budget")]
    [InlineData("LB", "276781", null, "budget")]
    public async Task A_bad_value_is_a_400_naming_the_field(string passport, string city, int? budget, string field)
    {
        using var client = As();

        var response = await client.PutAsJsonAsync(Me, Form(passport, city, budget));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.True(errors.TryGetProperty(field, out _), field);
    }

    [Fact]
    public async Task Travellers_never_see_each_others_profile()
    {
        using var fares = As();
        using var maya = As(uid: _uid + "-maya", email: "maya@example.com", name: "Maya");

        await fares.PutAsJsonAsync(Me, Form());
        var mayas = await maya.GetFromJsonAsync<ProfileViewModel>(Me);

        Assert.Equal(("Maya", "maya@example.com", "USD"), (mayas?.DisplayName, mayas?.Email, mayas?.HomeCurrency));
    }

    [Fact]
    public async Task Deleting_needs_a_recent_sign_in()
    {
        using var client = As(authTime: DateTimeOffset.UtcNow.AddMinutes(-10));

        var response = await client.DeleteAsync(Me);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("recent-sign-in-required", await SignInTests.CodeOf(response));
        Assert.DoesNotContain(_uid, factory.Accounts.Deleted, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Deleting_removes_the_profile_and_the_sign_in_and_refuses_the_old_token()
    {
        using var client = As();
        await client.PutAsJsonAsync(Me, Form());

        var response = await client.DeleteAsync(Me);
        var after = await client.GetAsync(Me);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains(_uid, factory.Accounts.Deleted, StringComparer.Ordinal);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
        Assert.Equal("sign-in-required", await SignInTests.CodeOf(after));
    }

    [Fact]
    public async Task Signing_up_again_after_deleting_gives_an_empty_profile()
    {
        var uid = _uid + "-again";
        using (var client = As(uid))
        {
            await client.PutAsJsonAsync(Me, Form());
            await client.DeleteAsync(Me);
        }

        // Firebase gives a new account a new id.
        using var fresh = As(uid + "-new");
        var profile = await fresh.GetFromJsonAsync<ProfileViewModel>(Me);

        Assert.Equal(("USD", null), (profile?.HomeCurrency, profile?.HomeCity));
    }
}

/// <summary>The sign-in service failing part-way through a delete (research R-4).</summary>
public sealed class DeleteAccountFailureTests : IDisposable
{
    private readonly TestApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task A_failure_is_a_503_and_the_sign_in_keeps_working_for_a_retry()
    {
        _factory.Accounts.Fail = true;
        using var client = _factory.CreateClient();
        var me = new Uri("/api/v1/me", UriKind.Relative);

        var response = await client.DeleteAsync(me);
        var after = await client.GetAsync(me);
        _factory.Accounts.Fail = false;
        var retry = await client.DeleteAsync(me);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("service-unavailable", await SignInTests.CodeOf(response));
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, retry.StatusCode);
        Assert.Equal([TestTokens.Uid], _factory.Accounts.Deleted, StringComparer.Ordinal);
    }
}
