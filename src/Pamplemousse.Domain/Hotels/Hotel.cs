using Pamplemousse.Domain.Common;

namespace Pamplemousse.Domain.Hotels;

/// <summary>
/// Everything known about a place to stay that does not depend on the dates, plus its last known price.
/// Empty fields stay null, never 0: a hotel without reviews has no score, not a score of 0.
/// </summary>
public sealed record Hotel
{
    /// <summary>TravelGenie id: the source prefix and the source's id, e.g. "bk:558347".</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required GeoPoint Location { get; init; }

    public string? StayType { get; init; }

    public string? Address { get; init; }

    public string? District { get; init; }

    /// <summary>The source's number for the hotel's city, shared by its hotels, cars and attractions.</summary>
    public long? CityUfi { get; init; }

    public string? CountryCode { get; init; }

    public string? TimeZone { get; init; }

    public double? DistanceToCentreKm { get; init; }

    public bool? IsCityCentre { get; init; }

    /// <summary>1 to 5. <see cref="StarsEstimated"/> is true when the source rated the hotel itself instead of an official rating.</summary>
    public int? Stars { get; init; }

    public bool? StarsEstimated { get; init; }

    /// <summary>Out of 10.</summary>
    public double? ReviewScore { get; init; }

    public int? ReviewCount { get; init; }

    public string? ReviewWord { get; init; }

    public double? BreakfastScore { get; init; }

    public double? WifiScore { get; init; }

    public IReadOnlyList<string> PhotoUrls { get; init; } = [];

    /// <summary>Local times such as "15:00".</summary>
    public string? CheckInFrom { get; init; }

    public string? CheckInUntil { get; init; }

    public string? CheckOutFrom { get; init; }

    public string? CheckOutUntil { get; init; }

    public bool? BreakfastIncluded { get; init; }

    public bool? FamilyFriendly { get; init; }

    /// <summary>Whether a room that fits the travellers could be cancelled for free, for the dates last looked at.</summary>
    public bool? FreeCancellation { get; init; }

    public IReadOnlyList<string> Facilities { get; init; } = [];

    public IReadOnlyList<string> Languages { get; init; } = [];

    /// <summary>The hotel's own page on the booking site, without dates.</summary>
    public string? BookingPageUrl { get; init; }

    public HotelPrice? LastPrice { get; init; }

    /// <summary>The source's full search entry, as JSON text, so new sorts and filters need no new calls.</summary>
    public string? RawSearch { get; init; }

    /// <summary>The source's full details answer, as JSON text.</summary>
    public string? RawDetails { get; init; }

    public DateTimeOffset? SearchUpdatedAt { get; init; }

    public DateTimeOffset? DetailsUpdatedAt { get; init; }

    public bool HasDetails => DetailsUpdatedAt is not null;

    /// <summary>Details are fetched again after a week (ADR 0017).</summary>
    public bool DetailsAreStale(DateTimeOffset now) => DetailsUpdatedAt is not { } at || now - at > TimeSpan.FromDays(7);

    /// <summary>
    /// Combines a newer answer with what is stored: every field the newer answer has wins, and fields it lacks
    /// (details after a search, or the price after opening a hotel) are kept.
    /// </summary>
    public Hotel MergeWith(Hotel newer)
    {
        ArgumentNullException.ThrowIfNull(newer);
        return new Hotel
        {
            Id = Id,
            Name = newer.Name,
            Location = newer.Location,
            StayType = newer.StayType ?? StayType,
            Address = newer.Address ?? Address,
            District = newer.District ?? District,
            CityUfi = newer.CityUfi ?? CityUfi,
            CountryCode = newer.CountryCode ?? CountryCode,
            TimeZone = newer.TimeZone ?? TimeZone,
            DistanceToCentreKm = newer.DistanceToCentreKm ?? DistanceToCentreKm,
            IsCityCentre = newer.IsCityCentre ?? IsCityCentre,
            Stars = newer.Stars ?? Stars,
            StarsEstimated = newer.StarsEstimated ?? StarsEstimated,
            ReviewScore = newer.ReviewScore ?? ReviewScore,
            ReviewCount = newer.ReviewCount ?? ReviewCount,
            ReviewWord = newer.ReviewWord ?? ReviewWord,
            BreakfastScore = newer.BreakfastScore ?? BreakfastScore,
            WifiScore = newer.WifiScore ?? WifiScore,
            PhotoUrls = newer.PhotoUrls.Count > 0 ? newer.PhotoUrls : PhotoUrls,
            CheckInFrom = newer.CheckInFrom ?? CheckInFrom,
            CheckInUntil = newer.CheckInUntil ?? CheckInUntil,
            CheckOutFrom = newer.CheckOutFrom ?? CheckOutFrom,
            CheckOutUntil = newer.CheckOutUntil ?? CheckOutUntil,
            BreakfastIncluded = newer.BreakfastIncluded ?? BreakfastIncluded,
            FamilyFriendly = newer.FamilyFriendly ?? FamilyFriendly,
            FreeCancellation = newer.FreeCancellation ?? FreeCancellation,
            Facilities = newer.Facilities.Count > 0 ? newer.Facilities : Facilities,
            Languages = newer.Languages.Count > 0 ? newer.Languages : Languages,
            BookingPageUrl = newer.BookingPageUrl ?? BookingPageUrl,
            LastPrice = newer.LastPrice ?? LastPrice,
            RawSearch = newer.RawSearch ?? RawSearch,
            RawDetails = newer.RawDetails ?? RawDetails,
            SearchUpdatedAt = newer.SearchUpdatedAt ?? SearchUpdatedAt,
            DetailsUpdatedAt = newer.DetailsUpdatedAt ?? DetailsUpdatedAt,
        };
    }
}

/// <summary>The price of a stay: total with taxes and fees, and per night. Only <see cref="ForStay"/> makes one.</summary>
public sealed record HotelPrice(decimal Total, decimal PerNight, string Currency, DateOnly CheckIn, DateOnly CheckOut, int Adults, DateTimeOffset FetchedAt)
{
    /// <summary>Null when the source gave no usable price (missing, zero or negative), so the hotel is left out.</summary>
    public static HotelPrice? ForStay(decimal? total, HotelSearch search, DateTimeOffset fetchedAt)
    {
        ArgumentNullException.ThrowIfNull(search);
        if (total is not > 0)
        {
            return null;
        }

        var amount = Math.Round(total.Value, 2);
        return new HotelPrice(amount, Math.Round(amount / search.Nights, 2), search.Currency, search.Dates.From, search.Dates.To, search.Adults, fetchedAt);
    }
}
