using Microsoft.Extensions.Logging;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Domain.Hotels;

namespace Pamplemousse.Application.Hotels;

/// <summary>
/// Reads and writes stored hotel data without letting a storage failure break the response (Principle IV):
/// a failed read gives nothing, a failed write is logged, and either adds one warning.
/// </summary>
internal sealed partial class HotelData(IHotelStore hotels, IPriceHistory history, ICityMappingStore cities, ILogger logger)
{
    public const string Unavailable = "Saved hotel data is unavailable right now.";

    private readonly List<string> _warnings = [];

    public IReadOnlyList<string> Warnings => _warnings;

    public Task<CityMapping?> CityAsync(string destinationId, CancellationToken ct) => TryAsync(() => cities.GetAsync(destinationId, ct), null);

    public Task SaveCityAsync(CityMapping city, CancellationToken ct) => TryAsync(() => cities.SaveAsync(city, ct));

    public Task<Hotel?> HotelAsync(string id, CancellationToken ct) => TryAsync(() => hotels.GetAsync(id, ct), null);

    public async Task<IReadOnlyList<Hotel>> HotelsAsync(IReadOnlyCollection<string> ids, CancellationToken ct) =>
        await TryAsync(() => hotels.GetManyAsync(ids, ct), []) ?? [];

    public Task SaveHotelsAsync(IReadOnlyList<Hotel> list, CancellationToken ct) => TryAsync(() => hotels.SaveManyAsync(list, ct));

    public Task AddSnapshotAsync(PriceSnapshot snapshot, CancellationToken ct) => TryAsync(() => history.AddAsync(snapshot, ct));

    public async Task<IReadOnlyList<PriceSnapshot>> LatestAsync(string searchKey, CancellationToken ct) =>
        await TryAsync(() => history.GetLatestAsync(searchKey, ct), []) ?? [];

    /// <summary>Stored hotels merged with <paramref name="fresh"/> answers, saved, and returned by id.</summary>
    public async Task<Dictionary<string, Hotel>> MergeAndSaveAsync(IReadOnlyList<Hotel> fresh, CancellationToken ct)
    {
        var stored = (await HotelsAsync([.. fresh.Select(h => h.Id)], ct)).ToDictionary(h => h.Id, StringComparer.Ordinal);
        var merged = fresh.Select(h => stored.TryGetValue(h.Id, out var old) ? old.MergeWith(h) : h).ToList();
        await SaveHotelsAsync(merged, ct);
        return merged.ToDictionary(h => h.Id, StringComparer.Ordinal);
    }

    private async Task<T?> TryAsync<T>(Func<Task<T>> read, T? fallback)
    {
        try
        {
            return await read();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Fail(ex);
            return fallback;
        }
    }

    private async Task TryAsync(Func<Task> write)
    {
        try
        {
            await write();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Fail(ex);
        }
    }

    private void Fail(Exception ex)
    {
        LogStorageFailed(logger, ex);
        if (!_warnings.Contains(Unavailable, StringComparer.Ordinal))
        {
            _warnings.Add(Unavailable);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Hotel storage failed")]
    private static partial void LogStorageFailed(ILogger logger, Exception exception);
}
