using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Destinations;
using Pamplemousse.Infrastructure.Providers;

namespace Pamplemousse.Infrastructure.Tests.Providers;

public class OpenMeteoWeatherProviderTests
{
    private static readonly GeoPoint Rome = new(41.8919, 12.5113);
    private static readonly FixedClock Today = new(new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero));

    private const string Daily = """
        {"daily":{"time":["2026-11-01","2026-11-02","2026-11-03"],
          "temperature_2m_max":[21.44,19.0,null],"temperature_2m_min":[9.7,8.0,7.0],
          "precipitation_probability_max":[10,30,40],"weather_code":[1,63,3]}}
        """;

    [Fact]
    public async Task Trip_inside_the_forecast_window_uses_the_forecast()
    {
        var http = new StubHttp().On("api.open-meteo.com/v1/forecast", Daily);
        var provider = new OpenMeteoWeatherProvider(http.Client(), TestDoubles.Cache(), Today);

        var report = await provider.GetAsync(Rome, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 3), CancellationToken.None);

        Assert.Equal(WeatherKind.Forecast, report.Kind);
        Assert.Contains("start_date=2026-11-01&end_date=2026-11-03", http.Requests[0].Query, StringComparison.Ordinal);
        Assert.Equal(2, report.Days.Count); // day 3 has no max temperature yet, so it is skipped
        Assert.Equal(new WeatherDay(new DateOnly(2026, 11, 1), 21.4, 9.7, 10, 1, "Mainly clear"), report.Days[0]);
        Assert.Equal("Rain", report.Days[1].Summary);
    }

    [Fact]
    public async Task Forecast_is_clipped_to_the_16_day_horizon()
    {
        var http = new StubHttp().On("forecast", Daily);
        var provider = new OpenMeteoWeatherProvider(http.Client(), TestDoubles.Cache(), Today);

        await provider.GetAsync(Rome, new DateOnly(2026, 11, 8), new DateOnly(2026, 11, 20), CancellationToken.None);

        Assert.Contains("end_date=2026-11-11", http.Requests[0].Query, StringComparison.Ordinal); // 27 Oct + 15 days
    }

    [Fact]
    public async Task Trip_beyond_the_forecast_uses_last_years_weather_on_the_same_dates()
    {
        var http = new StubHttp().On("archive-api.open-meteo.com", """
            {"daily":{"time":["2025-12-26"],"temperature_2m_max":[15.3],"temperature_2m_min":[5.2],"weather_code":[3]}}
            """);
        var provider = new OpenMeteoWeatherProvider(http.Client(), TestDoubles.Cache(), Today);

        var report = await provider.GetAsync(Rome, new DateOnly(2026, 12, 26), new DateOnly(2026, 12, 30), CancellationToken.None);

        Assert.Equal(WeatherKind.LastYear, report.Kind);
        Assert.Contains("start_date=2025-12-26&end_date=2025-12-30", http.Requests[0].Query, StringComparison.Ordinal);
        Assert.Equal(new DateOnly(2026, 12, 26), report.Days[0].Date); // shifted forward to the trip year
        Assert.Null(report.Days[0].RainChance);
    }
}
