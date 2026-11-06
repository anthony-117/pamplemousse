using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Domain.Destinations;
using Pamplemousse.Domain.Hotels;
using Pamplemousse.Domain.Places;
using Pamplemousse.Domain.Ranking;

namespace Pamplemousse.Application.Ranking;

/// <summary>
/// What is within walking distance of each hotel, for culture, food, nature and nightlife (spec 007, FR-003 and FR-008).
/// Culture counts the destination's top attractions (the list the app shows, cached a day by its provider). The other three
/// come from <see cref="INearbyPlaces"/>, which keeps each hotel's counts 30 days. Anything that fails counts as unknown,
/// with one warning for the whole list (FR-011).
/// </summary>
public sealed partial class HotelNearby(INearbyPlaces places, IEnumerable<IPlaceProvider> providers, ILogger<HotelNearby> logger)
{
    public const string Warning = "Places near the hotels couldn't be checked just now, so culture, food, nature and nightlife count as average.";

    private const int TopAttractions = 20;
    // OpenTripMap answers 429 to four hotels at once (two requests each) on the free key; two at a time stays under it.
    private const int AtOnce = 2;

    public async Task<(IReadOnlyDictionary<string, Surroundings> Around, string? Warning)> ForAsync(
        Destination destination, IReadOnlyList<HotelOffer> offers, Weights weights, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(offers);
        ArgumentNullException.ThrowIfNull(weights);
        var hotels = offers.Select(o => o.Hotel).DistinctBy(h => h.Id, StringComparer.Ordinal).ToList();
        var failed = false;

        IReadOnlyList<Place>? sights = null;
        if (weights.Culture > 0)
        {
            sights = await SafeAsync(() => providers.First(p => p.Category == PlaceCategory.Attraction).SearchAsync(destination, TopAttractions, ct), ct);
            failed |= sights is null;
        }

        var counts = new ConcurrentDictionary<string, NearbyCounts>(StringComparer.Ordinal);
        if (weights.Food > 0 || weights.Nightlife > 0 || weights.Nature > 0)
        {
            await Parallel.ForEachAsync(hotels, new ParallelOptions { MaxDegreeOfParallelism = AtOnce, CancellationToken = ct }, async (hotel, token) =>
            {
                if (await SafeAsync(() => places.CountAsync(hotel.Location, token), token) is { } found)
                {
                    counts[hotel.Id] = found;
                }
            });
            failed |= counts.Count < hotels.Count;
        }

        var around = hotels.ToDictionary(
            h => h.Id,
            h =>
            {
                var c = counts.GetValueOrDefault(h.Id);
                var nearSights = sights?.Count(s => s.Location.DistanceKmTo(h.Location) <= Surroundings.SightsKm);
                return new Surroundings(nearSights, c?.Food, c?.Nightlife, c?.Nature);
            },
            StringComparer.Ordinal);
        return (around, failed ? Warning : null);
    }

    /// <summary>The value, or null when the source fails, is slow or has no key. A request the caller cancelled still throws.</summary>
    private async Task<T?> SafeAsync<T>(Func<Task<T>> load, CancellationToken ct)
        where T : class
    {
        try
        {
            return await load();
        }
        catch (Exception ex) when (ex is ProviderNotConfiguredException or HttpRequestException or JsonException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            LogPlacesFailed(logger, ex);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Places near hotels could not be counted")]
    private static partial void LogPlacesFailed(ILogger logger, Exception exception);
}
