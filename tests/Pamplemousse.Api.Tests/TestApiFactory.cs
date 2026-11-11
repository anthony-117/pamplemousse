using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Pamplemousse.Api.Auth;
using Pamplemousse.Api.RateLimiting;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Destinations;
using Pamplemousse.Domain.Travellers;
using Pamplemousse.Infrastructure;
using Pamplemousse.Infrastructure.Providers;
using Pamplemousse.Infrastructure.Storage;

namespace Pamplemousse.Api.Tests;

/// <summary>
/// Hosts the API with fake free-data providers (no internet) and no API keys, so keyed providers
/// (OpenTripMap, Ticketmaster) take their "not configured" path. Wikidata only answers for Paris.
/// Sign-in trusts <see cref="TestTokens"/>, and every client it makes sends a valid token for <see cref="TestTokens.Uid"/>.
/// </summary>
public sealed class TestApiFactory : WebApplicationFactory<Program>
{
    public FakeExchangeRates Exchange { get; } = new();

    public FakeFlights Flights { get; } = new();

    public FakeHotels Hotels { get; } = new();

    public FakeCars Cars { get; } = new();

    public FakeNearby Nearby { get; } = new();

    public FakeChat Chat { get; } = new();

    public FakeAccounts Accounts { get; } = new();

    /// <summary>The Firebase project sign-in accepts; empty tests the "not configured" path. Set before the first request.</summary>
    public string SignInProject { get; set; } = TestTokens.Project;

    /// <summary>High by default so other tests never meet them; rate-limit tests lower them before the first request.</summary>
    public AssistantRateLimitSettings Limits { get; } = new() { PerClientPerMinute = 1000, PerClientPerDay = 1000, AllClientsPerMinute = 1000 };

    /// <summary>Every request arrives from this address, as from Caddy inside the Docker network.</summary>
    public static readonly IPAddress ProxyAddress = IPAddress.Parse("172.18.0.2");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.PostConfigure<ExternalApiOptions>(o =>
            {
                o.OpenTripMapApiKey = "";
                o.TicketmasterApiKey = "";
            });
            services.PostConfigure<AssistantRateLimitSettings>(o =>
            {
                o.PerClientPerMinute = Limits.PerClientPerMinute;
                o.PerClientPerDay = Limits.PerClientPerDay;
                o.AllClientsPerMinute = Limits.AllClientsPerMinute;
            });
            services.PostConfigure<SignInSettings>(o => o.ProjectId = SignInProject);
            services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
            {
                // Never Google's keys: the developer's .env may name the real project.
                o.Authority = null;
                o.TokenValidationParameters.ValidIssuer = TestTokens.Issuer;
                o.TokenValidationParameters.ValidAudience = TestTokens.Project;
                o.TokenValidationParameters.IssuerSigningKey = TestTokens.Key;
            });
            services.AddSingleton<IAccountDeleter>(Accounts);
            services.AddSingleton<ITravelProfileStore, InMemoryTravelProfiles>();
            services.AddSingleton<IPlanStore, InMemoryPlanStore>();
            services.AddSingleton<ISavedStore, InMemorySavedStore>();
            services.AddSingleton<IChatStore, InMemoryChatStore>();
            services.AddSingleton<IStartupFilter, FromProxy>();
            services.AddSingleton<IWeatherProvider, FakeWeather>();
            services.AddSingleton<IExchangeRateProvider>(Exchange);
            services.AddSingleton<IHolidayProvider, FakeHolidays>();
            services.AddSingleton<IAdvisoryProvider, FakeAdvisories>();
            services.AddSingleton<IFlightProvider>(Flights);
            services.AddSingleton<IHotelProvider>(Hotels);
            services.AddSingleton<ICarProvider>(Cars);
            services.AddSingleton<INearbyPlaces>(Nearby);

            // Never Gemini: the developer's .env may hold a real key. The real tool loop still runs the tools.
            services.AddSingleton<IChatClient>(_ => new FunctionInvokingChatClient(Chat));

            // The developer's .env may name a real Firebase project; tests keep hotel and car data in memory instead.
            services.AddSingleton<InMemoryHotelData>();
            foreach (var warmUp in services.Where(d => d.ImplementationType == typeof(FirestoreWarmUp)).ToList())
            {
                services.Remove(warmUp);
            }

            services.AddSingleton<ICityMappingStore>(sp => sp.GetRequiredService<InMemoryHotelData>());
            services.AddSingleton<IHotelStore>(sp => sp.GetRequiredService<InMemoryHotelData>());
            services.AddSingleton<IPriceHistory>(sp => sp.GetRequiredService<InMemoryHotelData>());
            services.AddSingleton<InMemoryCarData>();
            services.AddSingleton<ICarAirportStore>(sp => sp.GetRequiredService<InMemoryCarData>());
            services.AddSingleton<ICarHistory>(sp => sp.GetRequiredService<InMemoryCarData>());
            services.AddHttpClient<WikidataAttractionProvider>().ConfigurePrimaryHttpMessageHandler(() => new FakeWikidata());
        });
    }

    protected override void ConfigureClient(HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        base.ConfigureClient(client);
        client.DefaultRequestHeaders.Authorization = TestTokens.Bearer(TestTokens.For());
    }

    /// <summary>The test server sets no remote address; production requests come from Caddy.</summary>
    private sealed class FromProxy : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((HttpContext context, RequestDelegate nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = ProxyAddress;
                return nextMiddleware(context);
            });
            next(app);
        };
    }

    /// <summary>Knows one landmark in Paris; everywhere else Wikidata is "down", so attractions fall back to OpenTripMap.</summary>
    private sealed class FakeWikidata : HttpMessageHandler
    {
        private const string ParisLandmark = """
            {"results":{"bindings":[{"item":{"value":"http://www.wikidata.org/entity/Q243"},"label":{"value":"Eiffel Tower"},
             "articles":{"value":"191"},"coord":{"value":"Point(2.2945 48.8583)"},"typeLabel":{"value":"observation tower"}}]}}
            """;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var paris = Uri.UnescapeDataString(request.RequestUri!.Query).Contains("Point(2.3", StringComparison.Ordinal);
            return Task.FromResult(paris
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ParisLandmark, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }

    private sealed class FakeWeather : IWeatherProvider
    {
        public Task<WeatherReport> GetAsync(GeoPoint location, DateOnly firstDay, DateOnly lastDay, CancellationToken ct) =>
            Task.FromResult(new WeatherReport(WeatherKind.Forecast, [new WeatherDay(firstDay, 21.4, 9.7, 10, 1, "Mainly clear")]));
    }

    private sealed class FakeHolidays : IHolidayProvider
    {
        public Task<IReadOnlyList<Holiday>> GetAsync(string countryCode, DateOnly firstDay, DateOnly lastDay, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Holiday>>([new Holiday(firstDay, "Liberation Day", "Festa della Liberazione")]);
    }

    private sealed class FakeAdvisories : IAdvisoryProvider
    {
        public Task<TravelAdvisory?> GetAsync(Country country, CancellationToken ct) =>
            Task.FromResult<TravelAdvisory?>(new TravelAdvisory(2, "Exercise Increased Caution", "Be aware.", "https://travel.state.gov"));
    }
}

/// <summary>Returns a fixed EUR rate, or throws when <see cref="Fail"/> is set (to test partial responses).</summary>
public sealed class FakeExchangeRates : IExchangeRateProvider
{
    public bool Fail { get; set; }

    public Task<ExchangeRate?> GetAsync(string baseCurrency, string quoteCurrency, CancellationToken ct) => Fail
        ? throw new HttpRequestException("ECB down")
        : Task.FromResult<ExchangeRate?>(new ExchangeRate(baseCurrency, quoteCurrency, 0.89m, new DateOnly(2026, 10, 27), "test"));
}

/// <summary>Records the Firebase accounts deleted, or fails when <see cref="Fail"/> is set.</summary>
public sealed class FakeAccounts : IAccountDeleter
{
    private readonly ConcurrentQueue<string> _deleted = new();

    public bool Fail { get; set; }

    public IEnumerable<string> Deleted => _deleted;

    public Task DeleteAsync(TravellerId traveller, CancellationToken ct)
    {
        if (Fail)
        {
            throw new ServiceUnavailableException("The sign-in service can't be reached.");
        }

        _deleted.Enqueue(traveller.Value);
        return Task.CompletedTask;
    }
}
