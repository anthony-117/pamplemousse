using System.Globalization;
using System.Text.Json;
using Pamplemousse.Domain.Abstractions;
using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Destinations;
using Pamplemousse.Infrastructure.Caching;

namespace Pamplemousse.Infrastructure.Providers;

/// <summary>
/// Open-Meteo (free, no key). Trips starting within the 16-day forecast window get the live forecast;
/// later trips get the observed weather on the same dates last year.
/// </summary>
public sealed class OpenMeteoWeatherProvider(HttpClient http, JsonCache cache, TimeProvider clock) : IWeatherProvider
{
    internal const int ForecastDays = 16;

    public Task<WeatherReport> GetAsync(GeoPoint location, DateOnly firstDay, DateOnly lastDay, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var horizon = today.AddDays(ForecastDays - 1);
        var lat = location.Lat.ToString("F3", CultureInfo.InvariantCulture);
        var lng = location.Lng.ToString("F3", CultureInfo.InvariantCulture);

        if (firstDay <= horizon)
        {
            var start = firstDay < today ? today : firstDay;
            var end = lastDay > horizon ? horizon : lastDay;
            var url = $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lng}&timezone=auto" +
                      $"&daily=temperature_2m_max,temperature_2m_min,precipitation_probability_max,weather_code&start_date={start:yyyy-MM-dd}&end_date={end:yyyy-MM-dd}";
            return cache.GetOrCreateAsync($"weather:forecast:{lat}:{lng}:{start:yyyyMMdd}:{end:yyyyMMdd}", TimeSpan.FromHours(3),
                async token => new WeatherReport(WeatherKind.Forecast, await FetchAsync(url, 0, token)), ct);
        }

        var lastYearUrl = $"https://archive-api.open-meteo.com/v1/archive?latitude={lat}&longitude={lng}&timezone=auto" +
                          $"&daily=temperature_2m_max,temperature_2m_min,weather_code&start_date={firstDay.AddYears(-1):yyyy-MM-dd}&end_date={lastDay.AddYears(-1):yyyy-MM-dd}";
        return cache.GetOrCreateAsync($"weather:lastyear:{lat}:{lng}:{firstDay:yyyyMMdd}:{lastDay:yyyyMMdd}", TimeSpan.FromDays(30),
            async token => new WeatherReport(WeatherKind.LastYear, await FetchAsync(lastYearUrl, 1, token)), ct);
    }

    /// <summary>Reads Open-Meteo's daily arrays; <paramref name="shiftYears"/> moves dates forward so last year's days line up with the trip.</summary>
    private async Task<IReadOnlyList<WeatherDay>> FetchAsync(string url, int shiftYears, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(await http.GetStringAsync(new Uri(url), ct));
        var daily = doc.RootElement.Prop("daily");
        var dates = daily.Prop("time").Items().ToList();
        var max = daily.Prop("temperature_2m_max").Items().ToList();
        var min = daily.Prop("temperature_2m_min").Items().ToList();
        var rain = daily.Prop("precipitation_probability_max").Items().ToList();
        var codes = daily.Prop("weather_code").Items().ToList();

        var days = new List<WeatherDay>(dates.Count);
        for (var i = 0; i < dates.Count; i++)
        {
            var high = i < max.Count ? ((JsonElement?)max[i]).Num() : null;
            var low = i < min.Count ? ((JsonElement?)min[i]).Num() : null;
            if (high is null || low is null)
            {
                continue; // day not available yet
            }

            var code = i < codes.Count ? ((JsonElement?)codes[i]).Int() ?? -1 : -1;
            days.Add(new WeatherDay(
                DateOnly.ParseExact(dates[i].GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddYears(shiftYears),
                Math.Round(high.Value, 1),
                Math.Round(low.Value, 1),
                i < rain.Count ? ((JsonElement?)rain[i]).Int() : null,
                code,
                WeatherCodes.Describe(code)));
        }

        return days;
    }
}
