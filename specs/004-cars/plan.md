# Implementation Plan: Car rentals for a trip

**Branch**: `004-cars` | **Date**: 2026-11-05 | **Spec**: [spec.md](spec.md)

**Status**: Draft, waiting for the owner's approval

## Summary

Add `GET /api/v1/destinations/{id}/car-places` and `GET /api/v1/destinations/{id}/cars`. The API builds the
source's place ID from coordinates (the city centre, or an airport found once per city and stored), runs one
Booking COM car search for the dates and driver age, and returns the cars sorted, filtered and paged from that
stored result. It reuses what hotels built (ADR 0017): the Redis cache (1 day for cars), the search lock, the
shared monthly request counter, and Firestore for history. Each call to the source is saved as one snapshot
document, so the last result answers when the source fails, has no key, or the limit is nearly used.

## Technical Context

**Language/Version**: C# on .NET 10

**Primary Dependencies**: none new. `Microsoft.Extensions.Http.Resilience` (already present through Aspire) is
used to remove the standard resilience handler from the car client.

**Storage**: Redis `cars:{search}` for 1 day; Firestore `carSnapshots` (one document per call) and a
`carAirports` field on the existing `cities/{geonameId}` document. In-memory stores when Firestore or Redis is
not configured, as for hotels.

**Testing**: xUnit. Domain and Application with fakes; Infrastructure with canned JSON trimmed from the real
5 Nov response (`StubHttp`) and the Firestore emulator container; Api with `WebApplicationFactory` and in-memory
stores; Architecture tests unchanged.

**Target Platform**: Linux container on the Oracle server (ADR 0015)

**Project Type**: Web service

**Performance Goals**: first request within 40 s (SC-005); repeated request, sort or filter under 1 s (SC-004).

**Constraints**:
- Shares the 530-requests-a-month free plan with hotels: 1 request per new search, 1 per city for airports.
- Firestore free tier: one write per search (plus one per new city), not 500.
- Never call the internet from tests. A source failure is a warning, never an error.

**Scale/Scope**: two endpoints, one provider, one new Firestore collection, up to 500 cars per search.

**Checked with the real key** (2026-11-05): fields, normal price vs member price, coordinate IDs, airport
look-up, booking link, size and speed ([research.md](research.md)).

**Still open**: `ftsType=A` in the booking link for airports (R-6), checked in the quickstart.

## Constitution Check

| Principle | How this plan meets it | Result |
|---|---|---|
| I. Layered Architecture | Rules and sorting in Domain (`CarSearch`, `CarFilters`); handlers in Application behind `ICarProvider`, `ICarHistory`, `ICarAirportStore`; Firestore, Redis and HTTP only in Infrastructure; controller only translates | Pass |
| II. Every Feature Is Tested | Tests for every layer below, failure paths included; canned JSON only; Firestore against the emulator container; live checks in [quickstart.md](quickstart.md) | Pass |
| III. Zero Lint Findings | Normal build gate | Pass |
| IV. Partial Failure | Source down, slow past 40 s, no key, or the limit nearly used all give the last snapshot or an empty list with a warning | Pass |
| V. Low Cost First | Free plan; one request per search, searched only when Cars is opened; results kept a day; no retries | Pass |
| VI. Stable API Contract | New routes under `/api/v1`; nothing existing changes; view models with hand mapping; [contract](contracts/cars-api.md) | Pass |
| VII. Simplicity | No new package or framework; same patterns and stores as hotels; one snapshot document instead of a per-car collection | Pass |
| Configuration and Security | Same `RAPIDAPI_KEY` through `Env.cs`, sent in a header and never logged; the driver's age is part of the search key only, never linked to anyone | Pass |

## Project Structure

### Documentation (this feature)

```text
specs/004-cars/
├── spec.md · plan.md · research.md · data-model.md · quickstart.md
├── contracts/cars-api.md
└── tasks.md             # next step, after approval
```

### Source Code

```text
src/
├── Pamplemousse.Api/
│   ├── Controllers/DestinationsController.cs    # new GetCarPlacesAsync, GetCarsAsync actions
│   ├── Contracts/CarRequests.cs                 # new SearchCarsRequest
│   ├── ViewModels/CarViewModels.cs              # new
│   └── Mappings/CarMappings.cs                  # new
├── Pamplemousse.Application/
│   ├── Abstractions/Providers.cs                # ICarProvider
│   ├── Abstractions/Storage.cs                  # ICarHistory, ICarAirportStore
│   ├── Cars/CarSettings.cs                      # results TTL, airport radius, look-up refresh
│   ├── Cars/Queries/GetCarPlaces/               # query, handler, response
│   └── Cars/Queries/SearchCars/                 # query, handler, response
├── Pamplemousse.Domain/
│   └── Cars/                                    # CarSearch, PickUpPlace, CarOffer, CarFilters, CarSnapshot, DriverAge rules
└── Pamplemousse.Infrastructure/
    ├── Providers/BookingComCarProvider.cs       # look-up, search, coordinate IDs, booking link
    ├── Storage/FirestoreCarData.cs · InMemoryCarData.cs
    └── DependencyInjection.cs                   # car HttpClient: 40 s, no resilience handler

tests/
├── Pamplemousse.Domain.Tests/Cars/              # search rules, age, days, price choice, dedupe, sort, filters, paging
├── Pamplemousse.Application.Tests/Cars/         # cache hit, fresh call, wait for another caller, snapshot fallback, limit, no key, empty, airport look-up once
├── Pamplemousse.Infrastructure.Tests/Providers/ # canned JSON: fields, normal price, bags from specs, airports within 50 km, IDs, link, 429, broken JSON, timeout
├── Pamplemousse.Infrastructure.Tests/Storage/   # Firestore emulator: snapshot read back with raw data, airports merged into cities
└── Pamplemousse.Api.Tests/Cars/                 # endpoints: 200, 400s naming the field, 404, warnings
```

**Structure Decision**: follows ADR 0012 and the hotels feature. Both routes sit under destinations, because a
car only exists inside a search; there is no `/cars/{id}`. The details screen (Figma C4) uses the car from the
list, so it needs no call.

## How a search runs

1. Validate the input (Domain) and find the destination; unknown gives 404.
2. Resolve the pick-up place: `centre` is the destination's position. An IATA code must be one of the city's
   stored airports; if the city has none stored or they are over 30 days old, call the look-up once (step 5's
   counter applies) and store them. An unknown code gives 400 on `place`.
3. Read `cars:{search}` from Redis. Found: go to step 7.
4. Take the `lock:cars:{search}` lock for 60 s. If someone else holds it, wait up to 40 s for their result.
5. Check the monthly counter (shared with hotels). At 95% or more: go to step 8.
6. Call the source once. Map, drop bad prices, remove duplicates. Save one snapshot to Firestore and the cars to
   Redis for 1 day.
7. Filter, sort and page the cars; return them with the fetch time, the filter options, and the driver note.
8. On failure, no key, timeout, or the limit: return the newest snapshot for the search with its time and a
   warning, or an empty list, a warning and the Booking.com link.

Car places (`GET …/car-places`) returns the centre plus the stored airports, looking them up once when missing.
If the look-up fails, it returns only the centre with a warning.

## Delivery order

1. **Domain**: search, age, days, price choice, offer, filters, sort and paging.
2. **Provider**: look-up, search and link against canned JSON; car HttpClient without retries.
3. **Story 1**: storage (Firestore, in-memory), search handler with cache, lock and fallbacks, cars endpoint.
4. **Story 2**: airports, the places endpoint, the driver note.
5. **Story 3**: the booking link on every car.
6. **Story 4**: sorting and filtering, filter options.
7. **Checks and docs**: quickstart live checks (about 15 requests), README endpoints and data sources, sprint 2
   review.

Each step ends with all tests passing. The whole feature is one pull request. The app's Cars screen
(`chocomousse`, its own spec from the approved Figma page) follows once this is merged.

## Risks

| Risk | Effect | Response |
|---|---|---|
| A 23–30 s search feels broken | Travellers leave the screen | The app shows the C5 progress screen; the cache and the lock make every later view instant |
| The source's place-ID format changes (R-2) | City-centre searches fail | The provider falls back to the look-up's own IDs; the fallback path returns saved results meanwhile |
| Quota shared with hotels runs out | No new car or hotel prices for the month | One request per search, no retries, a day's cache; the 95% stop still answers from snapshots. Live checks budgeted at about 15 requests |
| Booking.com's search link changes (R-6) | "View on Booking.com" opens a broken page | The link is built in one method and checked in the quickstart; the source's own per-car link is stored too, in case it starts working |
| Large responses (2.3 MB) | Memory and time on the small server | Streamed into `JsonDocument`, mapped once, raw kept gzip-compressed; one search at a time per key thanks to the lock |

## Complexity Tracking

None. No constitution violations.
