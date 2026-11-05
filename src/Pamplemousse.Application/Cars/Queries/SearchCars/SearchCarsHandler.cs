using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Application.Ranking;
using Pamplemousse.Domain.Cars;
using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Destinations;
using Pamplemousse.Domain.Exceptions;
using Pamplemousse.Domain.Ranking;

namespace Pamplemousse.Application.Cars.Queries.SearchCars;

public sealed record SearchCarsQuery(
    string DestinationId,
    DateOnly? From,
    DateOnly? To,
    string? Place = null,
    int? DriverAge = null,
    string? Currency = null,
    string? Sort = null,
    IReadOnlyList<string>? Sizes = null,
    string? Gearbox = null,
    int? MinSeats = null,
    int? MinBags = null,
    IReadOnlyList<string>? Companies = null,
    IReadOnlyList<string>? PickUpTypes = null,
    decimal? MaxPrice = null,
    bool? FreeCancellation = null,
    int? Page = null,
    RankingInput? Ranking = null);

public sealed record SearchCarsResponse(
    Destination Destination,
    PickUpPlace Place,
    DateRange Dates,
    int Days,
    int DriverAge,
    string Currency,
    CarSort Sort,
    int Page,
    int TotalItems,
    int TotalPages,
    DateTimeOffset? PricesFetchedAt,
    bool IsSavedPrice,
    bool MayHaveMore,
    IReadOnlyList<CarOffer> Items,
    CarFilterOptions FilterOptions,
    string BookingUrl,
    string Source,
    string? DriverNote,
    string? Message,
    string? SearchUrl,
    IReadOnlyList<string> Warnings,
    RankingResult? Ranking = null);

/// <summary>
/// Rental cars for a destination, place, dates and driver (spec 004). One call to the source returns every car, so results come
/// from the cache when an identical search was answered in the last day; otherwise from the source, saved as history; and when
/// the source can't answer, from the last saved result. Sorting and filtering always run on those cars, never on the source.
/// </summary>
public sealed partial class SearchCarsHandler(
    IReferenceData data,
    ICarProvider provider,
    ICarAirportStore airports,
    ICarHistory history,
    ISearchCache cache,
    ISearchLock searchLock,
    CarSettings settings,
    TimeProvider clock,
    ILogger<SearchCarsHandler> logger) : IQueryHandler<SearchCarsQuery, SearchCarsResponse>
{
    /// <summary>A search takes 20–30 s at the source (research R-7), so the lock outlives it and others wait about as long.</summary>
    private static readonly TimeSpan LockTime = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan WaitForOthers = TimeSpan.FromSeconds(40);

    private const string NoCars = "No rental cars are available here for these dates. Search Booking.com for more, or try another pick-up place.";
    private const string NoMatch = "No cars match these filters. Try fewer filters, or search Booking.com.";
    private const string NotSearched = "Cars could not be searched just now. Try again in a moment, or search Booking.com.";

    public async Task<SearchCarsResponse> HandleAsync(SearchCarsQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var destination = data.FindDestination(query.DestinationId) ?? throw new NotFoundException($"Destination '{query.DestinationId}' was not found.");
        var dates = DateRange.ForTrip(query.From, query.To, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));
        var filters = CarFilters.Create(
            query.Sort, query.Sizes, query.Gearbox, query.MinSeats, query.MinBags, query.Companies, query.PickUpTypes, query.MaxPrice,
            query.FreeCancellation, query.Page);
        var preferences = RankingResult.PreferencesFor(query.Ranking, filters.Sort == CarSort.Match);

        var store = new CarData(airports, history, logger);
        var places = new CarPlaces(provider, store, settings, clock, logger);
        var place = await places.FindAsync(destination, query.Place, ct);
        var search = CarSearch.Create(destination.Id, place, dates, query.DriverAge, query.Currency);

        var warnings = new List<string>(places.Warnings);
        var (cars, isSaved) = await LoadAsync(search, destination.Location, store, warnings, ct);
        var offers = cars?.Cars ?? [];
        IReadOnlyList<CarOffer> matched = filters.Apply(offers);
        var ranking = preferences is null ? null : Rank(ref matched, preferences, filters.Sort == CarSort.Match, search.Currency);

        // No cars at all means the source failed with nothing saved, so nothing is known about this place.
        var message = cars is null ? NotSearched : matched.Count > 0 ? null : offers.Count > 0 ? NoMatch : NoCars;
        var bookingUrl = provider.SearchUrl(destination, search);

        return new SearchCarsResponse(
            destination,
            place,
            dates,
            search.Days,
            search.DriverAge,
            search.Currency,
            filters.Sort,
            filters.Page,
            matched.Count,
            Math.Max(1, (int)Math.Ceiling(matched.Count / (double)CarFilters.PageSize)),
            cars?.FetchedAt,
            isSaved,
            cars?.MayHaveMore ?? false,
            filters.PageOf(matched),
            CarFilterOptions.Of(offers),
            bookingUrl,
            provider.Source,
            search.DriverNote,
            message,
            matched.Count == 0 ? bookingUrl : null,
            [.. warnings, .. store.Warnings],
            ranking);
    }

    /// <summary>Scores the cars (spec 007). With sort=match they are reordered; otherwise each only gains its match.</summary>
    private static RankingResult Rank(ref IReadOnlyList<CarOffer> matched, Preferences preferences, bool byMatch, string currency)
    {
        if (ScoringEngine.NothingApplies(CarRanking.Used, preferences.Weights))
        {
            return RankingResult.Unranked(preferences, "cars");
        }

        var ranked = CarRanking.Rank(matched, preferences, currency);
        if (byMatch)
        {
            matched = [.. ranked.Select(r => r.Offer)];
        }

        return RankingResult.Of(preferences, ranked, c => c.Id);
    }

    /// <summary>The search's cars: from the cache, then the source, then the last saved snapshot. Null when there are none at all.</summary>
    private async Task<(CachedCarSearch? Cars, bool IsSaved)> LoadAsync(CarSearch search, GeoPoint centre, CarData store, List<string> warnings, CancellationToken ct)
    {
        var key = "cars:" + search.Key;
        var cached = await cache.GetAsync<CachedCarSearch>(key, ct);
        if (cached is not null)
        {
            return (cached, false);
        }

        var held = await searchLock.TryAcquireAsync(key, LockTime, ct);
        if (held is null)
        {
            // Someone is already asking the source for this search: wait for their answer rather than pay for it twice.
            var waited = await WaitForAsync(key, ct);
            if (waited is not null)
            {
                return (waited, false);
            }
        }

        // Each call to the source is paid and takes up to 45 s, so it runs to the end even when the caller gives up: the answer
        // is then saved and cached, and the caller's retry gets it at once. The HttpClient's own timeout still ends it.
        var fetch = FetchAndCacheAsync(search, centre, store, key, held);
        string reason;
        try
        {
            return (await fetch.WaitAsync(ct), false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            ObserveInBackground(fetch);
            throw;
        }
        catch (ProviderNotConfiguredException ex)
        {
            reason = ex.Message;
        }
        catch (QuotaExceededException)
        {
            reason = "The monthly car request limit is nearly used";
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            LogSourceFailed(logger, ex);
            reason = "Car prices could not be refreshed";
        }

        // Saved results are a fallback; a cancelled request should still get them, so this read ignores the request's token.
        var saved = await store.LatestAsync(search.Key, CancellationToken.None);
        warnings.Add(saved is null
            ? $"{reason.TrimEnd('.')}. No saved cars for this search yet."
            : string.Create(CultureInfo.InvariantCulture, $"{reason.TrimEnd('.')}. Showing cars from {saved.FetchedAt:yyyy-MM-dd HH:mm} UTC."));
        return saved is null ? (null, false) : (new CachedCarSearch(saved.FetchedAt, saved.MayHaveMore, saved.Cars), true);
    }

    /// <summary>Asks the source, saves and caches the answer, then frees the lock. Never cancelled by the caller.</summary>
    private async Task<CachedCarSearch> FetchAndCacheAsync(CarSearch search, GeoPoint centre, CarData store, string key, IAsyncDisposable? held)
    {
        try
        {
            var fresh = await FetchAsync(search, centre, store, CancellationToken.None);
            await cache.SetAsync(key, fresh, TimeSpan.FromHours(settings.ResultsTtlHours), CancellationToken.None);
            return fresh;
        }
        finally
        {
            if (held is not null)
            {
                await held.DisposeAsync();
            }
        }
    }

    /// <summary>A search the caller left still logs its failure, so it isn't lost as an unobserved task exception.</summary>
    private void ObserveInBackground(Task<CachedCarSearch> fetch) =>
        _ = fetch.ContinueWith(
            t => LogAbandonedSearchFailed(logger, t.Exception!.GetBaseException()),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    private async Task<CachedCarSearch> FetchAsync(CarSearch search, GeoPoint centre, CarData store, CancellationToken ct)
    {
        var results = await provider.SearchAsync(search, ct);
        var now = clock.GetUtcNow();
        var cars = CarOffer.Distinct(results.Cars
            .Select(c => c with { PickUp = c.PickUp with { DistanceKm = Math.Round(c.PickUp.Location.DistanceKmTo(centre), 1) } }));

        await store.AddSnapshotAsync(
            new CarSnapshot(
                search.Key, search.DestinationId, search.Place.Id, search.Dates.From, search.Dates.To, search.DriverAge, search.Currency, now,
                results.MayHaveMore, cars, results.Raw),
            ct);
        return new CachedCarSearch(now, results.MayHaveMore, cars);
    }

    private async Task<CachedCarSearch?> WaitForAsync(string key, CancellationToken ct)
    {
        var until = clock.GetUtcNow() + WaitForOthers;
        while (clock.GetUtcNow() < until)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500), clock, ct);
            if (await cache.GetAsync<CachedCarSearch>(key, ct) is { } cached)
            {
                return cached;
            }
        }

        return null;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Car source failed")]
    private static partial void LogSourceFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Car source failed after the caller left")]
    private static partial void LogAbandonedSearchFailed(ILogger logger, Exception exception);
}
