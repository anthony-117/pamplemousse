using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Domain.Hotels;

namespace Pamplemousse.Infrastructure.Storage;

/// <summary>Stands in for Firestore when it is not configured: everything works, but nothing survives a restart.</summary>
public sealed partial class InMemoryHotelData : ICityMappingStore, IHotelStore, IPriceHistory
{
    private readonly ConcurrentDictionary<string, CityMapping> _cities = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Hotel> _hotels = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<PriceSnapshot> _snapshots = new();

    public InMemoryHotelData(ILogger<InMemoryHotelData> logger)
    {
        LogNotConfigured(logger);
    }

    /// <summary>Every snapshot saved, oldest first.</summary>
    public IEnumerable<PriceSnapshot> Snapshots => _snapshots;

    public Task<CityMapping?> GetAsync(string destinationId, CancellationToken ct) => Task.FromResult(_cities.GetValueOrDefault(destinationId));

    public Task SaveAsync(CityMapping mapping, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        _cities[mapping.DestinationId] = mapping;
        return Task.CompletedTask;
    }

    Task<Hotel?> IHotelStore.GetAsync(string id, CancellationToken ct) => Task.FromResult(_hotels.GetValueOrDefault(id));

    public Task<IReadOnlyList<Hotel>> GetManyAsync(IReadOnlyCollection<string> ids, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ids);
        IReadOnlyList<Hotel> found = [.. ids.Select(_hotels.GetValueOrDefault).OfType<Hotel>()];
        return Task.FromResult(found);
    }

    public Task SaveManyAsync(IReadOnlyList<Hotel> hotels, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(hotels);
        foreach (var hotel in hotels)
        {
            _hotels[hotel.Id] = hotel;
        }

        return Task.CompletedTask;
    }

    public Task AddAsync(PriceSnapshot snapshot, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _snapshots.Enqueue(snapshot);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PriceSnapshot>> GetLatestAsync(string searchKey, CancellationToken ct)
    {
        IReadOnlyList<PriceSnapshot> latest = [.. _snapshots
            .Where(s => string.Equals(s.SearchKey, searchKey, StringComparison.Ordinal))
            .GroupBy(s => s.Page)
            .Select(g => g.MaxBy(s => s.FetchedAt)!)
            .OrderBy(s => s.Page)];
        return Task.FromResult(latest);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Hotel data is kept in memory only. Set FIREBASE_PROJECT_ID and GOOGLE_APPLICATION_CREDENTIALS to save it in Firestore.")]
    private static partial void LogNotConfigured(ILogger logger);
}
