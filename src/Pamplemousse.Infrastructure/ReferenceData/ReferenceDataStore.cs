using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Pamplemousse.Domain.Abstractions;
using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Destinations;

namespace Pamplemousse.Infrastructure.ReferenceData;

/// <summary>
/// Cities, countries and visa rules loaded once from the data files embedded in this assembly.
/// Sources: GeoNames cities15000 (CC BY 4.0), mledoze/countries (ODbL), Passport Index dataset (MIT).
/// </summary>
public sealed class ReferenceDataStore : IReferenceData
{
    private readonly Lazy<CityIndex> _cities = new(LoadCities);
    private readonly Lazy<Dictionary<string, Country>> _countries = new(LoadCountries);
    private readonly Lazy<Dictionary<(string, string), string>> _visas = new(LoadVisas);

    public IReadOnlyList<Country> Countries => [.. _countries.Value.Values.OrderBy(c => c.Name, StringComparer.Ordinal)];

    public Country? FindCountry(string code) =>
        _countries.Value.GetValueOrDefault(code.ToUpperInvariant());

    public Destination? FindDestination(string id) =>
        _cities.Value.ById.GetValueOrDefault(id);

    /// <summary>Exact name first, then names starting with the query, then names containing it; bigger cities first.</summary>
    public IReadOnlyList<Destination> SearchDestinations(string query, int limit)
    {
        var q = Normalize(query);
        if (q.Length == 0 || limit <= 0)
        {
            return [];
        }

        return _cities.Value.Entries
            .Select(e => (e.City, Rank: string.Equals(e.Key, q, StringComparison.Ordinal) ? 0 : e.Key.StartsWith(q, StringComparison.Ordinal) ? 1 : e.Key.Contains(q, StringComparison.Ordinal) ? 2 : 3))
            .Where(x => x.Rank < 3)
            .GroupBy(x => x.City.Id, StringComparer.Ordinal)
            .Select(g => g.MinBy(x => x.Rank))
            .OrderBy(x => x.Rank)
            .ThenByDescending(x => x.City.Population)
            .Take(limit)
            .Select(x => x.City)
            .ToList();
    }

    public VisaRequirement? FindVisa(string passportCountry, string destinationCountry)
    {
        var passport = passportCountry.ToUpperInvariant();
        var destination = destinationCountry.ToUpperInvariant();
        if (string.Equals(passport, destination, StringComparison.Ordinal))
        {
            return new VisaRequirement(passport, destination, "own country", null);
        }

        if (!_visas.Value.TryGetValue((passport, destination), out var raw))
        {
            return null;
        }

        return int.TryParse(raw, CultureInfo.InvariantCulture, out var days)
            ? new VisaRequirement(passport, destination, days < 0 ? "own country" : "visa free", days < 0 ? null : days)
            : new VisaRequirement(passport, destination, raw, null);
    }

    /// <summary>Lower-case, accents removed ("São Paulo" → "sao paulo") so searches match either spelling.</summary>
    internal static string Normalize(string value)
    {
        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static StreamReader OpenResource(string name)
    {
        var stream = typeof(ReferenceDataStore).Assembly.GetManifestResourceStream($"ReferenceData.{name}")
            ?? throw new InvalidOperationException($"Embedded reference data '{name}' is missing.");
        return new StreamReader(new GZipStream(stream, CompressionMode.Decompress), Encoding.UTF8);
    }

    private sealed record CityIndex(IReadOnlyList<(string Key, Destination City)> Entries, Dictionary<string, Destination> ById);

    private static CityIndex LoadCities()
    {
        var entries = new List<(string, Destination)>(40_000);
        var byId = new Dictionary<string, Destination>(40_000, StringComparer.Ordinal);
        using var reader = OpenResource("cities.tsv.gz");
        while (reader.ReadLine() is { } line)
        {
            // id, name, ascii name, country, region, lat, lng, population, time zone
            var c = line.Split('\t');
            var city = new Destination(
                c[0], c[1], c[4], c[3],
                new GeoPoint(double.Parse(c[5], CultureInfo.InvariantCulture), double.Parse(c[6], CultureInfo.InvariantCulture)),
                long.Parse(c[7], CultureInfo.InvariantCulture),
                c[8]);
            byId[city.Id] = city;
            entries.Add((Normalize(c[1]), city));
            var ascii = Normalize(c[2]);
            if (!string.Equals(ascii, entries[^1].Item1, StringComparison.Ordinal))
            {
                entries.Add((ascii, city));
            }
        }

        // A city can appear under two keys (native + ASCII name); SearchDestinations keeps its best match only.
        return new CityIndex(entries, byId);
    }

    private static Dictionary<string, Country> LoadCountries()
    {
        using var reader = OpenResource("countries.json.gz");
        var items = JsonSerializer.Deserialize<List<Country>>(reader.ReadToEnd(), JsonSerializerOptions.Web) ?? [];
        return items.ToDictionary(c => c.Code, StringComparer.Ordinal);
    }

    private static Dictionary<(string, string), string> LoadVisas()
    {
        var map = new Dictionary<(string, string), string>(40_000);
        using var reader = OpenResource("visas.csv.gz");
        while (reader.ReadLine() is { } line)
        {
            var c = line.Split(',');
            if (c.Length == 3)
            {
                map[(c[0], c[1])] = c[2];
            }
        }

        return map;
    }
}
