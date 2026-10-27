using System.Globalization;
using System.Net;
using System.Text.Json;
using Pamplemousse.Domain.Abstractions;
using Pamplemousse.Domain.Destinations;
using Pamplemousse.Infrastructure.Caching;

namespace Pamplemousse.Infrastructure.Providers;

/// <summary>Nager.Date (free, no key): nationwide public holidays.</summary>
public sealed class NagerHolidayProvider(HttpClient http, JsonCache cache) : IHolidayProvider
{
    public async Task<IReadOnlyList<Holiday>> GetAsync(string countryCode, DateOnly firstDay, DateOnly lastDay, CancellationToken ct)
    {
        var holidays = new List<Holiday>();
        for (var year = firstDay.Year; year <= lastDay.Year; year++)
        {
            holidays.AddRange(await GetYearAsync(countryCode.ToUpperInvariant(), year, ct));
        }

        return [.. holidays.Where(h => h.Date >= firstDay && h.Date <= lastDay).OrderBy(h => h.Date)];
    }

    private Task<List<Holiday>> GetYearAsync(string countryCode, int year, CancellationToken ct) =>
        cache.GetOrCreateAsync($"holidays:{countryCode}:{year}", TimeSpan.FromDays(30), async token =>
        {
            using var response = await http.GetAsync(new Uri($"https://date.nager.at/api/v3/PublicHolidays/{year}/{countryCode}"), token);
            if (response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.NotFound)
            {
                return new List<Holiday>(); // country not covered
            }

            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            return doc.RootElement.EnumerateArray()
                .Where(h => ((JsonElement?)h).Prop("global") is not { ValueKind: JsonValueKind.False })
                .Select(h => new Holiday(
                    DateOnly.ParseExact(((JsonElement?)h).Prop("date").Str()!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                    ((JsonElement?)h).Prop("name").Str() ?? "",
                    ((JsonElement?)h).Prop("localName").Str() ?? ""))
                .ToList();
        }, ct);
}
