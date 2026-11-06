using Pamplemousse.Application.Abstractions;
using Pamplemousse.Domain.Cars;
using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Destinations;

namespace Pamplemousse.Application.Tests.Cars;

/// <summary>A car source that answers from lists, counts its calls, and can fail on demand.</summary>
internal sealed class FakeCarProvider : ICarProvider
{
    public List<PickUpPlace> Airports { get; } = [];

    public List<CarOffer> Cars { get; } = [];

    public bool MayHaveMore { get; set; }

    public Exception? Error { get; set; }

    public Exception? AirportError { get; set; }

    public int AirportCalls { get; private set; }

    public List<CarSearch> Searches { get; } = [];

    public string Source => "Booking.com";

    public Task<IReadOnlyList<PickUpPlace>> FindAirportsAsync(Destination destination, CancellationToken ct)
    {
        AirportCalls++;
        return AirportError is null ? Task.FromResult<IReadOnlyList<PickUpPlace>>([.. Airports]) : Task.FromException<IReadOnlyList<PickUpPlace>>(AirportError);
    }

    /// <summary>When set, a search waits for it, as a slow source does, so a test can leave mid-search.</summary>
    public TaskCompletionSource? Gate { get; set; }

    public async Task<CarResults> SearchAsync(CarSearch search, CancellationToken ct)
    {
        Searches.Add(search);
        if (Gate is { } gate)
        {
            await gate.Task.WaitAsync(ct);
        }

        return Error is null ? new CarResults([.. Cars], MayHaveMore, [42]) : throw Error;
    }

    public string SearchUrl(Destination destination, CarSearch search) => $"https://cars/search?place={search.Place.Id}&age={search.DriverAge}";
}

/// <summary>Car airports and history in memory, with an optional failure for every call.</summary>
internal sealed class FakeCarData : ICarAirportStore, ICarHistory
{
    public Dictionary<string, CarAirports> Airports { get; } = new(StringComparer.Ordinal);

    public List<CarSnapshot> Snapshots { get; } = [];

    public Exception? Error { get; set; }

    public Task<CarAirports?> GetAsync(string destinationId, CancellationToken ct) => Run(() => Airports.GetValueOrDefault(destinationId));

    public Task SaveAsync(CarAirports airports, CancellationToken ct) => Run(() => Airports[airports.DestinationId] = airports);

    public Task AddAsync(CarSnapshot snapshot, CancellationToken ct) => Run(() =>
    {
        Snapshots.Add(snapshot);
        return true;
    });

    public Task<CarSnapshot?> GetLatestAsync(string searchKey, CancellationToken ct) =>
        Run(() => Snapshots.Where(s => string.Equals(s.SearchKey, searchKey, StringComparison.Ordinal)).MaxBy(s => s.FetchedAt));

    private Task<T> Run<T>(Func<T> action) => Error is null ? Task.FromResult(action()) : Task.FromException<T>(Error);
}

internal static class CarFakes
{
    public static readonly PickUpPlace Fiumicino = PickUpPlace.Airport("FCO", "Fiumicino Airport", new GeoPoint(41.7927, 12.2523));

    public static readonly PickUpPlace Moncton = PickUpPlace.Airport("YQM", "Roméo LeBlanc Airport", new GeoPoint(46.1122, -64.6786));

    public static readonly PickUpPlace Ciampino = PickUpPlace.Airport("CIA", "Ciampino Airport", new GeoPoint(41.7994, 12.5949));

    /// <summary>A car <paramref name="km"/> kilometres north of Rome's centre, for a 4-day rental in USD.</summary>
    public static CarOffer Car(
        int rank, decimal total, string model = "Fiat 500", string company = "Avis", string size = "Mini", double? rating = 8.0, double km = 1) => new(
        $"bk:{rank}",
        model,
        "or similar",
        size,
        "https://photo/1.png",
        Gearbox.Manual,
        4,
        3,
        1,
        true,
        "Unlimited km",
        true,
        "Like for like",
        new RentalCompany(company, null, rating, 10),
        new PickUpPoint($"{company} desk", PickUpType.Downtown, null, new GeoPoint(41.89 + (km / 111.2), 12.51), 0),
        CarPrice.For(total, null, 4, "USD", payNow: false)!,
        true,
        new Money(300, "EUR"),
        null,
        Sponsored: false,
        rank,
        null);
}
