# Data Model: Flight prices for a trip

Nothing is stored in the database (FR-016). These live in `Pamplemousse.Domain/Flights/` unless noted.

## FlightSearch (value object)

What the traveller asked for. Only its factory creates one, so an instance is always valid.

| Field | Type | Rule |
|---|---|---|
| OriginCode | string | Flight code of the departure city |
| DestinationCode | string | MUST differ from the origin, otherwise `DomainException` on field `origin` |
| Dates | `DateRange` | Existing trip date rules |
| TripType | enum `RoundTrip`, `OneWay` | Defaults to `RoundTrip`. For one way, the return date is ignored |
| DirectOnly | bool | Defaults to false |
| Currency | string | 3 letters, defaults to `USD`, otherwise `DomainException` on field `currency` |

Same-city check: two different TravelGenie cities can map to one flight code (Byblos and Beirut are both `BEY`). That also counts as the same city, and the error says so.

## FlightOffer (entity-like record)

| Field | Type | Notes |
|---|---|---|
| Id | string | `tp:` + a stable hash of the fields below, as in the `{source}:{id}` convention |
| Airline | string | Name; the code if the name is unknown |
| FlightNumber | string? | Airline code + number, e.g. `FR166`, when known |
| OriginAirport | string | e.g. `BVA`: it may be far from the city |
| DestinationAirport | string | e.g. `FCO` |
| DepartureAt | DateTimeOffset | |
| ReturnAt | DateTimeOffset? | Null for one way |
| Stops | int | Outbound stops |
| ReturnStops | int? | Null for one way |
| DurationMinutes | int? | When known; a round trip's total |
| Price | `Money` | Existing value object; MUST be above zero, otherwise the fare is left out |
| BookingUrl | string | Carries the marker when configured |
| DateMatch | enum `Exact`, `Nearby`, `SameMonth` | How close to the requested dates |
| Source | string | `Travelpayouts` |

Identity for duplicates: airline + flight number + departure + return.

Order: cheapest first. A response holds one `DateMatch` level only: the first of exact, nearby, same month that has fares.

## FlightCity

A city with a flightable airport, from the embedded list: `Code`, `Name`, `CountryCode`, `Location` (`GeoPoint`).

## CityMatch (Application response)

Which flight city served a TravelGenie city: `City` (`FlightCity`), `DistanceKm`, and `IsNearby`, true when the flight city is a different place from the one asked for. Used to meet FR-014.

## New interfaces (`Pamplemousse.Application/Abstractions`)

```csharp
public interface IFlightProvider
{
    Task<FlightResults> SearchAsync(FlightSearch search, CancellationToken ct);
}

public interface IFlightCities
{
    /// Nearest flight city within range; null when there is none.
    CityMatch? FindNearest(GeoPoint location);
}
```

`FlightResults` holds the fares plus, when the partner marker is missing, the warning for Story 2. The provider works out the date level, because the search strategy depends on the source.

## State

There are no state changes. Each search is read-only, and results expire from the cache after about an hour.
