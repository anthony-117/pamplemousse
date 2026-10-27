using System.Globalization;
using System.Net;
using System.Text.Json;
using Pamplemousse.Domain.Abstractions;
using Pamplemousse.Domain.Destinations;
using Pamplemousse.Infrastructure.Caching;

namespace Pamplemousse.Infrastructure.Providers;

/// <summary>Frankfurter (free, no key): European Central Bank reference rates, about 30 currencies.</summary>
public sealed class FrankfurterExchangeRateProvider(HttpClient http, JsonCache cache, TimeProvider clock) : IExchangeRateProvider
{
    private const string SourceName = "ECB via Frankfurter";

    public async Task<ExchangeRate?> GetAsync(string baseCurrency, string quoteCurrency, CancellationToken ct)
    {
        var from = baseCurrency.ToUpperInvariant();
        var to = quoteCurrency.ToUpperInvariant();
        if (from == to)
        {
            return new ExchangeRate(from, to, 1m, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime), SourceName);
        }

        return await cache.GetOrCreateAsync<ExchangeRate?>($"fx:{from}:{to}", TimeSpan.FromHours(12), async token =>
        {
            using var response = await http.GetAsync(new Uri($"https://api.frankfurter.dev/v1/latest?base={from}&symbols={to}"), token);
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.UnprocessableEntity or HttpStatusCode.BadRequest)
            {
                return null; // currency not published by the ECB (e.g. LBP)
            }

            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            var rate = doc.RootElement.Prop("rates").Prop(to);
            var date = doc.RootElement.Prop("date").Str();
            return rate is { ValueKind: JsonValueKind.Number } r && date is not null
                ? new ExchangeRate(from, to, Math.Round(r.GetDecimal(), 6), DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture), SourceName)
                : null;
        }, ct);
    }
}
