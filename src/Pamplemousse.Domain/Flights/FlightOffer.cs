using Pamplemousse.Domain.Common;

namespace Pamplemousse.Domain.Flights;

public enum TripType
{
    RoundTrip,
    OneWay,
}

/// <summary>How close a fare is to the dates the traveller asked for.</summary>
public enum DateMatch
{
    Exact,

    /// <summary>Within 3 days, on both legs of a round trip.</summary>
    Nearby,

    /// <summary>Another date in the same months as the requested dates.</summary>
    SameMonth,

    /// <summary>A round trip departing in the requested month and returning on any date, so its length can differ a lot.</summary>
    AnyReturn,
}

/// <summary>One priced option for a route and dates. The price is recent, not live or guaranteed.</summary>
public sealed record FlightOffer(
    string Id,
    string Airline,
    string? FlightNumber,
    string OriginAirport,
    string DestinationAirport,
    DateTimeOffset DepartureAt,
    DateTimeOffset? ReturnAt,
    int Stops,
    int? ReturnStops,
    int? DurationMinutes,
    Money Price,
    string BookingUrl,
    DateMatch DateMatch,
    string Source);

/// <summary>A city with a real airport, as the flight source knows it. <see cref="Code"/> is its IATA city code, e.g. ROM.</summary>
public sealed record FlightCity(string Code, string Name, string CountryCode, GeoPoint Location);
