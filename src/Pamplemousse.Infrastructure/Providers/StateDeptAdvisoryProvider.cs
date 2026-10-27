using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Pamplemousse.Domain.Abstractions;
using Pamplemousse.Domain.Destinations;
using Pamplemousse.Infrastructure.Caching;
using Pamplemousse.Infrastructure.ReferenceData;

namespace Pamplemousse.Infrastructure.Providers;

/// <summary>US State Department travel advisories (free, no key). Titles look like "Italy - Level 2: Exercise Increased Caution".</summary>
public sealed partial class StateDeptAdvisoryProvider(HttpClient http, JsonCache cache) : IAdvisoryProvider
{
    private const int SummaryLength = 280;

    public async Task<TravelAdvisory?> GetAsync(Country country, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(country);

        var all = await cache.GetOrCreateAsync("advisories:us", TimeSpan.FromHours(12), FetchAllAsync, ct);
        var names = new[] { country.Name, country.OfficialName }.Select(ReferenceDataStore.Normalize).ToHashSet(StringComparer.Ordinal);
        return all.FirstOrDefault(a => names.Contains(ReferenceDataStore.Normalize(a.CountryName)))?.Advisory;
    }

    private async Task<List<CountryAdvisory>> FetchAllAsync(CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(await http.GetStringAsync(new Uri("https://cadataapi.state.gov/api/TravelAdvisories"), ct));
        var result = new List<CountryAdvisory>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var title = ((JsonElement?)item).Prop("Title").Str() ?? "";
            var match = TitlePattern().Match(title);
            if (!match.Success)
            {
                continue;
            }

            result.Add(new CountryAdvisory(
                match.Groups["country"].Value.Trim(),
                new TravelAdvisory(
                    int.Parse(match.Groups["level"].Value, CultureInfo.InvariantCulture),
                    match.Groups["label"].Value.Trim(),
                    Summarize(((JsonElement?)item).Prop("Summary").Str() ?? ""),
                    ((JsonElement?)item).Prop("Link").Str() ?? "https://travel.state.gov")));
        }

        return result;
    }

    private static string Summarize(string html)
    {
        var text = WhitespacePattern().Replace(WebUtility.HtmlDecode(TagPattern().Replace(html, " ")), " ").Trim();
        text = SpaceBeforePunctuationPattern().Replace(text, "${punct}"); // "terrorism ." → "terrorism."
        return text.Length <= SummaryLength ? text : string.Concat(text.AsSpan(0, SummaryLength).TrimEnd(), "…");
    }

    public sealed record CountryAdvisory(string CountryName, TravelAdvisory Advisory);

    // Every pattern has a timeout so a malformed response cannot hang a request (ReDoS).
    [GeneratedRegex(@"^(?<country>.+?)\s+-\s+Level\s+(?<level>[1-4]):\s*(?<label>.+)$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TitlePattern();

    [GeneratedRegex("<[^>]+>", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"\s+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex WhitespacePattern();

    [GeneratedRegex(@"\s+(?<punct>[.,;:!?])", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SpaceBeforePunctuationPattern();
}
