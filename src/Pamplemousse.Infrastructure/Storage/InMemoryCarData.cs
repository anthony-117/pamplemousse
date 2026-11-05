using System.Collections.Concurrent;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Domain.Cars;

namespace Pamplemousse.Infrastructure.Storage;

/// <summary>Stands in for Firestore when it is not configured: everything works, but nothing survives a restart.</summary>
public sealed class InMemoryCarData : ICarAirportStore, ICarHistory
{
    private readonly ConcurrentDictionary<string, CarAirports> _airports = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<CarSnapshot> _snapshots = new();

    /// <summary>Every snapshot saved, oldest first.</summary>
    public IEnumerable<CarSnapshot> Snapshots => _snapshots;

    public Task<CarAirports?> GetAsync(string destinationId, CancellationToken ct) => Task.FromResult(_airports.GetValueOrDefault(destinationId));

    public Task SaveAsync(CarAirports airports, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(airports);
        _airports[airports.DestinationId] = airports;
        return Task.CompletedTask;
    }

    public Task AddAsync(CarSnapshot snapshot, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _snapshots.Enqueue(snapshot);
        return Task.CompletedTask;
    }

    public Task<CarSnapshot?> GetLatestAsync(string searchKey, CancellationToken ct) => Task.FromResult(
        _snapshots.Where(s => string.Equals(s.SearchKey, searchKey, StringComparison.Ordinal)).MaxBy(s => s.FetchedAt));
}
