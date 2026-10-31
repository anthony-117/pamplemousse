using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Pamplemousse.Domain.Abstractions;
using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Destinations;
using Pamplemousse.Domain.Places;
using Pamplemousse.Infrastructure.Caching;

namespace Pamplemousse.Infrastructure.Providers;

/// <summary>
/// Attractions from Wikidata (free, no key), ranked by fame: how many Wikipedia editions have an article about the place.
/// That puts the Colosseum, St Peter's and the Trevi Fountain first, where OpenTripMap only knows "popular or not".
/// Places Wikidata cannot provide (query down or slow, small towns) are filled in from OpenTripMap.
/// Descriptions and photos for the details page come from the English Wikipedia summary.
/// </summary>
public sealed partial class WikidataAttractionProvider(HttpClient http, JsonCache cache, OpenTripMapAttractionProvider fallback) : IPlaceProvider
{
    private const string QueryUrl = "https://query.wikidata.org/sparql?format=json&query=";
    private const int RadiusKm = 10;

    /// <summary>Places with fewer Wikipedia articles are rarely worth a tourist's time.</summary>
    private const int MinArticles = 15;

    /// <summary>How many famous places to look at; most are not attractions (languages, institutions, events).</summary>
    private const int Candidates = 150;

    /// <summary>
    /// A place is an attraction when one of its Wikidata type names contains one of these words ("Roman amphitheatre",
    /// "minor basilica", "art museum"). Checking the type hierarchy on Wikidata's side is exact but takes 10–60 s per
    /// city, so the check is done here on the names.
    /// </summary>
    private static readonly HashSet<string> AttractionWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "museum", "gallery", "church", "basilica", "cathedral", "chapel", "abbey", "monastery", "mosque", "temple", "synagogue",
        "shrine", "palace", "castle", "fortress", "fort", "citadel", "tower", "fountain", "square", "plaza", "piazza", "park",
        "garden", "monument", "memorial", "mausoleum", "arch", "column", "obelisk", "bridge", "amphitheatre", "theatre",
        // "site" covers archaeological and historic sites without letting in "archaeological artefact" (a statue in a museum).
        "opera", "cemetery", "site", "ruins", "forum", "gate", "wall", "hill", "landmark", "boulevard", "avenue",
        "market", "zoo", "aquarium", "observatory", "lighthouse", "souk", "bazaar", "beach", "waterfall", "viewpoint",
        "palazzo", "villa", "residence", "circus", "bath", "catacomb", "road",
    };

    public string Source => "wd";

    public PlaceCategory Category => PlaceCategory.Attraction;

    public async Task<IReadOnlyList<Place>> SearchAsync(Destination destination, int limit, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(destination);

        IReadOnlyList<Place> famous;
        try
        {
            // Landmarks do not change week to week.
            famous = await cache.GetOrCreateAsync($"places:wd:{destination.Id}", TimeSpan.FromDays(7), token => FamousPlacesAsync(destination, token), ct);
        }
        catch (Exception e) when (IsUnavailable(e, ct))
        {
            famous = [];
        }

        if (famous.Count >= limit)
        {
            return famous.Take(limit).ToList();
        }

        IReadOnlyList<Place> others;
        try
        {
            others = await fallback.SearchAsync(destination, limit, ct);
        }
        catch (ProviderNotConfiguredException) when (famous.Count > 0)
        {
            return famous;
        }

        var names = famous.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return famous.Concat(others.Where(p => !names.Contains(p.Name))).Take(limit).ToList();
    }

    public Task<Place?> GetAsync(string providerId, CancellationToken ct)
    {
        // The id is pasted into the query, so only accept real Wikidata ids.
        if (!WikidataId().IsMatch(providerId))
        {
            return Task.FromResult<Place?>(null);
        }

        return cache.GetOrCreateAsync<Place?>($"place:wd:{providerId}", TimeSpan.FromDays(7), async token =>
        {
            var query = $$"""
                SELECT ?label ?coord ?typeLabel ?website ?image ?article WHERE {
                  wd:{{providerId}} rdfs:label ?label. FILTER(LANG(?label) = "en")
                  OPTIONAL { wd:{{providerId}} wdt:P625 ?coord }
                  OPTIONAL { wd:{{providerId}} wdt:P18 ?image }
                  OPTIONAL { wd:{{providerId}} wdt:P31 ?type. ?type rdfs:label ?typeLabel. FILTER(LANG(?typeLabel) = "en") }
                  OPTIONAL { wd:{{providerId}} wdt:P856 ?website }
                  OPTIONAL { ?article schema:about wd:{{providerId}}; schema:isPartOf <https://en.wikipedia.org/> }
                }
                """;
            var rows = await QueryAsync(query, token);
            if (rows.Count == 0)
            {
                return null;
            }

            var article = rows.Select(r => r.Prop("article").Prop("value").Str()).FirstOrDefault(a => a is not null);
            var (description, thumbnail) = article is null ? (null, null) : await WikipediaSummaryAsync(article, token);

            // Wikidata's main image is a photo of the place; the Wikipedia thumbnail is sometimes a logo (the Eiffel Tower's is).
            var photo = rows.Select(r => r.Prop("image").Prop("value").Str()).FirstOrDefault(i => i is not null);
            var image = photo is null ? thumbnail : photo + "?width=800";
            return new Place(
                $"wd:{providerId}",
                rows[0].Prop("label").Prop("value").Str() ?? "",
                PlaceCategory.Attraction,
                rows.Select(r => ParsePoint(r.Prop("coord").Prop("value").Str())).FirstOrDefault(p => p is not null) ?? default,
                KindOf(rows.Select(r => r.Prop("typeLabel").Prop("value").Str())),
                Rating: null,
                RatingCount: null,
                PriceLevel: null,
                Address: null,
                description,
                image,
                rows.Select(r => r.Prop("website").Prop("value").Str()).FirstOrDefault(w => w is not null) ?? article,
                OpeningHours: [],
                "Wikidata");
        }, ct);
    }

    private async Task<IReadOnlyList<Place>> FamousPlacesAsync(Destination destination, CancellationToken ct)
    {
        var center = string.Create(CultureInfo.InvariantCulture, $"Point({destination.Location.Lng} {destination.Location.Lat})");
        var query = $$"""
            SELECT ?item ?articles ?coord ?label ?typeLabel ?population ?demolished WHERE {
              { SELECT ?item ?articles ?coord WHERE {
                  SERVICE wikibase:around { ?item wdt:P625 ?coord. bd:serviceParam wikibase:center "{{center}}"^^geo:wktLiteral; wikibase:radius "{{RadiusKm}}". }
                  ?item wikibase:sitelinks ?articles. FILTER(?articles >= {{MinArticles}})
                } ORDER BY DESC(?articles) LIMIT {{Candidates}} }
              OPTIONAL { ?item rdfs:label ?label. FILTER(LANG(?label) = "en") }
              OPTIONAL { ?item wdt:P31 ?type. ?type rdfs:label ?typeLabel. FILTER(LANG(?typeLabel) = "en") }
              OPTIONAL { ?item wdt:P1082 ?population }
              OPTIONAL { ?item wdt:P576 ?demolished }
            }
            """;
        var rows = await QueryAsync(query, ct);

        // One row per (place, type); group them back into places.
        return rows
            .GroupBy(IdOf, StringComparer.Ordinal)
            .Where(g => g.Key.Length > 0)
            // The English name is optional in the query because requiring it there makes it several times slower.
            .Where(g => g.First().Prop("label").Prop("value").Str() is { Length: > 0 })
            // Towns and districts have a population; demolished buildings cannot be visited.
            .Where(g => g.All(r => r.Prop("population") is null && r.Prop("demolished") is null))
            .Select(g => (Id: g.Key, Row: g.First(), Kind: KindOf(g.Select(r => r.Prop("typeLabel").Prop("value").Str()))))
            .Where(p => p.Kind is not null)
            .OrderByDescending(p => p.Row.Prop("articles").Prop("value").Str() is { } n ? int.Parse(n, CultureInfo.InvariantCulture) : 0)
            .Select(p => new Place(
                $"wd:{p.Id}",
                p.Row.Prop("label").Prop("value").Str() ?? "",
                PlaceCategory.Attraction,
                ParsePoint(p.Row.Prop("coord").Prop("value").Str()) ?? default,
                p.Kind,
                Rating: null,
                RatingCount: null,
                PriceLevel: null,
                Address: null,
                Description: null,
                ImageUrl: null,
                Website: null,
                OpeningHours: [],
                "Wikidata"))
            .ToList();
    }

    private async Task<List<JsonElement?>> QueryAsync(string sparql, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(await http.GetStringAsync(new Uri(QueryUrl + Uri.EscapeDataString(sparql)), ct));
        return ((JsonElement?)doc.RootElement).Prop("results").Prop("bindings").Items()
            .Select(e => (JsonElement?)e.Clone())
            .ToList();
    }

    private async Task<(string? Description, string? Image)> WikipediaSummaryAsync(string articleUrl, CancellationToken ct)
    {
        var title = articleUrl[(articleUrl.LastIndexOf('/') + 1)..];
        using var response = await http.GetAsync(new Uri($"https://en.wikipedia.org/api/rest_v1/page/summary/{title}"), ct);
        if (!response.IsSuccessStatusCode)
        {
            return (null, null);
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        JsonElement? summary = doc.RootElement;
        return (summary.Prop("extract").Str(), summary.Prop("thumbnail").Prop("source").Str() ?? summary.Prop("originalimage").Prop("source").Str());
    }

    /// <summary>The first type that names an attraction, capitalised ("roman amphitheatre" → "Roman amphitheatre"); null if none does.</summary>
    private static string? KindOf(IEnumerable<string?> typeLabels)
    {
        // Whole words, singular or plural: "arch" must not match "archives".
        var kind = typeLabels.FirstOrDefault(t => t is not null && t
            .Split([' ', '-'], StringSplitOptions.RemoveEmptyEntries)
            .Any(w => AttractionWords.Contains(w) || AttractionWords.Contains(w.TrimEnd('s'))));
        return kind is null ? null : char.ToUpperInvariant(kind[0]) + kind[1..];
    }

    /// <summary>"http://www.wikidata.org/entity/Q10285" → "Q10285".</summary>
    private static string IdOf(JsonElement? row)
    {
        var uri = row.Prop("item").Prop("value").Str() ?? "";
        return uri[(uri.LastIndexOf('/') + 1)..];
    }

    /// <summary>"Point(12.4922 41.8902)" (longitude first) → GeoPoint.</summary>
    private static GeoPoint? ParsePoint(string? wkt)
    {
        var match = wkt is null ? null : WktPoint().Match(wkt);
        return match is { Success: true }
            ? new GeoPoint(double.Parse(match.Groups["lat"].Value, CultureInfo.InvariantCulture), double.Parse(match.Groups["lng"].Value, CultureInfo.InvariantCulture))
            : null;
    }

    /// <summary>Wikidata down, slow (the client times out) or answering something unexpected; the caller's own cancellation still propagates.</summary>
    private static bool IsUnavailable(Exception e, CancellationToken ct) =>
        e is HttpRequestException or JsonException || (e is TaskCanceledException && !ct.IsCancellationRequested);

    [GeneratedRegex(@"^Q\d{1,12}$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex WikidataId();

    [GeneratedRegex(@"^Point\((?<lng>-?[\d.]+) (?<lat>-?[\d.]+)\)$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex WktPoint();
}
