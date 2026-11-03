using Pamplemousse.Domain.Hotels;

namespace Pamplemousse.Application.Abstractions;

/// <summary>A destination's ids at the hotel source, stored once (Firestore <c>cities</c>, ADR 0017).</summary>
public interface ICityMappingStore
{
    Task<CityMapping?> GetAsync(string destinationId, CancellationToken ct);

    Task SaveAsync(CityMapping mapping, CancellationToken ct);
}

/// <summary>Every hotel seen, with all its source data (Firestore <c>hotels</c>).</summary>
public interface IHotelStore
{
    Task<Hotel?> GetAsync(string id, CancellationToken ct);

    /// <summary>The stored hotels among <paramref name="ids"/>; unknown ids are skipped.</summary>
    Task<IReadOnlyList<Hotel>> GetManyAsync(IReadOnlyCollection<string> ids, CancellationToken ct);

    /// <summary>Replaces each hotel's document. Callers merge with the stored hotel first (<see cref="Hotel.MergeWith"/>).</summary>
    Task SaveManyAsync(IReadOnlyList<Hotel> hotels, CancellationToken ct);
}

/// <summary>Price history (Firestore <c>priceSnapshots</c> and <c>hotels/{id}/prices</c>).</summary>
public interface IPriceHistory
{
    /// <summary>Saves the snapshot, and one price record per hotel in it.</summary>
    Task AddAsync(PriceSnapshot snapshot, CancellationToken ct);

    /// <summary>The newest snapshot of each page for a search, page 1 first; empty when the search was never answered.</summary>
    Task<IReadOnlyList<PriceSnapshot>> GetLatestAsync(string searchKey, CancellationToken ct);
}

/// <summary>Short-lived results of identical searches (Redis, or memory when Redis is not configured).</summary>
public interface ISearchCache
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct)
        where T : class;

    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct)
        where T : class;
}

/// <summary>Makes sure only one request at a time calls the source for the same search.</summary>
public interface ISearchLock
{
    /// <summary>The lock, to dispose when done; null when someone else holds it. It frees itself after <paramref name="ttl"/>.</summary>
    Task<IAsyncDisposable?> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct);
}

/// <summary>Thrown when the source's monthly request limit is nearly used, so no new call is made.</summary>
public sealed class QuotaExceededException(string provider)
    : InvalidOperationException($"The monthly {provider} request limit is nearly used.")
{
    public string Provider { get; } = provider;
}
