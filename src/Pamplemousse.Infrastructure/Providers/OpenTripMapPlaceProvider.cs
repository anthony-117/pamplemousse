using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Pamplemousse.Domain.Abstractions;
using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Destinations;
using Pamplemousse.Domain.Places;
using Pamplemousse.Infrastructure.Caching;

namespace Pamplemousse.Infrastructure.Providers;

/// <summary>OpenTripMap (free key, OPENTRIPMAP_API_KEY): popular attractions around a city, with Wikipedia descriptions.</summary>
public sealed class OpenTripMapPlaceProvider(HttpClient http, JsonCache cache, IOptions<ExternalApiOptions> options) : IPlaceProvider
{
    private const string BaseUrl = "https://api.opentripmap.com/0.1/en/places";
    private const int RadiusMeters = 10_000;

    public string Source => "otm";

    public PlaceCategory Category => PlaceCategory.Attraction;

    private string ApiKey => options.Value.OpenTripMapApiKey is { Length: > 0 } key
        ? key
        : throw new ProviderNotConfiguredException("OpenTripMap", "OPENTRIPMAP_API_KEY");

    public Task<IReadOnlyList<Place>> SearchAsync(Destination destination, int limit, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var key = ApiKey;
        var lat = destination.Location.Lat.ToString("F4", CultureInfo.InvariantCulture);
        var lng = destination.Location.Lng.ToString("F4", CultureInfo.InvariantCulture);

        return cache.GetOrCreateAsync<IReadOnlyList<Place>>($"places:otm:{destination.Id}:{limit}", TimeSpan.FromHours(24), async token =>
        {
            // rate=3 keeps well-known places; ask for extra because some results have no name.
            var url = $"{BaseUrl}/radius?radius={RadiusMeters}&lon={lng}&lat={lat}&kinds=interesting_places&rate=3&format=json&limit={limit * 2}&apikey={key}";
            using var doc = JsonDocument.Parse(await http.GetStringAsync(new Uri(url), token));
            return doc.RootElement.EnumerateArray()
                .Select(e => (JsonElement?)e)
                .Where(e => !string.IsNullOrWhiteSpace(e.Prop("name").Str()))
                .OrderByDescending(e => Popularity(e.Prop("rate")))
                .ThenBy(e => e.Prop("dist").Num())
                .Take(limit)
                .Select(e => ToPlace(e, description: null, address: null, image: null, website: null))
                .ToList();
        }, ct);
    }

    public Task<Place?> GetAsync(string providerId, CancellationToken ct)
    {
        var key = ApiKey;
        return cache.GetOrCreateAsync<Place?>($"place:otm:{providerId}", TimeSpan.FromHours(24), async token =>
        {
            var url = $"{BaseUrl}/xid/{Uri.EscapeDataString(providerId)}?apikey={key}";
            using var response = await http.GetAsync(new Uri(url), token);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            JsonElement? e = doc.RootElement;
            var address = e.Prop("address");
            var parts = new[] { address.Prop("road").Str(), address.Prop("house_number").Str(), address.Prop("city").Str() }
                .Where(p => !string.IsNullOrWhiteSpace(p));
            return ToPlace(
                e,
                description: e.Prop("wikipedia_extracts").Prop("text").Str(),
                address: string.Join(", ", parts) is { Length: > 0 } a ? a : null,
                image: e.Prop("preview").Prop("source").Str(),
                website: e.Prop("url").Str() ?? e.Prop("wikipedia").Str());
        }, ct);
    }

    private static Place ToPlace(JsonElement? e, string? description, string? address, string? image, string? website)
    {
        var point = e.Prop("point");
        return new Place(
            $"otm:{e.Prop("xid").Str()}",
            e.Prop("name").Str() ?? "",
            PlaceCategory.Attraction,
            new GeoPoint(point.Prop("lat").Num() ?? 0, point.Prop("lon").Num() ?? 0),
            PrettyKind(e.Prop("kinds").Str()),
            Rating: null,
            RatingCount: null,
            PriceLevel: null,
            address,
            description,
            image,
            website,
            OpeningHours: [],
            "OpenTripMap");
    }

    /// <summary>OpenTripMap rates popularity 1–3, with "h" marking cultural heritage (e.g. 7 or "3h"); "h" ranks slightly higher.</summary>
    private static double Popularity(JsonElement? rate) => rate switch
    {
        { ValueKind: JsonValueKind.Number } n => n.GetDouble(),
        { ValueKind: JsonValueKind.String } s when s.GetString() is { Length: > 0 } text =>
            double.TryParse(text.TrimEnd('h'), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value + (text.EndsWith('h') ? 0.5 : 0) : 0,
        _ => 0,
    };

    /// <summary>"historic_architecture,interesting_places" → "Historic architecture".</summary>
    private static string? PrettyKind(string? kinds)
    {
        var first = kinds?.Split(',', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(k => k != "interesting_places");
        if (string.IsNullOrEmpty(first))
        {
            return null;
        }

        var words = first.Replace('_', ' ');
        return char.ToUpperInvariant(words[0]) + words[1..];
    }
}
