using Pamplemousse.Application.Abstractions;
using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Destinations;
using Pamplemousse.Domain.Places;
using Pamplemousse.Domain.TravelEvents;

namespace Pamplemousse.Application.Tests.Destinations;

/// <summary>Hand-written providers, so handlers are tested with no HTTP host and no internet.</summary>
internal sealed class Fakes : IReferenceData, IWeatherProvider, IExchangeRateProvider, IHolidayProvider, IAdvisoryProvider, IEventProvider
{
    public static readonly Destination Rome = new("3169070", "Rome", "Lazio", "IT", new GeoPoint(41.89, 12.51), 2_318_895, "Europe/Rome");
    public static readonly Country Italy = new("IT", "Italy", "Italian Republic", "Rome", "Europe", null, [new CurrencyInfo("EUR", "Euro", "€")], ["Italian"], "+39", null);

    public ExchangeRate? Rate { get; set; } = new("USD", "EUR", 0.89m, new DateOnly(2027, 4, 1), "test");
    public Exception? WeatherError { get; set; }
    public Exception? EventsError { get; set; }

    public IReadOnlyList<Country> Countries => [Italy];
    public IReadOnlyList<Destination> SearchDestinations(string query, int limit) => [Rome];
    public Destination? FindDestination(string id) => string.Equals(id, Rome.Id, StringComparison.Ordinal) ? Rome : null;
    public Country? FindCountry(string code) => string.Equals(code, "IT", StringComparison.Ordinal) ? Italy : null;
    public VisaRequirement? FindVisa(string passportCountry, string destinationCountry) => new(passportCountry, destinationCountry, "visa required", null);

    public Task<WeatherReport> GetAsync(GeoPoint location, DateOnly firstDay, DateOnly lastDay, CancellationToken ct) =>
        WeatherError is null ? Task.FromResult(new WeatherReport(WeatherKind.Forecast, [])) : Task.FromException<WeatherReport>(WeatherError);

    public Task<ExchangeRate?> GetAsync(string baseCurrency, string quoteCurrency, CancellationToken ct) => Task.FromResult(Rate);

    Task<IReadOnlyList<Holiday>> IHolidayProvider.GetAsync(string countryCode, DateOnly firstDay, DateOnly lastDay, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Holiday>>([]);

    public Task<TravelAdvisory?> GetAsync(Country country, CancellationToken ct) => Task.FromResult<TravelAdvisory?>(null);

    public Task<IReadOnlyList<TravelEvent>> SearchAsync(Destination destination, DateOnly firstDay, DateOnly lastDay, int limit, CancellationToken ct) =>
        EventsError is null ? Task.FromResult<IReadOnlyList<TravelEvent>>([]) : Task.FromException<IReadOnlyList<TravelEvent>>(EventsError);
}

internal sealed class FakePlaces(PlaceCategory category, Exception? error = null) : IPlaceProvider
{
    public string Source => "otm";
    public PlaceCategory Category => category;
    public int Calls { get; private set; }

    public Task<IReadOnlyList<Place>> SearchAsync(Destination destination, int limit, CancellationToken ct)
    {
        Calls++;
        return error is null
            ? Task.FromResult<IReadOnlyList<Place>>([new Place("otm:1", "Colosseum", category, destination.Location, null, null, null, null, null, null, null, null, [], "test")])
            : Task.FromException<IReadOnlyList<Place>>(error);
    }

    public Task<Place?> GetAsync(string providerId, CancellationToken ct) =>
        Task.FromResult<Place?>(new Place($"otm:{providerId}", "Colosseum", category, Fakes.Rome.Location, null, null, null, null, null, null, null, null, [], "test"));
}

/// <summary>A clock frozen on 27 Oct 2026.</summary>
internal sealed class FixedClock : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);
}
