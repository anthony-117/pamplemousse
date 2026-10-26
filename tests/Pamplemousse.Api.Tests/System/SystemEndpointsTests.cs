using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Pamplemousse.Api.Features.System;

namespace Pamplemousse.Api.Tests.System;

public class SystemEndpointsTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Ping_returns_service_name()
    {
        var response = await _client.GetFromJsonAsync<PingResponse>("/api/v1/system/ping");

        Assert.NotNull(response);
        Assert.Equal("pamplemousse", response.Name);
    }

    [Fact]
    public async Task Alive_endpoint_is_healthy()
    {
        var response = await _client.GetAsync(new Uri("/alive", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_route_returns_problem_details()
    {
        var response = await _client.GetAsync(new Uri("/api/v1/does-not-exist", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
