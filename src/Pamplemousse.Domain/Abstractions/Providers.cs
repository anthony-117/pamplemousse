using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Destinations;
using Pamplemousse.Domain.Events;
using Pamplemousse.Domain.Places;

namespace Pamplemousse.Domain.Abstractions;

/// <summary>Bundled reference data: cities, countries and visa rules. Works offline.</summary>
public interface IReferenceData
{
    IReadOnlyList<Destination> SearchDestinations(string query, int limit);

    Destination? FindDestination(string id);

    IReadOnlyList<Country> Countries { get; }

    Country? FindCountry(string code);

    VisaRequirement? FindVisa(string passportCountry, string destinationCountry);
}

public interface IWeatherProvider
{
    Task<WeatherReport> GetAsync(GeoPoint location, DateOnly firstDay, DateOnly lastDay, CancellationToken ct);
}

public interface IExchangeRateProvider
{
    /// <returns>null when the source does not publish this currency pair.</returns>
    Task<ExchangeRate?> GetAsync(string baseCurrency, string quoteCurrency, CancellationToken ct);
}

public interface IHolidayProvider
{
    Task<IReadOnlyList<Holiday>> GetAsync(string countryCode, DateOnly firstDay, DateOnly lastDay, CancellationToken ct);
}

public interface IAdvisoryProvider
{
    /// <returns>null when no advisory exists for the country.</returns>
    Task<TravelAdvisory?> GetAsync(Country country, CancellationToken ct);
}

/// <summary>Source of places for one category (attractions and restaurants both come from OpenTripMap today).</summary>
public interface IPlaceProvider
{
    /// <summary>Prefix used in place ids, e.g. "otm" or "gp".</summary>
    string Source { get; }

    PlaceCategory Category { get; }

    Task<IReadOnlyList<Place>> SearchAsync(Destination destination, int limit, CancellationToken ct);

    Task<Place?> GetAsync(string providerId, CancellationToken ct);
}

public interface IEventProvider
{
    Task<IReadOnlyList<TravelEvent>> SearchAsync(Destination destination, DateOnly firstDay, DateOnly lastDay, int limit, CancellationToken ct);
}

/// <summary>Thrown when a provider needs an API key that is not set.</summary>
public sealed class ProviderNotConfiguredException(string provider, string environmentVariable)
    : InvalidOperationException($"{provider} is not configured. Set {environmentVariable} in the environment or .env file.")
{
    public string Provider { get; } = provider;

    public string EnvironmentVariable { get; } = environmentVariable;
}
