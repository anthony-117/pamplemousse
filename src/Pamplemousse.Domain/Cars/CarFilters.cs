using Pamplemousse.Domain.Exceptions;

namespace Pamplemousse.Domain.Cars;

public enum CarSort
{
    /// <summary>The source's own order.</summary>
    Recommended,

    /// <summary>Cheapest first.</summary>
    Price,

    /// <summary>Best-rated rental company first.</summary>
    Rating,
}

/// <summary>A filter value present in a search, and how many cars have it.</summary>
public sealed record FilterOption(string Value, int Count);

/// <summary>The values each filter can take in one search, counted over all its cars (FR-008).</summary>
public sealed record CarFilterOptions(IReadOnlyList<FilterOption> Sizes, IReadOnlyList<FilterOption> Companies, IReadOnlyList<FilterOption> PickUpTypes)
{
    public static CarFilterOptions Of(IReadOnlyCollection<CarOffer> offers)
    {
        ArgumentNullException.ThrowIfNull(offers);
        return new CarFilterOptions(
            Count(offers.Select(o => o.Size)),
            Count(offers.Select(o => o.Company.Name)),
            [.. offers.GroupBy(o => o.PickUp.Type).OrderBy(g => g.Key).Select(g => new FilterOption(g.Key.ToString(), g.Count()))]);

        static List<FilterOption> Count(IEnumerable<string> values) => [.. values
            .GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new FilterOption(g.Key, g.Count()))];
    }
}

/// <summary>
/// How to order and narrow a search's stored cars. Changing these never calls the source (FR-008a).
/// Only <see cref="Create"/> makes one, so every instance is valid.
/// </summary>
public sealed record CarFilters
{
    public const int PageSize = 20;
    private const int MaxCount = 9;

    private CarFilters()
    {
    }

    public CarSort Sort { get; private init; }

    public IReadOnlyList<string> Sizes { get; private init; } = [];

    public Gearbox? Gearbox { get; private init; }

    public int? MinSeats { get; private init; }

    public int? MinBags { get; private init; }

    public IReadOnlyList<string> Companies { get; private init; } = [];

    public IReadOnlyList<PickUpType> PickUpTypes { get; private init; } = [];

    public decimal? MaxPrice { get; private init; }

    public bool FreeCancellation { get; private init; }

    public int Page { get; private init; } = 1;

    public static CarFilters Create(
        string? sort = null,
        IReadOnlyList<string>? sizes = null,
        string? gearbox = null,
        int? minSeats = null,
        int? minBags = null,
        IReadOnlyList<string>? companies = null,
        IReadOnlyList<string>? pickUpTypes = null,
        decimal? maxPrice = null,
        bool? freeCancellation = null,
        int? page = null)
    {
        var order = CarSort.Recommended;
        if (sort is not null && (!Enum.TryParse(sort, ignoreCase: true, out order) || !Enum.IsDefined(order) || IsNumber(sort)))
        {
            throw new DomainException("Use 'recommended', 'price' or 'rating'.", "sort");
        }

        Gearbox? box = null;
        if (gearbox is not null)
        {
            box = Parse<Gearbox>(gearbox) ?? throw new DomainException("Use 'manual' or 'automatic'.", "gearbox");
        }

        if (minSeats is < 1 or > MaxCount)
        {
            throw new DomainException($"Minimum seats must be between 1 and {MaxCount}.", "minSeats");
        }

        if (minBags is < 1 or > MaxCount)
        {
            throw new DomainException($"Minimum bags must be between 1 and {MaxCount}.", "minBags");
        }

        if (maxPrice is <= 0)
        {
            throw new DomainException("Maximum price must be more than 0.", "maxPrice");
        }

        if (page is < 1)
        {
            throw new DomainException("Page must be 1 or more.", "page");
        }

        var types = new List<PickUpType>();
        foreach (var value in Clean(pickUpTypes))
        {
            types.Add(Parse<PickUpType>(value)
                ?? throw new DomainException("Use 'terminal', 'shuttle', 'rentalCentre', 'trainStation', 'downtown' or 'other'.", "pickUpType"));
        }

        return new CarFilters
        {
            Sort = order,
            Sizes = Clean(sizes),
            Gearbox = box,
            MinSeats = minSeats,
            MinBags = minBags,
            Companies = Clean(companies),
            PickUpTypes = [.. types.Distinct()],
            MaxPrice = maxPrice,
            FreeCancellation = freeCancellation ?? false,
            Page = page ?? 1,
        };
    }

    /// <summary>Every offer that matches, in the chosen order (ties keep the source's order). Paging is separate.</summary>
    public IReadOnlyList<CarOffer> Apply(IEnumerable<CarOffer> offers)
    {
        ArgumentNullException.ThrowIfNull(offers);

        // A car with an unknown seat or bag count can't be shown to meet a minimum, so it doesn't match.
        var matching = offers
            .Where(o => Sizes.Count == 0 || Sizes.Contains(o.Size, StringComparer.OrdinalIgnoreCase))
            .Where(o => Gearbox is null || o.Gearbox == Gearbox)
            .Where(o => MinSeats is null || o.Seats >= MinSeats)
            .Where(o => MinBags is null || o.Bags >= MinBags)
            .Where(o => Companies.Count == 0 || Companies.Contains(o.Company.Name, StringComparer.OrdinalIgnoreCase))
            .Where(o => PickUpTypes.Count == 0 || PickUpTypes.Contains(o.PickUp.Type))
            .Where(o => MaxPrice is null || o.Price.Total <= MaxPrice)
            .Where(o => !FreeCancellation || o.FreeCancellation == true)
            .OrderBy(o => o.Rank);

        // Companies without a rating go last; equal ratings show the cheaper car first.
        IEnumerable<CarOffer> sorted = Sort switch
        {
            CarSort.Price => matching.OrderBy(o => o.Price.Total),
            CarSort.Rating => matching.OrderBy(o => o.Company.Rating is null).ThenByDescending(o => o.Company.Rating).ThenBy(o => o.Price.Total),
            _ => matching,
        };
        return [.. sorted];
    }

    public IReadOnlyList<CarOffer> PageOf(IReadOnlyList<CarOffer> offers)
    {
        ArgumentNullException.ThrowIfNull(offers);
        return [.. offers.Skip((Page - 1) * PageSize).Take(PageSize)];
    }

    private static bool IsNumber(string value) => value.Trim().TrimStart('-', '+') is { Length: > 0 } digits && digits.All(char.IsAsciiDigit);

    private static List<string> Clean(IReadOnlyList<string>? values) =>
        [.. (values ?? []).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>A named value only: numbers such as "1" are refused, though <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/> takes them.</summary>
    private static T? Parse<T>(string value)
        where T : struct, Enum =>
        !IsNumber(value) && Enum.TryParse<T>(value.Trim(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed) ? parsed : null;
}
