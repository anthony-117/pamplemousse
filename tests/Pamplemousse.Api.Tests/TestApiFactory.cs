using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Pamplemousse.Domain.Abstractions;
using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Destinations;
using Pamplemousse.Infrastructure;

namespace Pamplemousse.Api.Tests;

/// <summary>
/// Hosts the API with fake free-data providers (no internet) and no API keys, so keyed providers
/// (Google Places, OpenTripMap, Ticketmaster) take their "not configured" path.
/// </summary>
public sealed class TestApiFactory : WebApplicationFactory<Program>
{
    public FakeExchangeRates Exchange { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.PostConfigure<ExternalApiOptions>(o =>
            {
                o.GooglePlacesApiKey = "";
                o.OpenTripMapApiKey = "";
                o.TicketmasterApiKey = "";
            });
            services.AddSingleton<IWeatherProvider, FakeWeather>();
            services.AddSingleton<IExchangeRateProvider>(Exchange);
            services.AddSingleton<IHolidayProvider, FakeHolidays>();
            services.AddSingleton<IAdvisoryProvider, FakeAdvisories>();
        });
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
