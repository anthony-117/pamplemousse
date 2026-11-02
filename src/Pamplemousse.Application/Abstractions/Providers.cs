using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Destinations;
using Pamplemousse.Domain.Flights;
using Pamplemousse.Domain.Places;
using Pamplemousse.Domain.TravelEvents;

namespace Pamplemousse.Application.Abstractions;

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
    /// <summary>Latest rate from <paramref name="baseCurrency"/> to <paramref name="quoteCurrency"/>; null when the source does not publish the pair.</summary>
    Task<ExchangeRate?> GetAsync(string baseCurrency, string quoteCurrency, CancellationToken ct);
}

public interface IHolidayProvider
{
    Task<IReadOnlyList<Holiday>> GetAsync(string countryCode, DateOnly firstDay, DateOnly lastDay, CancellationToken ct);
}

public interface IAdvisoryProvider
{
    /// <summary>Current advisory for <paramref name="country"/>; null when none exists.</summary>
    Task<TravelAdvisory?> GetAsync(Country country, CancellationToken ct);
}

/// <summary>Source of places for one category.</summary>
public interface IPlaceProvider
{
    /// <summary>Prefix used in place ids, e.g. "otm" or "wd".</summary>
    string Source { get; }

    PlaceCategory Category { get; }

    Task<IReadOnlyList<Place>> SearchAsync(Destination destination, int limit, CancellationToken ct);

    Task<Place?> GetAsync(string providerId, CancellationToken ct);
}

/// <summary>Recent flight prices for a search, cheapest first.</summary>
public interface IFlightProvider
{
    Task<FlightResults> SearchAsync(FlightSearch search, CancellationToken ct);
}

/// <summary>The fares found, a link to search the booking site for the same trip, and anything the traveller should know.</summary>
public sealed record FlightResults(IReadOnlyList<FlightOffer> Offers, string SearchUrl, IReadOnlyList<string> Warnings);

/// <summary>Maps a place to the nearest city with a real airport.</summary>
public interface IFlightCities
{
    /// <summary>The nearest flight city within range; null when there is none.</summary>
    CityMatch? FindNearest(GeoPoint location);
}

public sealed record CityMatch(FlightCity City, double DistanceKm);

public interface IEventProvider
{
    Task<IReadOnlyList<TravelEvent>> SearchAsync(Destination destination, DateOnly firstDay, DateOnly lastDay, int limit, CancellationToken ct);
}
