using Pamplemousse.Domain.Hotels;

namespace Pamplemousse.Application.Hotels;

/// <summary>
/// How much hotel data each search gathers (FR-008c), from the "Hotels" section of appsettings.
/// The defaults suit the free plan: each page and each hotel's details cost one of 530 monthly requests.
/// </summary>
public sealed class HotelSettings
{
    /// <summary>Source pages (20 hotels each) fetched for a new search.</summary>
    public int PagesPerSearch { get; set; } = 1;

    /// <summary>Fetch details for every listed hotel, so every filter covers every hotel. About 21 requests a search instead of 1.</summary>
    public bool DetailsForEveryHotel { get; set; }

    /// <summary>Beyond this distance from the destination, a hotel is marked as near the city rather than in it.</summary>
    public double NearCityRadiusKm { get; set; } = 10;

    /// <summary>How long identical searches reuse their results (ADR 0017).</summary>
    public int ResultsTtlHours { get; set; } = 12;
}

/// <summary>A search's prices as kept in the cache for <see cref="HotelSettings.ResultsTtlHours"/>: every page fetched so far.</summary>
public sealed record CachedHotelSearch(DateTimeOffset FetchedAt, int PagesFetched, int TotalPages, string SourceName, IReadOnlyList<SnapshotPrice> Prices);
