# Data Model: Hotel prices for a trip

## Domain types

| Type | Fields | Rules |
|---|---|---|
| `HotelSearch` | DestinationId, CheckIn, CheckOut, Adults, Currency | Dates use the API's existing rules (not in the past, check-out after check-in, at most 30 nights); Adults 1–8, default 2; Currency 3 letters, default USD. `Key` = `{destinationId}:{checkIn}:{checkOut}:{adults}:{currency}` |
| `HotelFilters` | Sort, MinStars, MinReviewScore, MinPrice, MaxPrice, StayTypes, FreeCancellation, Page | Sort is one of recommended (default), price, review, stars, distance. Page ≥ 1. Not part of the search key, so changing them never calls the source |
| `Hotel` | Id (source ID), Name, StayType?, Address?, Stars?, ReviewScore?, ReviewCount?, Location, PhotoUrl, BookingPageUrl?, FreeCancellation?, Facilities, RawSearch, RawDetails?, SearchUpdatedAt, DetailsUpdatedAt? | Missing stars or reviews stay empty, never 0. Details older than 7 days are fetched again on opening |
| `HotelOffer` | Hotel, Total, PerNight, Currency, DistanceKm, NearCity, Rank | Total > 0; PerNight = Total ÷ nights; NearCity when DistanceKm > `NearCityRadiusKm` |
| `PriceSnapshot` | Id, SearchKey, FetchedAt, Prices: list of (HotelId, Total, Rank) | One per call to the source; never changed afterwards |
| `CityMapping` | DestinationId, LocationId, SourceName, MatchedAt | The source name is shown when it differs from the destination's name |

## Firestore collections

Agreed with the owner on 2026-11-03.

| Collection / document ID | Contents | Written |
|---|---|---|
| `cities/{destinationId}` | `CityMapping` | Once per city |
| `hotels/{bk_<sourceId>}` | `Hotel`: typed fields below, plus `rawSearch` and `rawDetails` as JSON text | Each search updates search fields; details on opening, at most weekly |
| `hotels/{id}/prices/{fetchedAt}` | One price for that hotel: search key, dates, adults, currency, total, per night, rank | Each call to the source, one per listed hotel |
| `priceSnapshots/{auto ID}` | `PriceSnapshot`: the whole search's prices | Each call to the source |

### `cities/{destinationId}`

| Field | Example |
|---|---|
| `locationId` | `"eyJjaXR5X25hbWUi..."` |
| `ufi` | `-126693` (the source's city number, shared by hotels, cars and attractions) |
| `sourceName` | `"Rome"` |
| `matchedAt` | timestamp |

### `hotels/{bk_<sourceId>}`

| Group | Fields |
|---|---|
| Identity | `sourceId`, `name`, `stayType`, `address`, `district`, `cityUfi`, `countryCode`, `timezone` |
| Position | `location` (geopoint), `distanceToCentreKm`, `isCityCentre` |
| Ratings | `stars`, `starsEstimated`, `reviewScore`, `reviewCount`, `reviewWord`, `breakfastScore`, `wifiScore` |
| Photos | `photoUrls` |
| Stay rules | `checkInFrom`, `checkInUntil`, `checkOutFrom`, `checkOutUntil`, `breakfastIncluded`, `familyFriendly`, `freeCancellation` |
| Facilities | `facilities`, `languages` |
| Booking | `bookingPageUrl` |
| Last known price | `lastPrice`: total, per night, currency, check-in, check-out, adults, `fetchedAt` |
| Raw data | `rawSearch`, `rawDetails` (the source's full answers as JSON text) |
| Times | `searchUpdatedAt`, `detailsUpdatedAt` |

Rooms and their prices depend on the dates, so they stay inside `rawDetails`; only `freeCancellation` and
`breakfastIncluded` are copied out. Search results live in Redis only; there is no `searches` collection.

### `priceSnapshots/{auto ID}`

`searchKey`, `destinationId`, `checkIn`, `checkOut`, `adults`, `currency`, `fetchedAt`, `page`, and `prices`:
a list of `hotelId`, `total`, `perNight`, `rank`, `freeCancellation`.

The newest snapshot for a search is found with `where searchKey == X order by fetchedAt desc limit 1`,
which needs one composite index (`searchKey` ascending, `fetchedAt` descending), listed in `firestore.indexes.json`.

## Redis keys

| Key | Value | Lifetime |
|---|---|---|
| `hotels:{searchKey}` | Hotel IDs in source order with prices and `fetchedAt` | 12 hours |
| `lock:{searchKey}` | `1` | 30 seconds |
| `quota:{yyyy-MM}` | Requests made to the Booking COM API this month (shared by all its features) | 35 days |

## Settings (`Hotels` section, appsettings)

| Setting | Default | Meaning |
|---|---|---|
| `PagesPerSearch` | 1 | Result pages fetched per new search |
| `DetailsForEveryHotel` | false | Fetch details for every listed hotel, not only on opening |
| `ExtraPagesWhenFiltered` | 0 | Extra pages to fetch when filters leave fewer than 20 hotels |
| `MonthlyRequestLimit` | 530 | Plan limit; new calls stop at 95% |
| `NearCityRadiusKm` | 10 | Beyond this, a hotel is marked near the city |
| `ResultsTtlHours` | 12 | Redis lifetime of a search's results (ADR 0017) |
