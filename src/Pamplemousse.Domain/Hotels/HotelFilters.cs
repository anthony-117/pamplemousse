using Pamplemousse.Domain.Exceptions;

namespace Pamplemousse.Domain.Hotels;

public enum HotelSort
{
    /// <summary>The source's own order.</summary>
    Recommended,

    /// <summary>Cheapest first.</summary>
    Price,

    /// <summary>Best reviewed first.</summary>
    Review,

    /// <summary>Most stars first.</summary>
    Stars,

    /// <summary>Closest to the centre first.</summary>
    Distance,

    /// <summary>Best match for the traveller's preferences first (spec 007). The filters keep the source's order; the ranking orders them.</summary>
    Match,
}

/// <summary>
/// How to order and narrow the stored hotels of a search. Changing these never calls the source.
/// Only <see cref="Create"/> makes one, so every instance is valid.
/// </summary>
public sealed record HotelFilters
{
    public const int PageSize = 20;

    private HotelFilters()
    {
    }

    public HotelSort Sort { get; private init; }

    public int? MinStars { get; private init; }

    public double? MinReviewScore { get; private init; }

    public decimal? MinPrice { get; private init; }

    public decimal? MaxPrice { get; private init; }

    public IReadOnlyList<string> StayTypes { get; private init; } = [];

    public bool FreeCancellation { get; private init; }

    public int Page { get; private init; } = 1;

    /// <summary>Filters that only hotel details can answer, so they skip hotels that were never opened.</summary>
    public bool NeedsDetails => StayTypes.Count > 0 || FreeCancellation;

    public static HotelFilters Create(
        string? sort = null,
        int? minStars = null,
        double? minReviewScore = null,
        decimal? minPrice = null,
        decimal? maxPrice = null,
        IReadOnlyList<string>? stayTypes = null,
        bool? freeCancellation = null,
        int? page = null)
    {
        var order = HotelSort.Recommended;
        if (sort is not null && (!Enum.TryParse(sort, ignoreCase: true, out order) || !Enum.IsDefined(order)))
        {
            throw new DomainException("Use 'recommended', 'price', 'review', 'stars', 'distance' or 'match'.", "sort");
        }

        if (minStars is < 1 or > 5)
        {
            throw new DomainException("Minimum stars must be between 1 and 5.", "minStars");
        }

        if (minReviewScore is < 0 or > 10)
        {
            throw new DomainException("Minimum review score must be between 0 and 10.", "minReview");
        }

        if (minPrice < 0 || maxPrice < 0 || minPrice > maxPrice)
        {
            throw new DomainException("Prices must be 0 or more, with 'minPrice' at most 'maxPrice'.", "maxPrice");
        }

        if (page is < 1)
        {
            throw new DomainException("Page must be 1 or more.", "page");
        }

        return new HotelFilters
        {
            Sort = order,
            MinStars = minStars,
            MinReviewScore = minReviewScore,
            MinPrice = minPrice,
            MaxPrice = maxPrice,
            StayTypes = [.. (stayTypes ?? []).Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim())],
            FreeCancellation = freeCancellation ?? false,
            Page = page ?? 1,
        };
    }

    /// <summary>Every offer that matches, in the chosen order (ties keep the source's order). Paging is separate.</summary>
    public IReadOnlyList<HotelOffer> Apply(IEnumerable<HotelOffer> offers)
    {
        ArgumentNullException.ThrowIfNull(offers);
        var matching = offers
            .Where(o => MinStars is null || o.Hotel.Stars >= MinStars)
            .Where(o => MinReviewScore is null || o.Hotel.ReviewScore >= MinReviewScore)
            .Where(o => MinPrice is null || o.Price.Total >= MinPrice)
            .Where(o => MaxPrice is null || o.Price.Total <= MaxPrice)
            .Where(o => StayTypes.Count == 0 || (o.Hotel.StayType is { } type && StayTypes.Contains(type, StringComparer.OrdinalIgnoreCase)))
            .Where(o => !FreeCancellation || o.Hotel.FreeCancellation == true)
            .OrderBy(o => o.Rank);

        // Hotels without the sorted value go last, whichever way the list is sorted.
        IEnumerable<HotelOffer> sorted = Sort switch
        {
            HotelSort.Price => matching.OrderBy(o => o.Price.Total),
            HotelSort.Review => matching.OrderBy(o => o.Hotel.ReviewScore is null).ThenByDescending(o => o.Hotel.ReviewScore),
            HotelSort.Stars => matching.OrderBy(o => o.Hotel.Stars is null).ThenByDescending(o => o.Hotel.Stars),
            HotelSort.Distance => matching.OrderBy(o => o.DistanceKm),
            _ => matching,
        };
        return [.. sorted];
    }

    public IReadOnlyList<HotelOffer> PageOf(IReadOnlyList<HotelOffer> offers)
    {
        ArgumentNullException.ThrowIfNull(offers);
        return [.. offers.Skip((Page - 1) * PageSize).Take(PageSize)];
    }
}
