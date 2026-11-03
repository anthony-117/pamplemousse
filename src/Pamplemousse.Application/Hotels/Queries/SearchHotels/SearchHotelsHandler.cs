using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Destinations;
using Pamplemousse.Domain.Exceptions;
using Pamplemousse.Domain.Hotels;

namespace Pamplemousse.Application.Hotels.Queries.SearchHotels;

public sealed record SearchHotelsQuery(
    string DestinationId,
    DateOnly? From,
    DateOnly? To,
    int? Adults,
    string? Currency,
    string? Sort = null,
    int? MinStars = null,
    double? MinReviewScore = null,
    decimal? MinPrice = null,
    decimal? MaxPrice = null,
    IReadOnlyList<string>? StayTypes = null,
    bool? FreeCancellation = null,
    int? Page = null);

/// <summary>The destination asked for, and the source's city used for it when the names differ.</summary>
public sealed record HotelDestination(string Id, string Name, string? SourceName);

public sealed record SearchHotelsResponse(
    HotelDestination Destination,
    DateRange Dates,
    int Nights,
    int Adults,
    string Currency,
    HotelSort Sort,
    int Page,
    int TotalItems,
    int TotalPages,
    DateTimeOffset? PricesFetchedAt,
    bool IsSavedPrice,
    IReadOnlyList<HotelOffer> Items,
    string Source,
    int? FiltersCheckedOn,
    string? Message,
    string? SearchUrl,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Hotels for a destination and dates (spec 002). Prices come from the cache when an identical search was answered in the
/// last 12 hours; otherwise from the source, saved as history; and when the source can't answer, from the last saved prices.
/// Sorting and filtering always run on the stored hotels, so they never cost a call to the source.
/// </summary>
public sealed partial class SearchHotelsHandler(
    IReferenceData data,
    IHotelProvider provider,
    ICityMappingStore cities,
    IHotelStore hotels,
    IPriceHistory history,
    ISearchCache cache,
    ISearchLock searchLock,
    HotelSettings settings,
    TimeProvider clock,
    ILogger<SearchHotelsHandler> logger) : IQueryHandler<SearchHotelsQuery, SearchHotelsResponse>
{
    private static readonly TimeSpan LockTime = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan WaitForOthers = TimeSpan.FromSeconds(5);

    public async Task<SearchHotelsResponse> HandleAsync(SearchHotelsQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var destination = data.FindDestination(query.DestinationId) ?? throw new NotFoundException($"Destination '{query.DestinationId}' was not found.");
        var now = clock.GetUtcNow();
        var dates = DateRange.ForTrip(query.From, query.To, DateOnly.FromDateTime(now.UtcDateTime));
        var search = HotelSearch.Create(destination.Id, dates, query.Adults, query.Currency);
        var filters = HotelFilters.Create(
            query.Sort, query.MinStars, query.MinReviewScore, query.MinPrice, query.MaxPrice, query.StayTypes, query.FreeCancellation, query.Page);

        var store = new HotelData(hotels, history, cities, logger);
        var run = new Run(search, store);

        // Our page N needs source page N, since both hold 20 hotels.
        var prices = await LoadPricesAsync(destination, run, Math.Max(settings.PagesPerSearch, filters.Page), ct);
        if (prices is null)
        {
            return Respond(destination, run, filters, null, [], [], run.Message ?? NoHotels);
        }

        var ids = prices.Prices.Select(p => p.HotelId).ToList();
        var missing = ids.Where(id => !run.Hotels.ContainsKey(id)).ToList();
        foreach (var hotel in await store.HotelsAsync(missing, ct))
        {
            run.Hotels[hotel.Id] = hotel;
        }

        var offers = prices.Prices
            .Where(p => run.Hotels.ContainsKey(p.HotelId))
            .Select(p => ToOffer(run.Hotels[p.HotelId], p, prices.FetchedAt, search, destination.Location))
            .ToList();
        var matched = filters.Apply(offers);
        var message = matched.Count > 0 ? null : offers.Count > 0 ? NoMatch : NoHotels;
        return Respond(destination, run, filters, prices, offers, matched, message);
    }

    private const string NoHotels = "No hotels are available for these dates. Search the booking site for more.";
    private const string NoMatch = "No hotels match these filters. Try fewer filters, or search the booking site.";

    private SearchHotelsResponse Respond(
        Destination destination, Run run, HotelFilters filters, CachedHotelSearch? prices, List<HotelOffer> offers, IReadOnlyList<HotelOffer> matched, string? message)
    {
        var sourceName = prices?.SourceName ?? run.SourceName;
        var renamed = sourceName is not null && !SameName(sourceName, destination.Name) ? sourceName : null;
        return new SearchHotelsResponse(
            new HotelDestination(destination.Id, destination.Name, renamed),
            run.Search.Dates,
            run.Search.Nights,
            run.Search.Adults,
            run.Search.Currency,
            filters.Sort,
            filters.Page,
            matched.Count,
            Math.Max(prices?.TotalPages ?? 0, filters.Page),
            prices?.FetchedAt,
            run.IsSaved,
            filters.PageOf(matched),
            provider.Source,
            filters.NeedsDetails ? offers.Count(o => o.Hotel.HasDetails) : null,
            message,
            matched.Count == 0 ? provider.SearchUrl(destination, run.Search) : null,
            [.. run.Warnings, .. run.Store.Warnings]);
    }

    /// <summary>
    /// The prices for at least <paramref name="pages"/> source pages: from the cache, then the source, then the last saved snapshots.
    /// Null when there are none at all; <see cref="Run.Message"/> or <see cref="Run.Warnings"/> then says why.
    /// </summary>
    private async Task<CachedHotelSearch?> LoadPricesAsync(Destination destination, Run run, int pages, CancellationToken ct)
    {
        var key = "hotels:" + run.Search.Key;
        var cached = await cache.GetAsync<CachedHotelSearch>(key, ct);
        if (IsEnough(cached, pages))
        {
            return cached;
        }

        await using var held = await searchLock.TryAcquireAsync(run.Search.Key, LockTime, ct);
        if (held is null)
        {
            // Someone is already asking the source for this search: wait for their answer rather than pay for it twice.
            var waited = await WaitForAsync(key, pages, ct);
            if (waited is not null)
            {
                return waited;
            }
        }

        string reason;
        try
        {
            var fresh = await FetchAsync(destination, run, cached, pages, ct);
            if (fresh is not null)
            {
                await cache.SetAsync(key, fresh, TimeLeft(fresh), ct);
            }

            return fresh;
        }
        catch (ProviderNotConfiguredException ex)
        {
            reason = ex.Message;
        }
        catch (QuotaExceededException)
        {
            reason = "The monthly hotel request limit is nearly used";
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            LogSourceFailed(logger, ex);
            reason = "Hotel prices could not be refreshed";
        }

        var saved = cached ?? await FromSnapshotsAsync(run);
        run.Warnings.Add(saved is null
            ? $"{reason.TrimEnd('.')}. No saved prices for this search yet."
            : string.Create(CultureInfo.InvariantCulture, $"{reason.TrimEnd('.')}. Showing prices from {saved.FetchedAt:yyyy-MM-dd HH:mm} UTC."));
        return saved;
    }

    private async Task<CachedHotelSearch?> FetchAsync(Destination destination, Run run, CachedHotelSearch? cached, int pages, CancellationToken ct)
    {
        var city = await run.Store.CityAsync(destination.Id, ct);
        if (city is null)
        {
            city = await provider.FindCityAsync(destination, ct);
            if (city is null)
            {
                run.Message = $"The booking site doesn't know {destination.Name} or a city near it. Search the booking site directly.";
                return null;
            }

            await run.Store.SaveCityAsync(city, ct);
        }

        run.SourceName = city.SourceName;
        var prices = cached?.Prices.ToList() ?? [];
        var fetchedAt = cached?.FetchedAt;
        var totalPages = cached?.TotalPages ?? int.MaxValue;
        var fetched = cached?.PagesFetched ?? 0;
        while (fetched < Math.Min(pages, totalPages))
        {
            var page = fetched + 1;
            var results = await provider.SearchAsync(city, run.Search, page, ct);
            var now = clock.GetUtcNow();
            fetchedAt ??= now;
            totalPages = Math.Max(results.TotalPages, page);
            fetched = page;

            var found = results.Hotels;
            if (settings.DetailsForEveryHotel)
            {
                found = await WithDetailsAsync(found, run.Search, ct);
            }

            var merged = await run.Store.MergeAsync([.. found.Select(f => f.Hotel)], ct);
            foreach (var (id, hotel) in merged)
            {
                run.Hotels[id] = hotel;
            }

            var snapshot = new PriceSnapshot(
                run.Search.Key, destination.Id, run.Search.Dates.From, run.Search.Dates.To, run.Search.Adults, run.Search.Currency, now, page,
                [.. found.Select(f => new SnapshotPrice(f.Hotel.Id, f.Price.Total, f.Price.PerNight, f.Rank, run.Hotels[f.Hotel.Id].FreeCancellation))]);

            // Two independent writes, sent together to save a round trip.
            await Task.WhenAll(run.Store.SaveHotelsAsync([.. merged.Values], ct), run.Store.AddSnapshotAsync(snapshot, ct));

            // The same hotel can come back on two pages when the source's order shifts between calls.
            prices.AddRange(snapshot.Prices.Where(p => prices.TrueForAll(x => !string.Equals(x.HotelId, p.HotelId, StringComparison.Ordinal))));
        }

        return new CachedHotelSearch(fetchedAt ?? clock.GetUtcNow(), fetched, totalPages == int.MaxValue ? fetched : totalPages, city.SourceName, prices);
    }

    /// <summary>Adds details to each hotel (one request each). A hotel whose details fail keeps its search fields.</summary>
    private async Task<IReadOnlyList<FoundHotel>> WithDetailsAsync(IReadOnlyList<FoundHotel> found, HotelSearch search, CancellationToken ct)
    {
        var withDetails = new List<FoundHotel>(found.Count);
        foreach (var f in found)
        {
            var details = await provider.GetDetailsAsync(f.Hotel.Id, search, ct);
            withDetails.Add(details is null ? f : f with { Hotel = f.Hotel.MergeWith(details) });
        }

        return withDetails;
    }

    private async Task<CachedHotelSearch?> WaitForAsync(string key, int pages, CancellationToken ct)
    {
        var until = clock.GetUtcNow() + WaitForOthers;
        while (clock.GetUtcNow() < until)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), clock, ct);
            var cached = await cache.GetAsync<CachedHotelSearch>(key, ct);
            if (IsEnough(cached, pages))
            {
                return cached;
            }
        }

        return null;
    }

    private static async Task<CachedHotelSearch?> FromSnapshotsAsync(Run run)
    {
        // Saved prices are a fallback; a cancelled request should still get them, so this read ignores the request's token.
        var snapshots = await run.Store.LatestAsync(run.Search.Key, CancellationToken.None);
        if (snapshots.Count == 0)
        {
            return null;
        }

        run.IsSaved = true;
        var prices = snapshots.SelectMany(s => s.Prices).DistinctBy(p => p.HotelId, StringComparer.Ordinal).ToList();
        return new CachedHotelSearch(snapshots.Min(s => s.FetchedAt), snapshots.Count, snapshots.Count, run.SourceName ?? "", prices);
    }

    private static bool IsEnough(CachedHotelSearch? cached, int pages) => cached is not null && cached.PagesFetched >= Math.Min(pages, cached.TotalPages);

    /// <summary>Results expire 12 hours after the first page was fetched, even when later pages are added.</summary>
    private TimeSpan TimeLeft(CachedHotelSearch prices)
    {
        var left = prices.FetchedAt + TimeSpan.FromHours(settings.ResultsTtlHours) - clock.GetUtcNow();
        return left > TimeSpan.FromMinutes(1) ? left : TimeSpan.FromMinutes(1);
    }

    private HotelOffer ToOffer(Hotel hotel, SnapshotPrice price, DateTimeOffset fetchedAt, HotelSearch search, GeoPoint centre)
    {
        var km = Math.Round(hotel.Location.DistanceKmTo(centre), 1);
        var stay = new HotelPrice(price.Total, price.PerNight, search.Currency, search.Dates.From, search.Dates.To, search.Adults, fetchedAt);
        return new HotelOffer(hotel, stay, price.Rank, km, km > settings.NearCityRadiusKm);
    }

    private static bool SameName(string a, string b) =>
        CultureInfo.InvariantCulture.CompareInfo.Compare(a, b, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) == 0;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Hotel source failed")]
    private static partial void LogSourceFailed(ILogger logger, Exception exception);

    /// <summary>What one request gathered along the way.</summary>
    private sealed class Run(HotelSearch search, HotelData store)
    {
        public HotelSearch Search { get; } = search;

        public HotelData Store { get; } = store;

        public Dictionary<string, Hotel> Hotels { get; } = new(StringComparer.Ordinal);

        public List<string> Warnings { get; } = [];

        public string? SourceName { get; set; }

        public string? Message { get; set; }

        public bool IsSaved { get; set; }
    }
}
