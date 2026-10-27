using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Pamplemousse.Domain.Abstractions;
using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Destinations;
using Pamplemousse.Domain.Places;
using Pamplemousse.Infrastructure.Caching;

namespace Pamplemousse.Infrastructure.Providers;

/// <summary>
/// Attractions: popular "interesting places" across the whole city. OpenTripMap only rates popularity coarsely,
/// so this is the fallback behind <see cref="WikidataAttractionProvider"/>, which ranks by fame.
/// </summary>
public sealed class OpenTripMapAttractionProvider(HttpClient http, JsonCache cache, IOptions<ExternalApiOptions> options)
    : OpenTripMapPlaceProvider(http, cache, options)
{
    /// <summary>Half the side of the search square around the city point.</summary>
    private const double HalfSideKm = 10;

    public override PlaceCategory Category => PlaceCategory.Attraction;

    /// <summary>
    /// The radius search returns the places nearest the city point first, so 500 results only cover ~2 km in a dense
    /// city and miss landmarks a little further out. The bounding-box search spreads the results over the whole area.
    /// rate=3 keeps well-known places.
    /// </summary>
    protected override Uri SearchUrl(Destination destination, string apiKey)
    {
        var (lat, lng) = (destination.Location.Lat, destination.Location.Lng);
        var dLat = HalfSideKm / KmPerDegree;
        var dLng = HalfSideKm / (KmPerDegree * Math.Cos(lat * Math.PI / 180));
        return new Uri(string.Create(
            CultureInfo.InvariantCulture,
            $"{BaseUrl}/bbox?lon_min={lng - dLng:F4}&lon_max={lng + dLng:F4}&lat_min={lat - dLat:F4}&lat_max={lat + dLat:F4}&kinds=interesting_places&rate=3&format=json&limit={CandidatePool}&apikey={apiKey}"));
    }

    /// <summary>Restaurants in a historic building are also tagged "interesting_places"; they belong to restaurants only.</summary>
    protected override bool Keep(Candidate candidate) => !candidate.HasKind("foods");

    // One landmark is often several OpenTripMap objects (the Colosseum twice): they share a Wikidata id. Keep the best-rated one.
    protected override Task<IEnumerable<Candidate>> RankAsync(IReadOnlyList<Candidate> candidates, CancellationToken ct) =>
        Task.FromResult<IEnumerable<Candidate>>(candidates
            .GroupBy(c => c.Wikidata ?? c.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(c => c.Rate).ThenBy(c => c.DistanceKm).First())
            .OrderByDescending(c => c.Rate)
            .ThenBy(c => c.DistanceKm));
}

/// <summary>Restaurants, cafés and bars ("foods") nearest the city point. No ratings, prices or opening hours in this source.</summary>
public sealed class OpenTripMapRestaurantProvider(HttpClient http, JsonCache cache, IOptions<ExternalApiOptions> options)
    : OpenTripMapPlaceProvider(http, cache, options)
{
    private const int RadiusMeters = 10_000;

    public override PlaceCategory Category => PlaceCategory.Restaurant;

    protected override Uri SearchUrl(Destination destination, string apiKey) => new(string.Create(
        CultureInfo.InvariantCulture,
        $"{BaseUrl}/radius?radius={RadiusMeters}&lon={destination.Location.Lng:F4}&lat={destination.Location.Lat:F4}&kinds=foods&format=json&limit={CandidatePool}&apikey={apiKey}"));

    protected override Task<IEnumerable<Candidate>> RankAsync(IReadOnlyList<Candidate> candidates, CancellationToken ct) =>
        Task.FromResult<IEnumerable<Candidate>>(candidates
            .DistinctBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(c => c.Rate)
            .ThenBy(c => c.DistanceKm));
}

/// <summary>OpenTripMap (free key, OPENTRIPMAP_API_KEY): places around a city, one subclass per category.</summary>
public abstract class OpenTripMapPlaceProvider(HttpClient http, JsonCache cache, IOptions<ExternalApiOptions> options) : IPlaceProvider
{
    protected const string BaseUrl = "https://api.opentripmap.com/0.1/en/places";

    /// <summary>How many places to fetch before filtering and ranking (the API maximum).</summary>
    protected const int CandidatePool = 500;

    protected const double KmPerDegree = 111.32;

    /// <summary>Kinds that say nothing about the place itself, skipped when picking its label.</summary>
    private static readonly HashSet<string> GenericKinds = ["interesting_places", "foods", "tourist_facilities", "other"];

    /// <summary>Food kinds; a restaurant in a church is labelled "Restaurants", not "Religion".</summary>
    private static readonly HashSet<string> FoodKinds = ["restaurants", "cafes", "pubs", "bars", "fast_food", "biergartens", "food_courts", "picnic_sites"];

    public string Source => "otm";

    public abstract PlaceCategory Category { get; }

    private string ApiKey => options.Value.OpenTripMapApiKey is { Length: > 0 } key
        ? key
        : throw new ProviderNotConfiguredException("OpenTripMap", "OPENTRIPMAP_API_KEY");

    public Task<IReadOnlyList<Place>> SearchAsync(Destination destination, int limit, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var url = SearchUrl(destination, ApiKey);
        var category = Category;

        return cache.GetOrCreateAsync<IReadOnlyList<Place>>($"places:otm:v2:{category}:{destination.Id}:{limit}", TimeSpan.FromHours(24), async token =>
        {
            List<Candidate> candidates;
            using (var doc = JsonDocument.Parse(await http.GetStringAsync(url, token)))
            {
                candidates = doc.RootElement.EnumerateArray()
                    .Select(e => Candidate.From(e, destination.Location))
                    .Where(c => !string.IsNullOrWhiteSpace(c.Name) && Keep(c))
                    .ToList();
            }

            return (await RankAsync(candidates, token))
                .Take(limit)
                .Select(c => NewPlace(c.Xid, c.Name, category, c.Location, c.Kinds, description: null, address: null, image: null, website: null))
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
            var kinds = e.Prop("kinds").Str();
            var point = e.Prop("point");
            return NewPlace(
                e.Prop("xid").Str() ?? providerId,
                e.Prop("name").Str() ?? "",
                kinds?.Split(',').Contains("foods") == true ? PlaceCategory.Restaurant : PlaceCategory.Attraction,
                new GeoPoint(point.Prop("lat").Num() ?? 0, point.Prop("lon").Num() ?? 0),
                kinds,
                description: e.Prop("wikipedia_extracts").Prop("text").Str(),
                address: string.Join(", ", parts) is { Length: > 0 } a ? a : null,
                image: e.Prop("preview").Prop("source").Str(),
                website: e.Prop("url").Str() ?? e.Prop("wikipedia").Str());
        }, ct);
    }

    /// <summary>The list request for this category.</summary>
    protected abstract Uri SearchUrl(Destination destination, string apiKey);

    /// <summary>Whether a named result belongs to this category.</summary>
    protected virtual bool Keep(Candidate candidate) => true;

    /// <summary>Removes duplicates and orders the candidates, best first.</summary>
    protected abstract Task<IEnumerable<Candidate>> RankAsync(IReadOnlyList<Candidate> candidates, CancellationToken ct);

    private static Place NewPlace(string xid, string name, PlaceCategory category, GeoPoint location, string? kinds, string? description, string? address, string? image, string? website) => new(
        $"otm:{xid}",
        name,
        category,
        location,
        PrettyKind(kinds, category),
        Rating: null,
        RatingCount: null,
        PriceLevel: null,
        address,
        description,
        image,
        website,
        OpeningHours: [],
        "OpenTripMap");

    /// <summary>OpenTripMap rates popularity 1–3, with "h" marking cultural heritage (e.g. 7 or "3h"); "h" ranks slightly higher.</summary>
    private static double Popularity(JsonElement? rate) => rate switch
    {
        { ValueKind: JsonValueKind.Number } n => n.GetDouble(),
        { ValueKind: JsonValueKind.String } s when s.GetString() is { Length: > 0 } text =>
            double.TryParse(text.TrimEnd('h'), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value + (text.EndsWith('h') ? 0.5 : 0) : 0,
        _ => 0,
    };

    /// <summary>"historic_architecture,interesting_places" → "Historic architecture"; for restaurants, "religion,foods,cafes" → "Cafes".</summary>
    private static string? PrettyKind(string? kinds, PlaceCategory category)
    {
        var all = kinds?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? [];
        var first = category == PlaceCategory.Restaurant
            ? all.FirstOrDefault(FoodKinds.Contains) ?? "restaurants"
            : all.FirstOrDefault(k => !GenericKinds.Contains(k));
        if (string.IsNullOrEmpty(first))
        {
            return null;
        }

        var words = first.Replace('_', ' ');
        return char.ToUpperInvariant(words[0]) + words[1..];
    }

    /// <summary>One result of a list request, read once so ranking does not touch the JSON again.</summary>
    protected sealed record Candidate(string Xid, string Name, string? Kinds, double Rate, string? Wikidata, GeoPoint Location, double DistanceKm)
    {
        public bool HasKind(string kind) => Kinds?.Split(',').Contains(kind, StringComparer.Ordinal) == true;

        internal static Candidate From(JsonElement e, GeoPoint center)
        {
            var point = e.Prop("point");
            var location = new GeoPoint(point.Prop("lat").Num() ?? 0, point.Prop("lon").Num() ?? 0);
            return new Candidate(
                e.Prop("xid").Str() ?? "",
                e.Prop("name").Str() ?? "",
                e.Prop("kinds").Str(),
                Popularity(e.Prop("rate")),
                e.Prop("wikidata").Str() is { Length: > 0 } id ? id : null,
                location,
                Distance(center, location));
        }

        /// <summary>Flat-earth distance in km; accurate enough inside one city.</summary>
        private static double Distance(GeoPoint a, GeoPoint b)
        {
            var x = (b.Lng - a.Lng) * Math.Cos((a.Lat + b.Lat) / 2 * Math.PI / 180);
            var y = b.Lat - a.Lat;
            return Math.Sqrt((x * x) + (y * y)) * KmPerDegree;
        }
    }
}
