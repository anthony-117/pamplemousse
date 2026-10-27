using System.Net;
using System.Text;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Pamplemousse.Infrastructure.Caching;

namespace Pamplemousse.Infrastructure.Tests;

/// <summary>
/// Answers HTTP requests from canned responses (first rule whose URL fragment matches) and records every URL.
/// Unmatched requests fail the test with 500, so a provider calling an unexpected URL is visible.
/// </summary>
public sealed class StubHttp : HttpMessageHandler
{
    private readonly List<(string UrlContains, HttpStatusCode Status, string Body)> _rules = [];

    public IList<Uri> Requests { get; } = new List<Uri>();

    public StubHttp On(string urlContains, string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        _rules.Add((urlContains, status, body));
        return this;
    }

    public HttpClient Client() => new(this);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!;
        Requests.Add(url);
        var rule = _rules.FirstOrDefault(r => url.ToString().Contains(r.UrlContains, StringComparison.Ordinal));
        var response = rule.UrlContains is null
            ? new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent($"No stub for {url}") }
            : new HttpResponseMessage(rule.Status) { Content = new StringContent(rule.Body, Encoding.UTF8, "application/json") };
        return Task.FromResult(response);
    }
}

/// <summary>A clock frozen at a given UTC date.</summary>
public sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

public static class TestDoubles
{
    public static JsonCache Cache() => new(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));

    public static IOptions<ExternalApiOptions> Keys(string openTripMap = "otm-key", string ticketmaster = "tm-key") =>
        Options.Create(new ExternalApiOptions { OpenTripMapApiKey = openTripMap, TicketmasterApiKey = ticketmaster });

    public static IDistributedCache MemoryCache() => new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
}
