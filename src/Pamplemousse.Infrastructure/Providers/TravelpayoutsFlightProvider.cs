using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Exceptions;
using Pamplemousse.Domain.Flights;
using Pamplemousse.Infrastructure.Caching;
using Pamplemousse.Infrastructure.ReferenceData;

namespace Pamplemousse.Infrastructure.Providers;

/// <summary>
/// Travelpayouts (Aviasales) Data API, ADR 0010: the cheapest fares other travellers found recently, not live prices.
/// Exact dates are often missing from this data, so a search falls back to nearby dates, then to other dates in the same months,
/// then, for round trips, to any return date.
/// </summary>
public sealed class TravelpayoutsFlightProvider(
    HttpClient http,
    JsonCache cache,
    IOptions<ExternalApiOptions> options,
    FlightCityStore cities,
    TimeProvider clock) : IFlightProvider
{
    private const string ApiUrl = "https://api.travelpayouts.com/aviasales/v3/prices_for_dates";
    private const string BookingSite = "https://www.aviasales.com";
    private const string SourceName = "Travelpayouts";
    private const int NearbyDays = 3;
    private const int PageSize = 100;

    public async Task<FlightResults> SearchAsync(FlightSearch search, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(search);
        var token = options.Value.TravelpayoutsApiToken is { Length: > 0 } t
            ? t
            : throw new ProviderNotConfiguredException(SourceName, "TRAVELPAYOUTS_API_TOKEN");

        var marker = options.Value.TravelpayoutsMarker;
        var key = $"flights:tp:{search.OriginCode}:{search.DestinationCode}:{search.Dates.From:yyyyMMdd}:{search.Dates.To:yyyyMMdd}:{search.TripType}:{search.DirectOnly}:{search.Currency}";

        // Identical searches reuse the answer for an hour: the data is itself a cache and the source asks partners to limit calls.
        var offers = await cache.GetOrCreateAsync<IReadOnlyList<FlightOffer>>(key, TimeSpan.FromHours(1), c => FindOffersAsync(search, token, marker, c), ct);

        IReadOnlyList<string> warnings = marker.Length > 0 ? [] : ["Booking links have no partner marker. Set TRAVELPAYOUTS_MARKER in the environment or .env file."];
        return new FlightResults(offers, WithMarker(BookingSite + SearchPath(search), marker), warnings);
    }

    private async Task<IReadOnlyList<FlightOffer>> FindOffersAsync(FlightSearch search, string token, string marker, CancellationToken ct)
    {
        var oneWay = search.TripType == TripType.OneWay;
        var from = search.Dates.From;
        var to = search.Dates.To;

        var exact = (await FetchAsync(search, Format(from, "yyyy-MM-dd"), oneWay ? null : Format(to, "yyyy-MM-dd"), token, ct))
            .Where(f => Departs(f) == from && (oneWay || Returns(f) == to))
            .ToList();
        if (exact.Count > 0)
        {
            return ToOffers(exact, search, DateMatch.Exact, marker);
        }

        // One call per month answers both fallback levels; a range near a month's edge needs the neighbouring month too.
        var pending = MonthsAround(from).SelectMany(d => oneWay
            ? [(Departure: d, Return: (string?)null)]
            : MonthsAround(to).Select(r => (Departure: d, Return: (string?)r)));
        var pages = await Task.WhenAll(pending.Select(m => FetchAsync(search, m.Departure, m.Return, token, ct)));

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var upcoming = pages.SelectMany(p => p).Where(f => Departs(f) >= today).ToList();

        var nearby = upcoming.Where(f => IsNear(Departs(f), from) && (oneWay || IsNear(Returns(f), to))).ToList();
        if (nearby.Count > 0)
        {
            return ToOffers(nearby, search, DateMatch.Nearby, marker);
        }

        var sameMonth = upcoming.Where(f => IsSameMonth(Departs(f), from) && (oneWay || IsSameMonth(Returns(f), to))).ToList();
        if (sameMonth.Count > 0 || oneWay)
        {
            return ToOffers(sameMonth, search, DateMatch.SameMonth, marker);
        }

        // Thin routes such as Beirut–Paris mostly have fares that return in another month.
        var anyReturn = (await FetchAsync(search, Format(from, "yyyy-MM"), returnDate: null, token, ct))
            .Where(f => f.ReturnAt is not null && Departs(f) >= today && IsSameMonth(Departs(f), from))
            .ToList();
        return ToOffers(anyReturn, search, DateMatch.AnyReturn, marker);
    }

    private async Task<List<Fare>> FetchAsync(FlightSearch search, string departure, string? returnDate, string token, CancellationToken ct)
    {
        var returnPart = returnDate is null ? "" : $"&return_at={returnDate}";

        // The currency is always sent: without it the source answers in rubles.
        var query = string.Create(
            CultureInfo.InvariantCulture,
            $"?origin={search.OriginCode}&destination={search.DestinationCode}&departure_at={departure}{returnPart}&one_way={Flag(search.TripType == TripType.OneWay)}&direct={Flag(search.DirectOnly)}&currency={search.Currency}&sorting=price&unique=false&limit={PageSize}");

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(ApiUrl + query));
        request.Headers.Add("X-Access-Token", token); // a header, so the token never appears in a logged URL

        using var response = await http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (response.StatusCode == HttpStatusCode.BadRequest && body.Contains("currency", StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException($"Currency {search.Currency} is not supported.", "currency");
        }

        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(body);
        return ((JsonElement?)doc.RootElement).Prop("data").Items().Select(ToFare).OfType<Fare>().ToList();
    }

    /// <summary>A fare the traveller can use: it has a real price and a parsable departure. Anything else is left out.</summary>
    private static Fare? ToFare(JsonElement e)
    {
        JsonElement? fare = e;
        if (fare.Prop("price").Num() is not > 0 || !TryParseTime(fare.Prop("departure_at").Str(), out var departure))
        {
            return null;
        }

        var number = fare.Prop("flight_number") is { ValueKind: JsonValueKind.Number } n ? n.GetRawText() : fare.Prop("flight_number").Str();
        return new Fare(
            fare.Prop("airline").Str() ?? "",
            number,
            fare.Prop("origin_airport").Str(),
            fare.Prop("destination_airport").Str(),
            departure,
            TryParseTime(fare.Prop("return_at").Str(), out var back) ? back : null,
            fare.Prop("transfers").Int() ?? 0,
            fare.Prop("return_transfers").Int(),
            fare.Prop("duration").Int(),
            (decimal)fare.Prop("price").Num()!.Value,
            fare.Prop("link").Str());
    }

    private List<FlightOffer> ToOffers(List<Fare> fares, FlightSearch search, DateMatch match, string marker)
    {
        var offers = fares
            .Where(f => !search.DirectOnly || (f.Stops == 0 && (f.ReturnStops ?? 0) == 0))
            .DistinctBy(f => (f.Airline, f.FlightNumber, f.DepartureAt, f.ReturnAt))
            .OrderBy(f => f.Price)
            .Select(f => ToOffer(f, search, match, marker));
        return [.. offers];
    }

    private FlightOffer ToOffer(Fare f, FlightSearch search, DateMatch match, string marker)
    {
        var oneWay = search.TripType == TripType.OneWay;
        return new FlightOffer(
            $"tp:{FareId(f)}",
            cities.AirlineName(f.Airline),
            f.FlightNumber is { Length: > 0 } number ? f.Airline + number : null,
            f.OriginAirport ?? search.OriginCode,
            f.DestinationAirport ?? search.DestinationCode,
            f.DepartureAt,
            oneWay ? null : f.ReturnAt,
            f.Stops,
            oneWay ? null : f.ReturnStops,
            f.Duration,
            new Money(f.Price, search.Currency),
            WithMarker(BookingSite + (f.Link is { Length: > 0 } link ? link : SearchPath(search)), marker),
            match,
            SourceName);
    }

    private static string FareId(Fare f)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{f.Airline}|{f.FlightNumber}|{f.DepartureAt:O}|{f.ReturnAt:O}")));
        return Convert.ToHexStringLower(hash)[..12];
    }

    /// <summary>The booking site's search address, e.g. /search/PAR1612ROM20121: dates as ddMM, then the number of passengers.</summary>
    private static string SearchPath(FlightSearch s)
    {
        var back = s.TripType == TripType.OneWay ? "" : Format(s.Dates.To, "ddMM");
        return $"/search/{s.OriginCode}{Format(s.Dates.From, "ddMM")}{s.DestinationCode}{back}1";
    }

    private static string WithMarker(string url, string marker) =>
        marker.Length == 0 ? url : $"{url}{(url.Contains('?', StringComparison.Ordinal) ? '&' : '?')}marker={Uri.EscapeDataString(marker)}";

    private static IEnumerable<string> MonthsAround(DateOnly date) =>
        new[] { date.AddDays(-NearbyDays), date, date.AddDays(NearbyDays) }.Select(d => Format(d, "yyyy-MM")).Distinct(StringComparer.Ordinal);

    private static bool IsNear(DateOnly? date, DateOnly target) => date is { } d && Math.Abs(d.DayNumber - target.DayNumber) <= NearbyDays;

    private static bool IsSameMonth(DateOnly? date, DateOnly target) => date is { } d && d.Year == target.Year && d.Month == target.Month;

    /// <summary>The local date: a fare's times carry their own offset.</summary>
    private static DateOnly Departs(Fare f) => DateOnly.FromDateTime(f.DepartureAt.DateTime);

    private static DateOnly? Returns(Fare f) => f.ReturnAt is { } back ? DateOnly.FromDateTime(back.DateTime) : null;

    private static string Format(DateOnly date, string format) => date.ToString(format, CultureInfo.InvariantCulture);

    private static string Flag(bool value) => value ? "true" : "false";

    private static bool TryParseTime(string? text, out DateTimeOffset time) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    private sealed record Fare(
        string Airline,
        string? FlightNumber,
        string? OriginAirport,
        string? DestinationAirport,
        DateTimeOffset DepartureAt,
        DateTimeOffset? ReturnAt,
        int Stops,
        int? ReturnStops,
        int? Duration,
        decimal Price,
        string? Link);
}
