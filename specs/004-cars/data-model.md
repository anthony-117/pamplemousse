# Data Model: Car rentals for a trip

All types live in `Pamplemousse.Domain/Cars/` unless noted. Money is `decimal`, rounded to 2 places.

## CarSearch

What was asked. Created by `CarSearch.Create`, which applies the rules.

| Field | Type | Rule |
|---|---|---|
| `DestinationId` | string | an existing destination |
| `Place` | `PickUpPlace` | `centre` or one of the city's airports |
| `Dates` | `DateRange` | `DateRange.ForTrip`, as hotels and flights |
| `Days` | int | days between the first and last day, at least 1 |
| `DriverAge` | int | 18–99, default 30, else `DomainException` on `driverAge` |
| `Currency` | string | 3 letters, default USD |
| `Key` | string | `{destination}:{place}:{from}:{to}:{age}:{currency}`, used for Redis, the lock and snapshots |

Pick-up and return times are fixed at 10:00 and are not part of the key.

## PickUpPlace

| Field | Type | Notes |
|---|---|---|
| `Id` | string | `centre`, or the IATA code (`FCO`) |
| `Kind` | `Centre` / `Airport` | |
| `Name` | string | "City centre" or the airport's name |
| `Iata` | string? | airports only |
| `Location` | `GeoPoint` | the destination's position for the centre |

## CarOffer

One car for one search.

| Field | Type | Notes |
|---|---|---|
| `Id` | string | `bk:{v_id}` |
| `Model`, `OrSimilar` | string | "Fiat 600", "or similar large car" |
| `Size` | string | the source's group, e.g. Intermediate |
| `PhotoUrl` | string? | |
| `Gearbox` | `Manual` / `Automatic` | |
| `Seats`, `Doors`, `Bags` | int? | `Bags` = checked bags from the specs text; null when unknown |
| `AirCon`, `UnlimitedMileage` | bool? | |
| `Mileage`, `FuelPolicy` | string? | the source's wording |
| `Company` | `RentalCompany(Name, LogoUrl, Rating, ReviewCount)` | rating 0–10 or null |
| `PickUp` | `PickUpPoint(Name, Type, Address, Location, DistanceKm)` | `Type`: Terminal, Shuttle, RentalCentre, TrainStation, Downtown, Other; distance from the destination |
| `Price` | `CarPrice(Total, PerDay, Currency, PayNow)` | `Total` is the normal price (research R-4) |
| `FreeCancellation` | bool? | |
| `Deposit`, `DamageExcess` | `Money(Amount, Currency)?` | in their own currency |
| `Sponsored` | bool | |
| `Rank` | int | the source's recommended order |
| `SourceUrl` | string? | the source's own per-car link, kept but not shown (it is 404 today) |

Rules: a car with no price or a price ≤ 0 is dropped; of cars with the same model, company and pick-up name,
only the cheapest is kept.

## CarFilters

From the query; `Apply` and `PageOf` work on a list of `CarOffer`, like `HotelFilters`.

| Field | Values |
|---|---|
| `Sort` | `recommended` (by `Rank`), `price`, `rating` (nulls last, then price) |
| `Sizes`, `Companies`, `PickUpTypes` | sets; empty means any |
| `Gearbox` | null, `manual`, `automatic` |
| `MinSeats`, `MinBags` | 1–9; a car with an unknown value doesn't match |
| `MaxPrice` | > 0, on `Total` |
| `FreeCancellation` | true keeps only cars with `FreeCancellation == true` |
| `Page` | ≥ 1, 20 per page |

`Options(offers)` returns the sizes, companies and pick-up types present in the whole result, with counts.

## CarSnapshot (Application storage record)

One call to the source; Firestore `carSnapshots/{searchKey}_{yyyyMMddHHmmss}`.

| Field | Type |
|---|---|
| `SearchKey`, `DestinationId`, `PlaceId`, `From`, `To`, `DriverAge`, `Currency` | as in `CarSearch` |
| `FetchedAt` | `DateTimeOffset` |
| `MayHaveMore` | bool, true when the source returned its cap of 500 |
| `Cars` | list of `CarOffer` (about 280 KB for 500) |
| `Raw` | bytes: the source's response, gzip (about 130 KB) |

Redis `cars:{searchKey}` holds `{FetchedAt, MayHaveMore, Cars}` for 1 day (`Cars:ResultsTtlHours` = 24).

## City airports

Added to the existing `cities/{geonameId}` document by a merge write:

| Field | Type |
|---|---|
| `carAirports` | list of `{iata, name, lat, lng, sourceId}` within 50 km, nearest first |
| `carAirportsCheckedAt` | timestamp; looked up again after 30 days |
