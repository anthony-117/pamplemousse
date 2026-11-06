using System.Text.Json;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Destinations;
using Pamplemousse.Domain.Hotels;

namespace Pamplemousse.Application.Tests.Hotels;

/// <summary>A hotel source that answers from a list, counts its calls, and can fail on demand.</summary>
internal sealed class FakeHotelProvider : IHotelProvider
{
    public List<FoundHotel> Hotels { get; } = [];

    public int TotalPages { get; set; } = 1;

    public Exception? Error { get; set; }

    public Hotel? Details { get; set; }

    public int CityCalls { get; private set; }

    public List<int> SearchedPages { get; } = [];

    public int DetailCalls { get; private set; }

    public string Source => "Booking.com";

    public Task<CityMapping?> FindCityAsync(Destination destination, CancellationToken ct)
    {
        CityCalls++;
        return Error is null
            ? Task.FromResult<CityMapping?>(new CityMapping(destination.Id, "loc-rome", -126693, "Rome", HotelFakes.Now))
            : Task.FromException<CityMapping?>(Error);
    }

    public Task<HotelResults> SearchAsync(CityMapping city, HotelSearch search, int page, CancellationToken ct)
    {
        SearchedPages.Add(page);
        if (Error is not null)
        {
            return Task.FromException<HotelResults>(Error);
        }

        IReadOnlyList<FoundHotel> onPage = [.. Hotels.Where(h => (h.Rank - 1) / 20 == page - 1)];
        return Task.FromResult(new HotelResults(onPage, TotalPages));
    }

    public Task<Hotel?> GetDetailsAsync(string hotelId, HotelSearch search, CancellationToken ct)
    {
        DetailCalls++;
        return Error is null ? Task.FromResult(Details is null ? null : Details with { Id = hotelId }) : Task.FromException<Hotel?>(Error);
    }

    public string SearchUrl(Destination destination, HotelSearch search) => "https://book/search";

    public string BookingUrl(Hotel hotel, HotelSearch search) => $"https://book/{hotel.Id}?adults={search.Adults}";
}

/// <summary>Cities, hotels and price history in memory, with an optional failure for every call.</summary>
internal sealed class FakeHotelData : ICityMappingStore, IHotelStore, IPriceHistory
{
    public Dictionary<string, CityMapping> Cities { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, Hotel> Hotels { get; } = new(StringComparer.Ordinal);

    public List<PriceSnapshot> Snapshots { get; } = [];

    public Exception? Error { get; set; }

    public Task<CityMapping?> GetAsync(string destinationId, CancellationToken ct) => Run(() => Cities.GetValueOrDefault(destinationId));

    public Task SaveAsync(CityMapping mapping, CancellationToken ct) => Run(() => Cities[mapping.DestinationId] = mapping);

    Task<Hotel?> IHotelStore.GetAsync(string id, CancellationToken ct) => Run(() => Hotels.GetValueOrDefault(id));

    public Task<IReadOnlyList<Hotel>> GetManyAsync(IReadOnlyCollection<string> ids, CancellationToken ct) =>
        Run<IReadOnlyList<Hotel>>(() => [.. ids.Select(Hotels.GetValueOrDefault).OfType<Hotel>()]);

    public Task SaveManyAsync(IReadOnlyList<Hotel> hotels, CancellationToken ct) => Run(() =>
    {
        foreach (var h in hotels)
        {
            Hotels[h.Id] = h;
        }

        return true;
    });

    public Task AddAsync(PriceSnapshot snapshot, CancellationToken ct) => Run(() =>
    {
        Snapshots.Add(snapshot);
        return true;
    });

    public Task<IReadOnlyList<PriceSnapshot>> GetLatestAsync(string searchKey, CancellationToken ct) =>
        Run<IReadOnlyList<PriceSnapshot>>(() => [.. Snapshots.Where(s => string.Equals(s.SearchKey, searchKey, StringComparison.Ordinal)).GroupBy(s => s.Page).Select(g => g.MaxBy(s => s.FetchedAt)!).OrderBy(s => s.Page)]);

    private Task<T> Run<T>(Func<T> action) => Error is null ? Task.FromResult(action()) : Task.FromException<T>(Error);
}

/// <summary>A cache that keeps values as JSON, like the real one, so anything that would not survive Redis fails here too.</summary>
internal sealed class FakeSearchCache : ISearchCache
{
    public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

    public Task<T?> GetAsync<T>(string key, CancellationToken ct)
        where T : class => Task.FromResult(Values.TryGetValue(key, out var json) ? JsonSerializer.Deserialize<T>(json, JsonSerializerOptions.Web) : null);

    public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct)
        where T : class
    {
        Values[key] = JsonSerializer.Serialize(value, JsonSerializerOptions.Web);
        return Task.CompletedTask;
    }
}

internal sealed class FakeSearchLock : ISearchLock
{
    private readonly Lock _sync = new();

    public bool Taken { get; set; }

    /// <summary>How many locks were freed; the last step of a search, so a test can wait for one that runs on its own.</summary>
    public int Released { get; private set; }

    public Task<IAsyncDisposable?> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct) =>
        Task.FromResult<IAsyncDisposable?>(Taken ? null : new Handle(this));

    private sealed class Handle(FakeSearchLock owner) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            lock (owner._sync)
            {
                owner.Released++;
            }

            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>A clock that starts on 3 Nov 2026 and can be moved forward.</summary>
internal sealed class MovableClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = HotelFakes.Now;

    public override DateTimeOffset GetUtcNow() => Now;
}

internal static class HotelFakes
{
    public static readonly DateTimeOffset Now = new(2026, 11, 3, 9, 0, 0, TimeSpan.Zero);

    public static readonly HotelSearch Search = HotelSearch.Create(
        "3169070", DateRange.ForTrip(new DateOnly(2027, 4, 10), new DateOnly(2027, 4, 14), new DateOnly(2026, 11, 3)), adults: null, currency: null);

    /// <summary>A hotel <paramref name="km"/> kilometres north of Rome's centre.</summary>
    public static FoundHotel Found(int rank, decimal total, int? stars = null, double? review = null, double km = 1)
    {
        var price = HotelPrice.ForStay(total, Search, Now)!;
        var hotel = new Hotel
        {
            Id = $"bk:{rank}",
            Name = $"Hotel {rank}",
            Location = new GeoPoint(41.89 + (km / 111.2), 12.51),
            Stars = stars,
            ReviewScore = review,
            PhotoUrls = ["https://photo/1.jpg"],
            LastPrice = price,
            RawSearch = "{}",
            SearchUpdatedAt = Now,
        };
        return new FoundHotel(hotel, price, rank);
    }
}
