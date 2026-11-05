using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Domain.Common;
using Pamplemousse.Infrastructure.Caching;

namespace Pamplemousse.Infrastructure.Providers;

/// <summary>
/// Places around a hotel from OpenTripMap (free key, OPENTRIPMAP_API_KEY), for weighted search (spec 007, research R-1).
/// Two requests per position, kept 30 days: hotels don't move and places change slowly. The API returns at most the
/// 500 nearest places, so a count of 500 means 500 or more.
/// </summary>
public sealed class OpenTripMapNearbyPlaces(HttpClient http, JsonCache cache, IOptions<ExternalApiOptions> options) : INearbyPlaces
{
    private const string BaseUrl = "https://api.opentripmap.com/0.1/en/places/radius";
    private const int Limit = 500;
    private static readonly TimeSpan KeepFor = TimeSpan.FromDays(30);

    private static readonly HashSet<string> FoodKinds = new(["restaurants", "cafes"], StringComparer.Ordinal);
    private static readonly HashSet<string> NightKinds = new(["bars", "pubs", "nightclubs"], StringComparer.Ordinal);

    public Task<NearbyCounts> CountAsync(GeoPoint position, CancellationToken ct)
    {
        var key = options.Value.OpenTripMapApiKey is { Length: > 0 } k
            ? k
            : throw new ProviderNotConfiguredException("OpenTripMap", "OPENTRIPMAP_API_KEY");

        // About 10 m of rounding, so the same hotel always hits the same entry.
        var at = string.Create(CultureInfo.InvariantCulture, $"{position.Lat:F4}:{position.Lng:F4}");
        return cache.GetOrCreateAsync($"nearby:otm:v1:{at}", KeepFor, async token =>
        {
            var food = 0;
            var night = 0;
            foreach (var kinds in await KindsAsync(position, 1000, "restaurants,cafes,bars,pubs,nightclubs", key, token))
            {
                food += kinds.Overlaps(FoodKinds) ? 1 : 0;
                night += kinds.Overlaps(NightKinds) ? 1 : 0;
            }

            var nature = (await KindsAsync(position, 2000, "natural,gardens_and_parks", key, token)).Count;
            return new NearbyCounts(food, night, nature);
        }, ct);
    }

    private async Task<List<HashSet<string>>> KindsAsync(GeoPoint position, int radiusMeters, string kinds, string apiKey, CancellationToken ct)
    {
        var url = new Uri(string.Create(
            CultureInfo.InvariantCulture,
            $"{BaseUrl}?radius={radiusMeters}&lon={position.Lng:F4}&lat={position.Lat:F4}&kinds={kinds}&format=json&limit={Limit}&apikey={apiKey}"));
        using var doc = JsonDocument.Parse(await http.GetStringAsync(url, ct));
        return [.. doc.RootElement.EnumerateArray()
            .Select(e => e.TryGetProperty("kinds", out var k) && k.ValueKind == JsonValueKind.String ? k.GetString() ?? "" : "")
            .Select(k => new HashSet<string>(k.Split(',', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal))];
    }
}
