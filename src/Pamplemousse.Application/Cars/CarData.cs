using Microsoft.Extensions.Logging;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Domain.Cars;

namespace Pamplemousse.Application.Cars;

/// <summary>
/// How many cars each search keeps and for how long, from the "Cars" section of appsettings (spec 004).
/// </summary>
public sealed class CarSettings
{
    /// <summary>How long identical searches reuse their results (ADR 0017: 1 day).</summary>
    public int ResultsTtlHours { get; set; } = 24;

    /// <summary>Airports further than this from the destination are not offered (research R-3).</summary>
    public double AirportRadiusKm { get; set; } = 50;

    /// <summary>A city's airports are looked up again after this many days (FR-013).</summary>
    public int AirportsRefreshDays { get; set; } = 30;
}

/// <summary>A search's cars as kept in the cache for <see cref="CarSettings.ResultsTtlHours"/>.</summary>
public sealed record CachedCarSearch(DateTimeOffset FetchedAt, bool MayHaveMore, IReadOnlyList<CarOffer> Cars);

/// <summary>
/// Reads and writes stored car data without letting a storage failure break the response (Principle IV):
/// a failed read gives nothing, a failed write is logged, and either adds one warning.
/// </summary>
internal sealed partial class CarData(ICarAirportStore airports, ICarHistory history, ILogger logger)
{
    public const string Unavailable = "Saved car data is unavailable right now.";

    private readonly List<string> _warnings = [];

    public IReadOnlyList<string> Warnings => _warnings;

    public Task<CarAirports?> AirportsAsync(string destinationId, CancellationToken ct) => TryAsync(() => airports.GetAsync(destinationId, ct));

    public Task SaveAirportsAsync(CarAirports found, CancellationToken ct) => TryAsync(() => airports.SaveAsync(found, ct));

    public Task AddSnapshotAsync(CarSnapshot snapshot, CancellationToken ct) => TryAsync(() => history.AddAsync(snapshot, ct));

    public Task<CarSnapshot?> LatestAsync(string searchKey, CancellationToken ct) => TryAsync(() => history.GetLatestAsync(searchKey, ct));

    private async Task<T?> TryAsync<T>(Func<Task<T?>> read)
        where T : class
    {
        try
        {
            return await read();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Fail(ex);
            return null;
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
        lock (_warnings)
        {
            if (!_warnings.Contains(Unavailable, StringComparer.Ordinal))
            {
                _warnings.Add(Unavailable);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Car storage failed")]
    private static partial void LogStorageFailed(ILogger logger, Exception exception);
}
