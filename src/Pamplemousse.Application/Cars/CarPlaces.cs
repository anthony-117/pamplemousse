using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Domain.Cars;
using Pamplemousse.Domain.Destinations;
using Pamplemousse.Domain.Exceptions;

namespace Pamplemousse.Application.Cars;

/// <summary>
/// A destination's pick-up places (spec 004, FR-002): the city centre, which needs no request, then the airports near it,
/// looked up once at the source, stored on the city, and looked up again after <see cref="CarSettings.AirportsRefreshDays"/>.
/// </summary>
internal sealed partial class CarPlaces(ICarProvider provider, CarData store, CarSettings settings, TimeProvider clock, ILogger logger)
{
    public List<string> Warnings { get; } = [];

    /// <summary>The centre first, then the airports, nearest first. A failed look-up gives only the centre and a warning.</summary>
    public async Task<IReadOnlyList<PickUpPlace>> ListAsync(Destination destination, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(destination);
        return [PickUpPlace.Centre(destination.Location), .. await AirportsAsync(destination, ct)];
    }

    /// <summary>The place with this id; the centre when none is given. An id that isn't one of the city's places fails on <c>place</c>.</summary>
    public async Task<PickUpPlace> FindAsync(Destination destination, string? placeId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (string.IsNullOrWhiteSpace(placeId) || string.Equals(placeId.Trim(), PickUpPlace.CentreId, StringComparison.OrdinalIgnoreCase))
        {
            return PickUpPlace.Centre(destination.Location);
        }

        var airports = await AirportsAsync(destination, ct);
        return airports.FirstOrDefault(a => string.Equals(a.Id, placeId.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new DomainException(
                airports.Count == 0
                    ? $"Use 'centre': no airport near {destination.Name} is known for car rental."
                    : $"Use 'centre' or one of the airports near {destination.Name}: {string.Join(", ", airports.Select(a => a.Id))}.",
                "place");
    }

    private async Task<IReadOnlyList<PickUpPlace>> AirportsAsync(Destination destination, CancellationToken ct)
    {
        var stored = await store.AirportsAsync(destination.Id, ct);
        var now = clock.GetUtcNow();
        if (stored is not null && now - stored.CheckedAt < TimeSpan.FromDays(settings.AirportsRefreshDays))
        {
            return stored.Airports;
        }

        string reason;
        try
        {
            var found = await provider.FindAirportsAsync(destination, ct);

            // Names repeat across the world ("Rome" also finds Moncton's Roméo LeBlanc airport), so only nearby airports count.
            var near = found
                .Select(a => (Airport: a, Km: destination.Location.DistanceKmTo(a.Location)))
                .Where(a => a.Km <= settings.AirportRadiusKm)
                .OrderBy(a => a.Km)
                .Select(a => a.Airport)
                .DistinctBy(a => a.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
            await store.SaveAirportsAsync(new CarAirports(destination.Id, near, now), ct);
            return near;
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
            LogLookUpFailed(logger, ex);
            reason = "Airports could not be looked up";
        }

        // Older airports are still right far more often than not; without any, the centre still works.
        if (stored is not null)
        {
            return stored.Airports;
        }

        Warnings.Add($"{reason.TrimEnd('.')}. Only the city centre is offered for now.");
        return [];
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Car airport look-up failed")]
    private static partial void LogLookUpFailed(ILogger logger, Exception exception);
}
