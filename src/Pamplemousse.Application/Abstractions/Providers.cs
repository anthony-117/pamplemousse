using Pamplemousse.Domain.Cars;
using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Destinations;
using Pamplemousse.Domain.Flights;
using Pamplemousse.Domain.Hotels;
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

/// <summary>How many places of each kind are around a position (spec 007, research R-1). 500 means 500 or more.</summary>
public sealed record NearbyCounts(int Food, int Nightlife, int Nature);

/// <summary>Counts restaurants and cafés and bars and pubs within 1 km, and parks and natural sites within 2 km, of a position.</summary>
public interface INearbyPlaces
{
    Task<NearbyCounts> CountAsync(GeoPoint position, CancellationToken ct);
}

/// <summary>Recent flight prices for a search, cheapest first.</summary>
public interface IFlightProvider
{
    Task<FlightResults> SearchAsync(FlightSearch search, CancellationToken ct);
}

/// <summary>The fares found, and a link to search the booking site for the same trip.</summary>
public sealed record FlightResults(IReadOnlyList<FlightOffer> Offers, string SearchUrl);

/// <summary>Maps a place to the nearest city with a real airport.</summary>
public interface IFlightCities
{
    /// <summary>The nearest flight city within range; null when there is none.</summary>
    CityMatch? FindNearest(GeoPoint location);
}

public sealed record CityMatch(FlightCity City, double DistanceKm);

/// <summary>Live hotel prices (ADR 0016). Everything source-specific stays behind this interface (FR-015).</summary>
public interface IHotelProvider
{
    /// <summary>Shown as each hotel's source, e.g. "Booking.com".</summary>
    string Source { get; }

    /// <summary>The source's ids for the city nearest to <paramref name="destination"/>; null when the source knows none nearby.</summary>
    Task<CityMapping?> FindCityAsync(Destination destination, CancellationToken ct);

    /// <summary>One page of hotels with their prices, in the source's recommended order.</summary>
    Task<HotelResults> SearchAsync(CityMapping city, HotelSearch search, int page, CancellationToken ct);

    /// <summary>Everything the source says about one hotel for these dates; null when it doesn't know the hotel.</summary>
    Task<Hotel?> GetDetailsAsync(string hotelId, HotelSearch search, CancellationToken ct);

    /// <summary>The booking site's own search for the city and dates, for when no hotel can be shown.</summary>
    string SearchUrl(Destination destination, HotelSearch search);

    /// <summary>The hotel's page on the booking site with the dates and travellers filled in.</summary>
    string BookingUrl(Hotel hotel, HotelSearch search);
}

/// <summary>A page of hotels. <see cref="TotalPages"/> is how many pages the source has for the search.</summary>
public sealed record HotelResults(IReadOnlyList<FoundHotel> Hotels, int TotalPages);

/// <summary>A hotel with its search fields and price for the stay; <see cref="Rank"/> counts from 1 across pages.</summary>
public sealed record FoundHotel(Hotel Hotel, HotelPrice Price, int Rank);

/// <summary>Live rental cars (ADR 0016, spec 004). Everything source-specific stays behind this interface (FR-016).</summary>
public interface ICarProvider
{
    /// <summary>Shown as each car's source, e.g. "Booking.com".</summary>
    string Source { get; }

    /// <summary>Every airport the source finds for the destination's name, wherever it is; the caller keeps the nearby ones.</summary>
    Task<IReadOnlyList<PickUpPlace>> FindAirportsAsync(Destination destination, CancellationToken ct);

    /// <summary>All cars for the search, at most the source's cap, in its recommended order. Distances are left at 0.</summary>
    Task<CarResults> SearchAsync(CarSearch search, CancellationToken ct);

    /// <summary>The booking site's own car search for the same place, dates, times and driver age (FR-010).</summary>
    string SearchUrl(Destination destination, CarSearch search);
}

/// <summary>What one car search returned. <see cref="Raw"/> is the source's whole answer, kept for history (FR-008b).</summary>
public sealed record CarResults(IReadOnlyList<CarOffer> Cars, bool MayHaveMore, byte[] Raw);

public interface IEventProvider
{
    Task<IReadOnlyList<TravelEvent>> SearchAsync(Destination destination, DateOnly firstDay, DateOnly lastDay, int limit, CancellationToken ct);
}
