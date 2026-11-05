# Research: Car rentals for a trip

Checked on 2026-11-05 with the real RapidAPI key, 2 requests: Rome car look-up, and one search at Rome city centre,
4–8 Dec 2026, 10:00 to 10:00, driver 30, USD. Booking.com's site was checked in a browser for the same search.
Earlier evaluation: OneDrive `01-research/07-booking-com18-api-evaluation.md` §4.

## R-1: Endpoints

- **Decision**: two endpoints of `booking-com18.p.rapidapi.com`:

  | Endpoint | Used for | Cost |
  |---|---|---|
  | `/car/auto-complete?query={city}` | The city's airports (name, IATA code, coordinates) | 1 request per city, stored, checked monthly |
  | `/car/search?pickUpId=&dropOffId=&pickUpDate=&pickUpTime=&dropOffDate=&dropOffTime=&driverAge=&currencyCode=` | All cars for the search, at most 500 | 1 request per search |

- **Evidence**: the search took 23 s (30 s in the earlier evaluation) and returned 2.28 MB with 500 cars from
  15 companies. There is no page parameter: one call returns everything.
- **Not used**: `/car/detail` and `/car/packages`. The search already has every field the spec needs.

## R-2: Place IDs are coordinates

- **Decision**: build `pickUpId` ourselves as base64 of `{"latitude":"…","longitude":"…"}`. The city centre uses
  the destination's own position, so it needs no look-up.
- **Evidence**: every ID from the look-up decodes to exactly that JSON, e.g. Rome City Centre →
  `{"latitude":"41.89814758300781","longitude":"12.481033325195312"}`. The search with that ID returned Rome cars.
- **Risk**: an undocumented format. If it changes, the look-up's own IDs still work; the provider keeps the
  source's ID for airports, and the centre falls back to the look-up's "city" or "district" entry.

## R-3: Airports for a city

- **Decision**: from the look-up, keep entries with `type = "airport"`, an `iata_code`, and coordinates within
  50 km of the destination. Order by distance.
- **Evidence**: "Rome" returned Fiumicino (FCO), Ciampino (CIA), and unrelated airports such as Moncton's
  "Roméo LeBlanc" (YQM) and one in the Bahamas. The distance rule keeps only FCO and CIA.

## R-4: Price (FR-004)

- **Decision**: total = `pricing_info.drive_away_price_before` when present, otherwise `drive_away_price`.
  Price per day = total ÷ rental days, rounded to 2 decimals. Rental days = days between the first and last day,
  at least 1.
- **Evidence** (site in USD, not signed in):

  | Car | Site | `drive_away_price` | `…_before` | Used | Gap |
  |---|---|---|---|---|---|
  | Budget Fiat 600 | $77 | 68.32 | 76.76 | 76.76 | 0% |
  | Avis Fiat 600 | $81 | 71.89 | 80.77 | 80.77 | 0% |
  | Avis Cupra Formentor | $101 | 89.69 | 100.77 | 100.77 | 0% |
  | Europcar Jeep Compass | $94 | 96.10 | — | 96.10 | +2% |
  | Europcar Peugeot 2008 | $88 | 89.33 | — | 89.33 | +2% |
  | Europcar Audi Q3 | $99 | 100.97 | — | 100.97 | +2% |
  | Locauto Toyota Aygo | $117 | 118.99 | — | 118.99 | +2% |
  | Drivalia Ford Tourneo | $375 | 388.70 | — | 388.70 | +4% |
  | Centauro Kia Picanto | $65, shown as $55 with a 15% site discount | 58.22 | 68.50 | 68.50 | +5% |

  8 of 9 within 4%; all within 5% (SC-003). The member price (`drive_away_price` when a "before" exists) is
  about 11% lower and is what Booking.com shows to signed-in Genius members.
- **Rule**: a missing or non-positive price drops the car.

## R-5: Fields (FR-003)

| Need | Field |
|---|---|
| Model, "or similar" | `vehicle_info.v_name`, `group_or_similar` |
| Size class | `vehicle_info.group` (Mini, Economy, Compact, Intermediate, Standard, Full-size, Premium, Special, Luxury) |
| Photo | `vehicle_info.image_url` |
| Gearbox, seats, doors, air-con | `transmission`, `seats`, `doors`, `aircon` |
| Bags | `content.vehicleSpecs[icon=BAGS_LARGE].text` ("3 checked bags"), 498 of 500. `vehicle_info.suitcases` is always 0 and is ignored. Only checked bags are known, so "small bags" in FR-003 is dropped |
| Mileage, fuel policy | `mileage`, `unlimited_mileage`, `fuel_policy` |
| Company | `supplier_info.name`, `logo_url`; rating `rating_info.average`, `no_of_ratings` (485 of 500 in Rome) |
| Pick-up place | `route_info.pickup.name`, `location_type` (IN_TERMINAL, SHUTTLE_BUS, CAR_RENTAL_CENTRE, TRAINSTATION, DOWNTOWN, …), `address`, `latitude`, `longitude` |
| Payment, cancellation | `pricing_info.pay_when` (PAY_NOW, PAY_LOCAL), `vehicle_info.free_cancellation` |
| Deposit, damage excess | `pricing_info.fee_breakdown.known_fees[type=DEPOSIT / DAMAGE_EXCESS]` with their own `currency` (EUR in Rome) |
| Sponsored | `vehicle_info.badges.sponsored` |
| ID | `vehicle_info.v_id` (unique across the 500) |

- **Duplicates**: 80 of 500 share model, company and pick-up place. Keep the lowest price (spec edge case).

## R-6: Booking link (FR-010)

- **Decision**: `https://cars.booking.com/search-results?location=&dropLocation=&locationName={place}&dropLocationName={place}`
  `&coordinates={lat},{lng}&dropCoordinates={lat},{lng}&ftsType=D&dropFtsType=D&driversAge={age}`
  `&puDay=&puMonth=&puYear=&puHour=10&puMinute=0&doDay=&doMonth=&doYear=&doHour=10&doMinute=0&prefcurrency={cur}&preflang=en`.
  `ftsType` is `A` for airports. `{place}` is the airport name, or the city name for the centre.
- **Evidence**: this is the URL Booking.com's own form produced; opened in a browser it showed Rome cars for the
  same dates and age. The source's `forward_url` (`DriverExtras.do?…vehicleInfo.vehicle.id=…`) returns 404, and the
  older `SearchResults.do?location=` keeps the dates but loses the place.
- **Confirmed** 5 Nov 2026: `ftsType=A` opens Booking.com's Fiumicino search with the right dates, times and age.

## R-7: Slow, large responses

- **Decision**: the car client has its own 40 s timeout and **no** retries: the Aspire standard resilience
  handler (10 s per attempt, 30 s total, retries on 5xx and 429) is removed for this client, since each retry
  costs a request and a 30 s wait. The response is read as a stream into `JsonDocument`.
- **Lock**: held for 60 s (hotels: 30 s); a second traveller asking the same search waits up to 40 s for the
  first one's result instead of 5 s.
- **Caddy**: its default proxy timeouts do not cut a 40 s request.

## R-8: Storage size

- **Decision**: one Firestore document per call to the source, in `carSnapshots`: the search, the time, the 500
  cars as typed fields (about 280 KB), and the source's full response gzip-compressed (about 130 KB) for FR-008b.
  About 410 KB, under Firestore's 1 MB document limit. Redis holds the typed cars for 1 day under `cars:{search}`.
- **Rationale**: storing each car as its own document, as hotels do, would be 500 writes per search; the free
  tier allows 20,000 a day, so 40 searches. Cars don't exist apart from a search (no "open this car" call), so a
  per-car collection buys nothing.
- **Airports**: stored on the existing `cities/{geonameId}` document, field `carAirports` with `checkedAt`, by a
  merge write that leaves the hotel fields alone.
