using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Destinations;
using Pamplemousse.Domain.Hotels;
using Pamplemousse.Infrastructure.Caching;

namespace Pamplemousse.Infrastructure.Providers;

/// <summary>
/// Hotels from the Booking COM API on RapidAPI (ADR 0016): live prices, the same as on Booking.com.
/// Every request counts toward the plan's monthly limit, so each one is counted first and refused at 95% (ADR 0017).
/// </summary>
public sealed class BookingComHotelProvider(
    HttpClient http,
    IOptions<ExternalApiOptions> options,
    IRequestQuota quota,
    TimeProvider clock) : IHotelProvider
{
    public const string QuotaSource = "bookingcom";

    private const string Host = "booking-com18.p.rapidapi.com";
    private const string BookingSite = "https://www.booking.com";
    private const double MaxCityDistanceKm = 100;
    private const int PageSize = 20;

    public string Source => "Booking.com";

    public async Task<CityMapping?> FindCityAsync(Destination destination, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(destination);
        using var doc = await GetAsync($"/stays/auto-complete?query={Uri.EscapeDataString(destination.Name)}", ct);

        // Names repeat across countries (Zahle also matches São Paulo hotels), so only cities in the same country count, nearest first.
        var city = ((JsonElement?)doc.RootElement).Prop("data").Items()
            .Select(e => (JsonElement?)e)
            .Where(e => string.Equals(e.Prop("dest_type").Str(), "city", StringComparison.Ordinal)
                && string.Equals(e.Prop("cc1").Str(), destination.CountryCode, StringComparison.OrdinalIgnoreCase)
                && e.Prop("id").Str() is { Length: > 0 }
                && e.Prop("latitude").Num() is not null
                && e.Prop("longitude").Num() is not null)
            .Select(e => (Entry: e, Km: destination.Location.DistanceKmTo(new GeoPoint(e.Prop("latitude").Num()!.Value, e.Prop("longitude").Num()!.Value))))
            .Where(c => c.Km <= MaxCityDistanceKm)
            .OrderBy(c => c.Km)
            .Select(c => c.Entry)
            .FirstOrDefault();
        if (city is null)
        {
            return null;
        }

        var ufi = long.TryParse(city.Prop("dest_id").Str(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : (long?)null;
        return new CityMapping(destination.Id, city.Prop("id").Str()!, ufi, city.Prop("name").Str() ?? destination.Name, clock.GetUtcNow());
    }

    public async Task<HotelResults> SearchAsync(CityMapping city, HotelSearch search, int page, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(city);
        ArgumentNullException.ThrowIfNull(search);
        var query = string.Create(
            CultureInfo.InvariantCulture,
            $"/stays/search?locationId={Uri.EscapeDataString(city.LocationId)}&checkinDate={search.Dates.From:yyyy-MM-dd}&checkoutDate={search.Dates.To:yyyy-MM-dd}&adults={search.Adults}&units=metric&temperature=c&currencyCode={search.Currency}&page={page}");
        using var doc = await GetAsync(query, ct);
        JsonElement? root = doc.RootElement;

        var now = clock.GetUtcNow();
        var found = root.Prop("data").Items()
            .Select((e, i) => ToFound(e, search, ((page - 1) * PageSize) + i + 1, now))
            .OfType<FoundHotel>()
            .DistinctBy(f => f.Hotel.Id, StringComparer.Ordinal)
            .ToList();
        return new HotelResults(found, root.Prop("meta").Prop("totalPage").Int() ?? page);
    }

    public async Task<Hotel?> GetDetailsAsync(string hotelId, HotelSearch search, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(search);
        if (!hotelId.StartsWith("bk:", StringComparison.Ordinal))
        {
            return null;
        }

        var query = string.Create(
            CultureInfo.InvariantCulture,
            $"/stays/detail?hotelId={Uri.EscapeDataString(hotelId[3..])}&checkinDate={search.Dates.From:yyyy-MM-dd}&checkoutDate={search.Dates.To:yyyy-MM-dd}&adults={search.Adults}&units=metric&currencyCode={search.Currency}");
        using var doc = await GetAsync(query, ct);
        var data = ((JsonElement?)doc.RootElement).Prop("data");
        return data is { ValueKind: JsonValueKind.Object } d ? ToDetails(hotelId, d, search.Adults, clock.GetUtcNow()) : null;
    }

    public string SearchUrl(Destination destination, HotelSearch search)
    {
        ArgumentNullException.ThrowIfNull(destination);
        return $"{BookingSite}/searchresults.html?ss={Uri.EscapeDataString(destination.Name)}&{StayQuery(search)}";
    }

    public string BookingUrl(Hotel hotel, HotelSearch search)
    {
        ArgumentNullException.ThrowIfNull(hotel);

        // Without the details call we only know the id; Booking.com's search can open a single hotel by it.
        return hotel.BookingPageUrl is { Length: > 0 } page
            ? $"{page}{(page.Contains('?', StringComparison.Ordinal) ? '&' : '?')}{StayQuery(search)}"
            : $"{BookingSite}/searchresults.html?dest_id={Uri.EscapeDataString(hotel.Id[(hotel.Id.IndexOf(':', StringComparison.Ordinal) + 1)..])}&dest_type=hotel&{StayQuery(search)}";
    }

    private static string StayQuery(HotelSearch search)
    {
        ArgumentNullException.ThrowIfNull(search);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"checkin={search.Dates.From:yyyy-MM-dd}&checkout={search.Dates.To:yyyy-MM-dd}&group_adults={search.Adults}&no_rooms=1&group_children=0&selected_currency={search.Currency}");
    }

    private async Task<JsonDocument> GetAsync(string pathAndQuery, CancellationToken ct)
    {
        var key = options.Value.RapidApiKey is { Length: > 0 } k ? k : throw new ProviderNotConfiguredException("Hotels", "RAPIDAPI_KEY");

        // Stop at 95% of the plan, keeping a margin for live checks and other features sharing the key.
        var limit = (long)Math.Floor(options.Value.RapidApiMonthlyLimit * 0.95);
        if (!await quota.TryTakeAsync(QuotaSource, limit, ct))
        {
            throw new QuotaExceededException("Booking COM API");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"https://{Host}{pathAndQuery}"));
        request.Headers.Add("x-rapidapi-key", key); // a header, so the key never appears in a logged URL
        request.Headers.Add("x-rapidapi-host", Host);

        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));

        // The source reports some failures inside a 200 answer.
        if (doc.RootElement.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.False)
        {
            var message = ((JsonElement?)doc.RootElement).Prop("message").Str();
            doc.Dispose();
            throw new HttpRequestException($"The hotel source refused the request: {message}");
        }

        return doc;
    }

    /// <summary>A hotel the traveller can use: it has a position and a real price. Anything else is left out.</summary>
    private static FoundHotel? ToFound(JsonElement e, HotelSearch search, int rank, DateTimeOffset now)
    {
        JsonElement? h = e;
        var id = h.Prop("id").Num();
        var lat = h.Prop("latitude").Num();
        var lng = h.Prop("longitude").Num();
        var name = h.Prop("name").Str();
        var breakdown = h.Prop("priceBreakdown");

        // The gross price leaves out some taxes and fees; adding the excluded part gives the total the booking site charges.
        var gross = breakdown.Prop("grossPrice").Prop("value").Num();
        var total = gross is null ? (decimal?)null : (decimal)(gross.Value + (breakdown.Prop("excludedPrice").Prop("value").Num() ?? 0));
        if (id is null || lat is null || lng is null || name is null || HotelPrice.ForStay(total, search, now) is not { } price)
        {
            return null;
        }

        var (stars, estimated) = Stars(h.Prop("propertyClass").Int(), h.Prop("qualityClass").Int());
        var reviews = h.Prop("reviewCount").Int() is { } count && count > 0 ? count : (int?)null;
        var hotel = new Hotel
        {
            Id = string.Create(CultureInfo.InvariantCulture, $"bk:{(long)id.Value}"),
            Name = name,
            Location = new GeoPoint(lat.Value, lng.Value),
            CityUfi = h.Prop("ufi").Int(),
            CountryCode = h.Prop("countryCode").Str()?.ToUpperInvariant(),
            Stars = stars,
            StarsEstimated = estimated,
            ReviewScore = reviews is null ? null : h.Prop("reviewScore").Num(),
            ReviewCount = reviews,
            ReviewWord = reviews is null ? null : h.Prop("reviewScoreWord").Str(),
            PhotoUrls = [.. h.Prop("photoUrls").Items().Select(p => ((JsonElement?)p).Str()).OfType<string>().Select(Larger)],
            CheckInFrom = Blank(h.Prop("checkin").Prop("fromTime").Str()),
            CheckInUntil = Blank(h.Prop("checkin").Prop("untilTime").Str()),
            CheckOutFrom = Blank(h.Prop("checkout").Prop("fromTime").Str()),
            CheckOutUntil = Blank(h.Prop("checkout").Prop("untilTime").Str()),
            LastPrice = price,
            RawSearch = e.GetRawText(),
            SearchUpdatedAt = now,
        };
        return new FoundHotel(hotel, price, rank);
    }

    private static Hotel? ToDetails(string hotelId, JsonElement data, int adults, DateTimeOffset now)
    {
        JsonElement? d = data;
        var lat = d.Prop("latitude").Num();
        var lng = d.Prop("longitude").Num();
        var name = d.Prop("hotel_name_trans").Str() ?? d.Prop("hotel_name").Str();
        if (lat is null || lng is null || name is null)
        {
            return null;
        }

        // Free cancellation only counts for a room that fits the travellers; with no such room it is unknown.
        var fitting = d.Prop("block").Items().Select(b => (JsonElement?)b).Where(b => Occupancy(b.Prop("max_occupancy")) >= adults).ToList();
        bool? freeCancellation = fitting.Count == 0 ? null : fitting.Any(b => Flag(b.Prop("refundable")) == true);

        var street = d.Prop("address").Str();
        var place = string.Join(' ', new[] { d.Prop("zip").Str(), d.Prop("city").Str() }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return new Hotel
        {
            Id = hotelId,
            Name = name,
            Location = new GeoPoint(lat.Value, lng.Value),
            StayType = Blank(d.Prop("accommodation_type_name").Str()),
            Address = string.IsNullOrWhiteSpace(street) ? Blank(place) : Blank(place) is null ? street : $"{street}, {place}",
            District = Blank(d.Prop("district").Str()),
            CityUfi = d.Prop("ufi").Int(),
            CountryCode = d.Prop("countrycode").Str()?.ToUpperInvariant(),
            TimeZone = Blank(d.Prop("timezone").Str()),
            DistanceToCentreKm = d.Prop("distance_to_cc").Num() is { } km ? Math.Round(km, 2) : null,
            IsCityCentre = Flag(d.Prop("is_city_center")),
            BreakfastScore = d.Prop("breakfast_review_score").Prop("rating").Num(),
            WifiScore = d.Prop("wifi_review_score").Prop("rating").Num(),
            BreakfastIncluded = Flag(d.Prop("hotel_include_breakfast")),
            FamilyFriendly = Flag(d.Prop("is_family_friendly")),
            FreeCancellation = freeCancellation,
            Facilities = [.. d.Prop("facilities_block").Prop("facilities").Items().Select(f => ((JsonElement?)f).Prop("name").Str()).OfType<string>()],
            Languages = [.. d.Prop("languages_spoken").Prop("languagecode").Items().Select(l => ((JsonElement?)l).Str()).OfType<string>()],
            BookingPageUrl = d.Prop("url").Str() is { } url && url.StartsWith(BookingSite + "/", StringComparison.Ordinal) ? url : null,
            RawDetails = data.GetRawText(),
            DetailsUpdatedAt = now,
        };
    }

    /// <summary>An official rating first; otherwise the source's own estimate, flagged as such. 0 means unrated.</summary>
    private static (int? Stars, bool? Estimated) Stars(int? official, int? estimate) => official switch
    {
        > 0 => (official, false),
        _ when estimate is > 0 => (estimate, true),
        _ => (null, null),
    };

    /// <summary>Search photos are 60 px squares; the same link serves 500 px.</summary>
    private static string Larger(string url) => url.Replace("/square60/", "/square500/", StringComparison.Ordinal);

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>The source sends some flags as 0/1 and others as true/false.</summary>
    private static bool? Flag(JsonElement? e) => e switch
    {
        { ValueKind: JsonValueKind.True } => true,
        { ValueKind: JsonValueKind.False } => false,
        { ValueKind: JsonValueKind.Number } n => n.GetDouble() != 0,
        _ => null,
    };

    /// <summary>A number, or a number written as text ("2").</summary>
    private static int Occupancy(JsonElement? e) =>
        e.Int() ?? (int.TryParse(e.Str(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0);
}
