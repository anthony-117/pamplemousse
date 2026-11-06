using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Pamplemousse.Api.ViewModels;

namespace Pamplemousse.Api.Tests.System;

public class SystemEndpointsTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Ping_returns_service_name()
    {
        var response = await _client.GetFromJsonAsync<PingViewModel>("/api/v1/system/ping");

        Assert.NotNull(response);
        Assert.Equal("pamplemousse", response.Name);
    }

    [Fact]
    public async Task Alive_endpoint_is_healthy()
    {
        var response = await _client.GetAsync(new Uri("/alive", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/openapi/v1.json", "application/json")]
    [InlineData("/scalar", "text/html")]
    public async Task Api_reference_is_served_in_development(string path, string mediaType)
    {
        var response = await _client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(mediaType, response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("http://localhost:61234", true)]
    [InlineData("https://evil.example.com", false)]
    public async Task Cors_allows_localhost_origins_in_development(string origin, bool allowed)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/system/ping");
        request.Headers.Add("Origin", origin);

        var response = await _client.SendAsync(request);

        Assert.Equal(allowed, response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Unknown_route_says_nothing_until_signed_in()
    {
        // This client sends no sign-in, so the route's existence isn't revealed; signed in, it's a 404 (SignInTests).
        var response = await _client.GetAsync(new Uri("/api/v1/does-not-exist", UriKind.Relative));

        Assert.True(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.ServiceUnavailable);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
