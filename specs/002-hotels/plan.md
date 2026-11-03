# Implementation Plan: Hotel prices for a trip

**Branch**: `002-hotels` | **Date**: 2026-11-03 | **Spec**: [spec.md](spec.md)

**Status**: Draft, waiting for the owner's approval

## Summary

Add `GET /api/v1/destinations/{id}/hotels` and `GET /api/v1/hotels/{hotelId}`. The API maps the TravelGenie
city to the Booking COM API's location ID (ADR 0016), searches for the dates and adults, stores every field
it gets back in Firestore, and returns the hotels sorted and filtered from that stored data. Redis holds
each search's results for 12 hours, a lock so the same search is never sent twice at once, and the monthly
request count (ADR 0017). Every call to the source is saved as a price snapshot. When the source fails, has
no key, or the monthly limit is nearly used, the last snapshot is returned with its time.

## Technical Context

**Language/Version**: C# on .NET 10 (SDK 10.0.113)

**Primary Dependencies**: Three new packages, all from the existing families or Google's official SDK:
- `Google.Cloud.Firestore`: Google's official .NET client (ADR 0017).
- `Aspire.StackExchange.Redis`: the Redis connection itself, needed for the lock and the request counter, which the existing cache interface can't do. Same Aspire family and version as the cache package already used.
- `Testcontainers.Redis` and `Testcontainers.Firestore`: real Redis and the Firestore emulator in tests (test projects only).

**Storage**: Redis (already in Aspire as `cache`) for 12-hour results, locks and the request counter. Firestore for cities, hotels and price snapshots.

**Testing**: xUnit. Domain and Application with fakes; Infrastructure with canned JSON (`StubHttp`) for the provider and real containers for Redis and the Firestore emulator; Api with `WebApplicationFactory` and in-memory stores; Architecture tests.

**Target Platform**: Linux container on the Oracle server (ADR 0015)

**Project Type**: Web service

**Performance Goals**: First request under 5 s, repeated request under 1 s (SC-004, SC-005)

**Constraints**:
- About $0: free Basic plan, 530 requests a month, hard limit.
- Firestore free tier: 50,000 reads and 20,000 writes a day, 1 GiB. A search writes about 21 documents.
- Never call the internet from tests.
- A source failure is a warning, never an error.

**Scale/Scope**: Two endpoints, one provider, three Firestore collections, about 20 hotels per search on the free plan.

**Checked with the real key** (2026-11-03): field names, total price (gross plus excluded taxes), 20 per page, the details link, and that the priced room fits the adults ([research.md](research.md)).

**Still open**: whether Booking.com accepts a hotel search link without the details call (R-5); the Firebase service account from the owner.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | How this plan meets it | Result |
|---|---|---|
| I. Layered Architecture | Handlers in Application; provider, stores, cache, lock and counter behind Application interfaces; rules in Domain; Firestore and Redis only in Infrastructure; controller only translates | Pass |
| II. Every Feature Is Tested | Tests for every layer listed below, failure paths included; canned JSON only; Redis and Firestore tested against real containers; live checks in [quickstart.md](quickstart.md) | Pass |
| III. Zero Lint Findings | Normal build gate, no exceptions needed | Pass |
| IV. Partial Failure | Source down, no key, limit nearly used, or Firestore missing all give saved prices or an empty list with a warning; 10 s timeout on the source | Pass |
| V. Low Cost First | Free Basic plan and Firestore free tier; one page per search, details only when a hotel is opened; the Pro plan is a setting change only, with the owner's approval | Pass |
| VI. Stable API Contract | New routes under `/api/v1`; nothing existing changes; view models with hand mapping; [contract](contracts/hotels-api.md) | Pass |
| VII. Simplicity | No new framework: Firestore and Redis are chosen in ADR 0017; sorting and filtering run in memory over at most a few hundred hotels, so no Firestore indexes or query language | Pass |
| Configuration and Security | `RAPIDAPI_KEY`, `GOOGLE_APPLICATION_CREDENTIALS` and `FIREBASE_PROJECT_ID` through `Env.cs`, added to `.env.example`; the key is sent in a header and never logged; the service account file stays outside the repo; no personal data stored or logged | Pass |

Re-checked after design: all pass. No violations to justify.

## Project Structure

### Documentation (this feature)

```text
specs/002-hotels/
├── spec.md · plan.md · research.md · data-model.md · quickstart.md
├── contracts/hotels-api.md
└── tasks.md             # next step, after approval
```

### Source Code (repository root)

```text
src/
├── Pamplemousse.AppHost/AppHost.cs              # Firestore emulator container when no service account is set
├── Pamplemousse.Api/
│   ├── Controllers/DestinationsController.cs    # new GetHotelsAsync action
│   ├── Controllers/HotelsController.cs          # new: one hotel, with its booking link
│   ├── Contracts/HotelRequests.cs               # new SearchHotelsRequest, GetHotelRequest
│   ├── ViewModels/HotelViewModels.cs            # new
│   ├── Mappings/HotelMappings.cs                # new
│   └── Env.cs · Program.cs                      # key, project ID, credentials, settings
├── Pamplemousse.Application/
│   ├── Abstractions/Providers.cs                # IHotelProvider
│   ├── Abstractions/Storage.cs                  # new: IHotelStore, ICityMappingStore, IPriceSnapshotStore, ISearchLock, IRequestQuota
│   ├── Hotels/Queries/SearchHotels/             # query, handler, response
│   ├── Hotels/Queries/GetHotel/                 # query, handler, response
│   ├── Hotels/HotelSorting.cs                   # sort and filter over stored hotels
│   └── DependencyInjection.cs
├── Pamplemousse.Domain/
│   └── Hotels/                                  # Hotel, HotelOffer, HotelSearch, HotelFilters, PriceSnapshot, CityMapping, rules
└── Pamplemousse.Infrastructure/
    ├── Providers/BookingComHotelProvider.cs     # new: auto-complete, search, details
    ├── Storage/Firestore*.cs                    # new: the three Firestore stores
    ├── Storage/InMemory*.cs                     # new: used when Firestore is not configured
    ├── Caching/RedisSearchLock.cs · RedisRequestQuota.cs  # new
    ├── HotelOptions.cs                          # new: pages per search, details for every hotel, limit, near-city radius
    └── ExternalApiOptions.cs · DependencyInjection.cs

tests/
├── Pamplemousse.Domain.Tests/Hotels/            # search rules, total price, occupancy, filters
├── Pamplemousse.Application.Tests/Hotels/       # handler: cache hit, fresh call, snapshot fallback, limit reached, empty, sort and filter
├── Pamplemousse.Infrastructure.Tests/Providers/ # canned JSON: fields, totals, bad prices, duplicates, missing key, 429, broken JSON
├── Pamplemousse.Infrastructure.Tests/Storage/   # Firestore emulator and Redis containers: save and read back, lock, counter
└── Pamplemousse.Api.Tests/Hotels/               # endpoints: 200, 400s naming the field, 404, warnings
```

**Structure Decision**: Follows ADR 0012. Hotels get their own `Application/Hotels/` folder. The search route stays
under destinations, like flights. Opening a hotel gets its own `/api/v1/hotels/{hotelId}` route, because a hotel
exists apart from any search. Storage interfaces go in a new `Storage.cs` beside `Providers.cs`, because later
features (cars, attractions, flights) will reuse the same pattern.

## How a search runs

1. Validate the input (Domain rules) and find the destination; unknown gives 404.
2. Look up the city's source ID in `cities`; if missing, call auto-complete once and store it.
3. Read `hotels:{search}` from Redis. Found: go to step 7.
4. Check the monthly counter. At 95% or more: go to step 8.
5. Take the `lock:{search}` lock. If someone else holds it, wait up to 5 s for their result.
6. Call the source (pages per search from settings, default 1). Count each call. Save the hotels to `hotels`, one price snapshot to `priceSnapshots`, and the result list to Redis for 12 h.
7. Sort, filter and page the stored hotels; return them with the time prices were fetched.
8. On failure, no key, or the limit: return the newest snapshot for the search with its time and a warning, or an empty list, a warning and a booking-site link.

Opening a hotel (`GET /api/v1/hotels/{hotelId}`) reads the hotel from `hotels`; if its details are missing or
older than a week, it calls the details endpoint once (subject to the same counter) and stores them. The booking
link is the details `url` with the dates, adults, one room and currency added.

## Delivery order

1. **Domain** rules and types (no external calls).
2. **Storage:** Firestore and in-memory stores, Redis lock and counter, AppHost emulator, container tests.
3. **Story 1:** provider, search handler, endpoint, including the failure and limit paths.
4. **Story 2:** hotel details, booking link, hotel endpoint.
5. **Story 3:** sorting and filtering on stored data.
6. **Checks and docs:** the live checks in `quickstart.md` (about 25 requests), README endpoints and data sources, `.env.example`, keys checklist, sprint review.

Each step ends with all tests passing. The whole feature is one pull request unless it grows too large.

## Risks

| Risk | Effect | Response |
|---|---|---|
| 530 requests a month run out during development and demos | No new prices for the rest of the month | Counter stops at 95%; saved snapshots still answer; live checks budgeted at about 25 requests; Pro plan only with the owner's approval |
| Free cancellation and type of stay come only from hotel details | On the free plan these two filters only cover hotels already opened | Response says how many hotels the filter could check. Setting "details for every hotel" fixes it on the Pro plan (about 21 requests per search instead of 1) |
| Tests now need Docker for the Redis and Firestore containers | The pre-push hook and CI fail when Docker isn't running | Docker is already needed for Aspire and the CI image build; container tests say clearly when Docker is missing |
| No Firebase service account yet | Nothing is stored between restarts | In-memory stores and a startup warning; local runs use the emulator through Aspire |
| The source is an unofficial API and may change or vanish | Rework in one class, or no hotels | Everything source-specific stays in `BookingComHotelProvider`; raw data is stored, so old snapshots stay readable; `IHotelProvider` keeps the source replaceable (FR-015) |
| Booking.com's link without the slug may not open the hotel (R-5) | One extra request per opened hotel | Use the details `url`, stored for a week, so each hotel costs the request once |

## Complexity Tracking

None. No constitution violations.
