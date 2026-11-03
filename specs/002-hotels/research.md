# Research: Hotel prices for a trip

Checked on 2026-11-03 with the real RapidAPI key, about 40 requests in all. Full evaluation of the API:
OneDrive `01-research/07-booking-com18-api-evaluation.md`.

## R-1: Which endpoints

- **Decision**: three endpoints of `booking-com18.p.rapidapi.com`:

  | Endpoint | Used for | Cost |
  |---|---|---|
  | `/stays/auto-complete?query=` | City name to the source's location `id` | 1 request per new city, then stored |
  | `/stays/search?locationId=&checkinDate=&checkoutDate=&adults=&units=metric&temperature=c&currencyCode=` | Hotels and prices, 20 per page (`page` for more) | 1 request per page |
  | `/stays/detail?hotelId=&checkinDate=&checkoutDate=&adults=&currencyCode=` | Everything about one hotel, including its Booking.com `url` | 1 request per hotel |

- **Headers**: `x-rapidapi-key` and `x-rapidapi-host`. The key never appears in a URL or a log.
- **Rationale**: ADR 0016. Search results are live and match Booking.com.

## R-2: Total price

- **Decision**: total = `priceBreakdown.grossPrice.value` + `priceBreakdown.excludedPrice.value` (missing excluded price counts as 0). Price per night = total ÷ nights, rounded to 2 decimals.
- **Evidence**: both values are for the whole stay; Paris and Rome samples matched the booking site within a few percent once excluded taxes were added. In details, `composite_price_breakdown.all_inclusive_amount` gives the same total.
- **Rule**: a total of zero or less, or missing, drops the hotel (spec edge case).

## R-3: Room fits the adults (FR-003)

- **Decision**: always send `adults` in the search. When details are fetched, also check that the priced block's `max_occupancy` is at least the adults, and drop the price if not.
- **Evidence**: the search with `adults=2` returned blocks for two (`5901510_88984935_2_1_0` for hotel 59015); its details show `max_occupancy` 2 and `nr_adults` 2. The cheapest one-person rooms seen earlier only appeared with `adults=1`.
- **Risk**: the search does not state occupancy itself. SC-002 is checked by hand on 10 hotels in the quickstart.

## R-4: Which fields exist where

| Need | Search result | Details |
|---|---|---|
| Name, photo, stars, review score and count, position | Yes (`photoUrls` are 60 px; larger sizes by changing the size in the URL, checked in quickstart) | Yes |
| Distance from centre | No, computed from lat/lng and the destination's position | `distance_to_cc` |
| Type of stay | No | `accommodation_type_name` |
| Free cancellation | No | `block[].refundable`, `refundable_until` |
| Facilities, policies, rooms, breakfast | No | Yes |
| Booking.com link | No | `url` |

- **Decision**: search fields are stored on every search; details are stored when a hotel is opened (free plan) or for every listed hotel when the setting is on (FR-008c). Filters that need details only cover hotels with details, and the response says how many.

## R-5: Booking link (FR-010)

- **Decision**: details `url` + `?checkin=YYYY-MM-DD&checkout=YYYY-MM-DD&group_adults=N&no_rooms=1&group_children=0&selected_currency=XXX`.
- **Evidence**: details returned `https://www.booking.com/hotel/fr/mirific.html` for hotel 59015. The query parameters are Booking.com's public ones.
- **Open**: a link built from the hotel ID alone (`searchresults.html?dest_id={id}&dest_type=hotel`) would save the details request. To be tried by hand in the quickstart; if it reliably opens the right hotel, it becomes the link for hotels without details.

## R-6: Firestore from .NET

- **Decision**: `Google.Cloud.Firestore` with `FirestoreDbBuilder`. Project from `FIREBASE_PROJECT_ID`; credentials from the file in `GOOGLE_APPLICATION_CREDENTIALS`. With `FIRESTORE_EMULATOR_HOST` set, it talks to the emulator (`EmulatorDetection.EmulatorOrProduction`).
- **Storing every field (FR-008b)**: each hotel keeps typed fields for sorting and filtering, plus the raw search and details JSON as strings (`rawSearch`, `rawDetails`). Details are about 26 KB, far below Firestore's 1 MB document limit.
- **Alternatives rejected**: storing the raw JSON as nested Firestore maps (field names and types from the source can change and break reads); Postgres, already in Aspire (ADR 0017 chose Firestore).

## R-7: Redis lock and counter

- **Decision**: add `Aspire.StackExchange.Redis` for `IConnectionMultiplexer`, on the same `cache` resource.
  - Lock: `SET lock:{search} 1 NX EX 30`; others wait up to 5 s for the result in `hotels:{search}`.
  - Counter: `INCR quota:{yyyy-MM}` before each source call, expires after 35 days. At 95% of `MonthlyRequestLimit` (default 530), no new call.
- **Rationale**: `IDistributedCache` can't do "set if absent" or atomic counting.
- **Without Redis** (tests and runs without Aspire): in-memory lock and counter, as the cache already falls back to memory.

## R-8: Sorting and filtering (FR-008a)

- **Decision**: load the search's stored hotels (20 per page fetched, so at most a few hundred) and sort, filter and page them in memory in Application.
- **Rationale**: Firestore needs a composite index for each mix of filters and allows a range filter on only a few fields; in-memory work over this little data is simpler and fast.
- **Using the source's own sort**: only when filters leave fewer than 20 hotels and the setting `ExtraPagesWhenFiltered` (default 0 on the free plan) allows fetching more pages.

## R-9: Nearby places (spec scenario 7)

- **Decision**: a hotel more than 10 km (setting `NearCityRadiusKm`) from the destination's position is marked `nearCity`.
- **Evidence**: the search returns hotels around the location ID; for small places the source widens the area itself.

## R-10: Tests with real Redis and the Firestore emulator

- **Decision**: `Testcontainers.Redis` and `Testcontainers.Firestore`, started once per test class. Docker 29 is installed locally; GitHub's `ubuntu-latest` runners include Docker.
- **AppHost**: when no service account is set, a Firestore emulator container (`gcr.io/google.com/cloudsdktool/google-cloud-cli:emulators`) runs and `FIRESTORE_EMULATOR_HOST` is passed to the API.
