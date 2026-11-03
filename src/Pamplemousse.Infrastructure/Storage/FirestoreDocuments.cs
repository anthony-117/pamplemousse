using System.Globalization;
using Google.Cloud.Firestore;
using Pamplemousse.Domain.Hotels;
using DomainGeoPoint = Pamplemousse.Domain.Common.GeoPoint;
using FirestoreGeoPoint = Google.Cloud.Firestore.GeoPoint;

namespace Pamplemousse.Infrastructure.Storage;

/// <summary>
/// Converts Domain types to and from Firestore documents (data-model.md). Field names are written out here,
/// so Domain types carry no storage attributes. Firestore has no decimal, so amounts are stored as doubles
/// and read back rounded to cents.
/// </summary>
internal static class FirestoreDocuments
{
    /// <summary>Firestore ids can't contain '/', and the agreed form is bk_558347.</summary>
    public static string DocumentId(string hotelId) => hotelId.Replace(':', '_');

    public static string HotelId(string documentId)
    {
        var separator = documentId.IndexOf('_', StringComparison.Ordinal);
        return separator < 0 ? documentId : string.Concat(documentId.AsSpan(0, separator), ":", documentId.AsSpan(separator + 1));
    }

    public static Dictionary<string, object?> FromCity(CityMapping c) => new(StringComparer.Ordinal)
    {
        ["locationId"] = c.LocationId,
        ["ufi"] = c.Ufi,
        ["sourceName"] = c.SourceName,
        ["matchedAt"] = Timestamp.FromDateTimeOffset(c.MatchedAt),
    };

    public static CityMapping ToCity(string destinationId, IDictionary<string, object> d) => new(
        destinationId,
        Str(d, "locationId") ?? "",
        Long(d, "ufi"),
        Str(d, "sourceName") ?? "",
        Time(d, "matchedAt") ?? DateTimeOffset.MinValue);

    public static Dictionary<string, object?> FromHotel(Hotel h) => new(StringComparer.Ordinal)
    {
        ["sourceId"] = h.Id[(h.Id.IndexOf(':', StringComparison.Ordinal) + 1)..],
        ["name"] = h.Name,
        ["stayType"] = h.StayType,
        ["address"] = h.Address,
        ["district"] = h.District,
        ["cityUfi"] = h.CityUfi,
        ["countryCode"] = h.CountryCode,
        ["timezone"] = h.TimeZone,
        ["location"] = new FirestoreGeoPoint(h.Location.Lat, h.Location.Lng),
        ["distanceToCentreKm"] = h.DistanceToCentreKm,
        ["isCityCentre"] = h.IsCityCentre,
        ["stars"] = h.Stars,
        ["starsEstimated"] = h.StarsEstimated,
        ["reviewScore"] = h.ReviewScore,
        ["reviewCount"] = h.ReviewCount,
        ["reviewWord"] = h.ReviewWord,
        ["breakfastScore"] = h.BreakfastScore,
        ["wifiScore"] = h.WifiScore,
        ["photoUrls"] = h.PhotoUrls.ToList(),
        ["checkInFrom"] = h.CheckInFrom,
        ["checkInUntil"] = h.CheckInUntil,
        ["checkOutFrom"] = h.CheckOutFrom,
        ["checkOutUntil"] = h.CheckOutUntil,
        ["breakfastIncluded"] = h.BreakfastIncluded,
        ["familyFriendly"] = h.FamilyFriendly,
        ["freeCancellation"] = h.FreeCancellation,
        ["facilities"] = h.Facilities.ToList(),
        ["languages"] = h.Languages.ToList(),
        ["bookingPageUrl"] = h.BookingPageUrl,
        ["lastPrice"] = h.LastPrice is { } p ? FromPrice(p) : null,
        ["rawSearch"] = h.RawSearch,
        ["rawDetails"] = h.RawDetails,
        ["searchUpdatedAt"] = Stamp(h.SearchUpdatedAt),
        ["detailsUpdatedAt"] = Stamp(h.DetailsUpdatedAt),
    };

    public static Hotel ToHotel(string documentId, IDictionary<string, object> d)
    {
        var location = d.TryGetValue("location", out var l) && l is FirestoreGeoPoint g ? new DomainGeoPoint(g.Latitude, g.Longitude) : default;
        return new Hotel
        {
            Id = HotelId(documentId),
            Name = Str(d, "name") ?? "",
            Location = location,
            StayType = Str(d, "stayType"),
            Address = Str(d, "address"),
            District = Str(d, "district"),
            CityUfi = Long(d, "cityUfi"),
            CountryCode = Str(d, "countryCode"),
            TimeZone = Str(d, "timezone"),
            DistanceToCentreKm = Double(d, "distanceToCentreKm"),
            IsCityCentre = Bool(d, "isCityCentre"),
            Stars = (int?)Long(d, "stars"),
            StarsEstimated = Bool(d, "starsEstimated"),
            ReviewScore = Double(d, "reviewScore"),
            ReviewCount = (int?)Long(d, "reviewCount"),
            ReviewWord = Str(d, "reviewWord"),
            BreakfastScore = Double(d, "breakfastScore"),
            WifiScore = Double(d, "wifiScore"),
            PhotoUrls = Strings(d, "photoUrls"),
            CheckInFrom = Str(d, "checkInFrom"),
            CheckInUntil = Str(d, "checkInUntil"),
            CheckOutFrom = Str(d, "checkOutFrom"),
            CheckOutUntil = Str(d, "checkOutUntil"),
            BreakfastIncluded = Bool(d, "breakfastIncluded"),
            FamilyFriendly = Bool(d, "familyFriendly"),
            FreeCancellation = Bool(d, "freeCancellation"),
            Facilities = Strings(d, "facilities"),
            Languages = Strings(d, "languages"),
            BookingPageUrl = Str(d, "bookingPageUrl"),
            LastPrice = d.TryGetValue("lastPrice", out var p) && p is IDictionary<string, object> price ? ToPrice(price) : null,
            RawSearch = Str(d, "rawSearch"),
            RawDetails = Str(d, "rawDetails"),
            SearchUpdatedAt = Time(d, "searchUpdatedAt"),
            DetailsUpdatedAt = Time(d, "detailsUpdatedAt"),
        };
    }

    public static Dictionary<string, object?> FromSnapshot(PriceSnapshot s) => new(StringComparer.Ordinal)
    {
        ["searchKey"] = s.SearchKey,
        ["destinationId"] = s.DestinationId,
        ["checkIn"] = Date(s.CheckIn),
        ["checkOut"] = Date(s.CheckOut),
        ["adults"] = s.Adults,
        ["currency"] = s.Currency,
        ["fetchedAt"] = Timestamp.FromDateTimeOffset(s.FetchedAt),
        ["page"] = s.Page,
        ["prices"] = s.Prices.Select(p => new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["hotelId"] = p.HotelId,
            ["total"] = (double)p.Total,
            ["perNight"] = (double)p.PerNight,
            ["rank"] = p.Rank,
            ["freeCancellation"] = p.FreeCancellation,
        }).ToList(),
    };

    public static PriceSnapshot ToSnapshot(IDictionary<string, object> d) => new(
        Str(d, "searchKey") ?? "",
        Str(d, "destinationId") ?? "",
        ParseDate(Str(d, "checkIn")),
        ParseDate(Str(d, "checkOut")),
        (int)(Long(d, "adults") ?? 0),
        Str(d, "currency") ?? "",
        Time(d, "fetchedAt") ?? DateTimeOffset.MinValue,
        (int)(Long(d, "page") ?? 1),
        [.. (d.TryGetValue("prices", out var list) && list is IEnumerable<object> items ? items : [])
            .OfType<IDictionary<string, object>>()
            .Select(p => new SnapshotPrice(Str(p, "hotelId") ?? "", Money(p, "total"), Money(p, "perNight"), (int)(Long(p, "rank") ?? 0), Bool(p, "freeCancellation")))]);

    /// <summary>One price for one hotel, in <c>hotels/{id}/prices/{fetchedAt}</c>.</summary>
    public static Dictionary<string, object?> FromHistory(PriceSnapshot s, SnapshotPrice p) => new(StringComparer.Ordinal)
    {
        ["searchKey"] = s.SearchKey,
        ["checkIn"] = Date(s.CheckIn),
        ["checkOut"] = Date(s.CheckOut),
        ["adults"] = s.Adults,
        ["currency"] = s.Currency,
        ["total"] = (double)p.Total,
        ["perNight"] = (double)p.PerNight,
        ["rank"] = p.Rank,
        ["fetchedAt"] = Timestamp.FromDateTimeOffset(s.FetchedAt),
    };

    /// <summary>Sortable and unique per call: 20270410T091200123Z.</summary>
    public static string HistoryId(DateTimeOffset fetchedAt) => fetchedAt.UtcDateTime.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture);

    private static Dictionary<string, object?> FromPrice(HotelPrice p) => new(StringComparer.Ordinal)
    {
        ["total"] = (double)p.Total,
        ["perNight"] = (double)p.PerNight,
        ["currency"] = p.Currency,
        ["checkIn"] = Date(p.CheckIn),
        ["checkOut"] = Date(p.CheckOut),
        ["adults"] = p.Adults,
        ["fetchedAt"] = Timestamp.FromDateTimeOffset(p.FetchedAt),
    };

    private static HotelPrice ToPrice(IDictionary<string, object> d) => new(
        Money(d, "total"),
        Money(d, "perNight"),
        Str(d, "currency") ?? "",
        ParseDate(Str(d, "checkIn")),
        ParseDate(Str(d, "checkOut")),
        (int)(Long(d, "adults") ?? 0),
        Time(d, "fetchedAt") ?? DateTimeOffset.MinValue);

    private static Timestamp? Stamp(DateTimeOffset? time) => time is { } t ? Timestamp.FromDateTimeOffset(t) : null;

    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateOnly ParseDate(string? text) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : default;

    private static string? Str(IDictionary<string, object> d, string key) => d.TryGetValue(key, out var v) ? v as string : null;

    private static long? Long(IDictionary<string, object> d, string key) => d.TryGetValue(key, out var v) && v is long n ? n : null;

    private static double? Double(IDictionary<string, object> d, string key) => d.TryGetValue(key, out var v)
        ? v switch
        {
            double x => x,
            long n => n,
            _ => null,
        }
        : null;

    private static decimal Money(IDictionary<string, object> d, string key) => Math.Round((decimal)(Double(d, key) ?? 0), 2);

    private static bool? Bool(IDictionary<string, object> d, string key) => d.TryGetValue(key, out var v) && v is bool b ? b : null;

    private static DateTimeOffset? Time(IDictionary<string, object> d, string key) => d.TryGetValue(key, out var v) && v is Timestamp t ? t.ToDateTimeOffset() : null;

    private static IReadOnlyList<string> Strings(IDictionary<string, object> d, string key) =>
        d.TryGetValue(key, out var v) && v is IEnumerable<object> items ? [.. items.OfType<string>()] : [];
}
