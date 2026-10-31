using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Pamplemousse.Domain.Abstractions;
using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Destinations;
using Pamplemousse.Domain.Events;
using Pamplemousse.Infrastructure.Caching;

namespace Pamplemousse.Infrastructure.Providers;

/// <summary>Ticketmaster Discovery API (free key, TICKETMASTER_API_KEY): concerts, sports and shows near a city.</summary>
public sealed class TicketmasterEventProvider(HttpClient http, JsonCache cache, IOptions<ExternalApiOptions> options) : IEventProvider
{
    private const int RadiusKm = 30;

    public Task<IReadOnlyList<TravelEvent>> SearchAsync(Destination destination, DateOnly firstDay, DateOnly lastDay, int limit, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var key = options.Value.TicketmasterApiKey is { Length: > 0 } k
            ? k
            : throw new ProviderNotConfiguredException("Ticketmaster", "TICKETMASTER_API_KEY");

        return cache.GetOrCreateAsync<IReadOnlyList<TravelEvent>>($"events:tm:{destination.Id}:{firstDay:yyyyMMdd}:{lastDay:yyyyMMdd}:{limit}", TimeSpan.FromHours(6), async token =>
        {
            var latlong = string.Create(CultureInfo.InvariantCulture, $"{destination.Location.Lat:F4},{destination.Location.Lng:F4}");
            var url = $"https://app.ticketmaster.com/discovery/v2/events.json?apikey={key}&latlong={latlong}&radius={RadiusKm}&unit=km" +
                      $"&startDateTime={firstDay:yyyy-MM-dd}T00:00:00Z&endDateTime={lastDay:yyyy-MM-dd}T23:59:59Z&sort=date,asc&size={limit}";
            using var doc = JsonDocument.Parse(await http.GetStringAsync(new Uri(url), token));
            return ((JsonElement?)doc.RootElement).Prop("_embedded").Prop("events").Items()
                .Select(e => (JsonElement?)e)
                .Where(e => e.Prop("dates").Prop("start").Prop("localDate").Str() is not null)
                .Select(ToEvent)
                // Ticketmaster sometimes lists one show twice (e.g. two ticket offers); separate dates or times stay.
                .DistinctBy(e => (e.Name, e.Date, e.Time, e.Venue))
                .ToList();
        }, ct);
    }

    private static TravelEvent ToEvent(JsonElement? e)
    {
        var start = e.Prop("dates").Prop("start");
        var price = e.Prop("priceRanges").First();
        var min = price.Prop("min").Num();
        var currency = price.Prop("currency").Str();
        var image = e.Prop("images").Items()
            .OrderByDescending(i => ((JsonElement?)i).Prop("width").Int() ?? 0)
            .Select(i => ((JsonElement?)i).Prop("url").Str())
            .FirstOrDefault();

        return new TravelEvent(
            $"tm:{e.Prop("id").Str()}",
            e.Prop("name").Str() ?? "",
            DateOnly.ParseExact(start.Prop("localDate").Str()!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            start.Prop("localTime").Str()?[..5],
            e.Prop("_embedded").Prop("venues").First().Prop("name").Str(),
            e.Prop("classifications").First().Prop("segment").Prop("name").Str(),
            min is > 0 && currency is { Length: 3 } ? new Money((decimal)min.Value, currency) : null,
            image,
            e.Prop("url").Str() ?? "https://www.ticketmaster.com",
            "Ticketmaster");
    }
}
