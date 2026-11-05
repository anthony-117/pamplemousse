# Implementation Plan: Weighted search

**Branch**: `007-weighted-search` | **Date**: 2026-11-05 | **Spec**: [spec.md](spec.md)

**Status**: Approved by the owner on 2026-11-05, with the two spec changes below

## Summary

Add a deterministic `ScoringEngine` in Domain and a `sort=match` option, with six preferences and a trip budget,
on the existing hotels, flights and cars lists. Each offer gets a % match, a breakdown and a fixed-wording reason.
Budget and Comfort apply to all three kinds. Culture, Food, Nature and Nightlife apply to hotels, measured from
places near each hotel: the destination's top attractions, and OpenTripMap counts fetched once per hotel and kept
for 30 days. Ranking runs on the stored search results, so it never makes a paid request.

## Technical Context

**Language/Version**: C# on .NET 10

**Primary Dependencies**: none new.

**Storage**: Redis only: `nearby:{hotelId}` (30 days) and `attractions:{destinationId}` (1 day), in the
existing `ISearchCache` (in-memory when Redis is not configured). No Firestore change.

**Testing**: xUnit.
- Domain: the engine and the three kinds, with hand-built offers.
- Application: handlers with fakes.
- Infrastructure: the OpenTripMap counts against canned JSON trimmed from the 5 Nov answers.
- Api: `WebApplicationFactory`.

**Target Platform**: Linux container on the Oracle server (ADR 0015)

**Project Type**: Web service

**Performance Goals**:
- Re-ranking a search already answered takes under 1 s (SC-004).
- The first ranking of a new hotel list adds about 3–5 s, for the nearby counts.

**Constraints**:
- No paid request for ranking (FR-007).
- OpenTripMap free plan: 5,000 requests a day, 10 a second. Ranking uses at most 40 requests per new 20-hotel
  search, 4 at a time.
- The same input always gives the same output (FR-009).

**Scale/Scope**: one Domain module, one new provider, changes to three handlers, three request records and three
view models.

**Checked with the real key** (2026-11-05, OpenTripMap only, about 12 requests): the 500 cap, kinds and counts
around three Rome positions ([research.md](research.md)).

## Spec changes found while planning

These need your OK together with this plan.

1. **FR-008**: one city-wide request can't count places near every hotel, because OpenTripMap returns at most
   the 500 nearest (R-1). Instead: two free requests per hotel, the first time it is ranked, kept 30 days.
   It's still free and bounded, and needs no paid source.
2. **FR-003**: journey time is compared within the list (shortest ÷ this), not on a fixed scale, because a good
   time depends on the route (R-3).

## Constitution Check

| Principle | How this plan meets it | Result |
|---|---|---|
| I. Layered Architecture | Scoring in Domain with no dependency (`Ranking/`); nearby counts behind `INearbyPlaces` in Application; OpenTripMap in Infrastructure; controllers only add the query parameters | Pass |
| II. Every Feature Is Tested | Engine rules, each kind, reasons, handlers, provider and endpoints, failure paths included; canned JSON only | Pass |
| III. Zero Lint Findings | Normal build gate | Pass |
| IV. Partial Failure | Places down or no key: interests count as average with a warning; ranking still runs on budget and comfort | Pass |
| V. Low Cost First | No paid request; free OpenTripMap requests cached 30 days; attractions cached a day | Pass |
| VI. Stable API Contract | Only additions: new optional parameters, a `match` field, three response fields; no route or field changes ([contract](contracts/weighted-search-api.md)) | Pass |
| VII. Simplicity | One generic engine for three kinds; no new package; no new endpoint or store | Pass |
| Configuration and Security | Same `OPENTRIPMAP_API_KEY`; preferences are part of the request only, never stored or logged (FR-013) | Pass |

## Project Structure

### Documentation (this feature)

```text
specs/007-weighted-search/
├── spec.md · plan.md · research.md · data-model.md · quickstart.md
├── contracts/weighted-search-api.md
└── tasks.md             # next step, after approval
```

### Source Code

```text
src/
├── Pamplemousse.Api/
│   ├── Contracts/RankingRequest.cs              # new: the six preferences + tripBudget, shared by the three requests
│   ├── Contracts/*Requests.cs                   # hotels, cars, flights requests gain the ranking fields; flights gains sort
│   ├── ViewModels/MatchViewModels.cs            # new: match, parts, weights
│   └── Mappings/*Mappings.cs                    # add match per item, weights, tripBudget, rankingNote
├── Pamplemousse.Application/
│   ├── Abstractions/Providers.cs                # INearbyPlaces
│   ├── Ranking/HotelNearby.cs                   # counts per hotel from cache or provider; culture from attractions
│   ├── Hotels/Queries/SearchHotels/             # rank when asked, page after ranking, R-5 paging
│   ├── Flights/Queries/SearchFlights/           # sort, rank before the limit
│   └── Cars/Queries/SearchCars/                 # rank when asked
├── Pamplemousse.Domain/
│   ├── Ranking/                                 # Weights, Criterion, Candidate, measures, Match, ScoringEngine, ReasonBook
│   ├── Ranking/HotelRanking.cs · FlightRanking.cs · CarRanking.cs
│   ├── Hotels/HotelFilters.cs · Cars/CarFilters.cs   # Match sort value
└── Pamplemousse.Infrastructure/
    ├── Providers/OpenTripMapNearbyPlaces.cs     # two radius requests per position
    └── DependencyInjection.cs                   # register it on the existing OpenTripMap client

tests/
├── Pamplemousse.Domain.Tests/Ranking/           # weights validation, normalising, equal values, single offer, unknowns, weight 0, over budget order, ties, reasons, determinism, each kind's measures
├── Pamplemousse.Application.Tests/Ranking/      # nearby from cache, fetched once, 4 at a time, failure → average + warning; handlers: match sort, other sort with preferences, nothing applies, no paid call when re-ranking, hotel paging
├── Pamplemousse.Infrastructure.Tests/Providers/ # canned OpenTripMap JSON: counts by kind, 500 cap, no key, 429, broken JSON
└── Pamplemousse.Api.Tests/Ranking/              # each list with sort=match, 400s naming the field, match null without preferences
```

**Structure Decision**: ranking is a sort on the existing lists, not a new endpoint. That keeps filters, paging,
links and fallbacks working unchanged (spec Story 3), and the app's tabs call the same routes they already call.
The engine is generic over the offer type, so the itinerary (TG-10) and the assistant can reuse it.

## How a ranked request runs

1. Validate the preferences and the trip budget (Domain); bad values give 400 naming the field.
2. Run the list exactly as today: cache, then source, then saved results. Ranking never changes this path.
3. Apply the filters. With `sort=match`, they keep the source's order.
4. If every criterion this kind uses has weight 0, return the usual order with `rankingNote` (FR-010).
5. Hotels only: get each hotel's nearby counts (`nearby:{hotelId}`, else OpenTripMap, 4 at a time, 10 s each)
   and the attraction list (`attractions:{id}`, else the attraction provider). Any failure: interests unknown
   and one warning.
6. Build a `Candidate` per offer and rank with `ScoringEngine` (data-model.md).
7. Order: by match (`sort=match`) or the sort asked for. Page, and map `match` onto each item.

Flights rank all the offers the source returned, then apply `limit`. Cars rank all stored cars, up to 500, then
page.

## Delivery order

1. **Domain**: weights, engine, reasons, and each kind's candidates, fully tested on hand-built data.
2. **Provider**: OpenTripMap nearby counts against canned JSON.
3. **Story 1 (hotels)**: `HotelNearby`, the hotels handler and request, view model, endpoint tests.
4. **Story 2**: flights (sort parameter, rank before limit) and cars.
5. **Story 3**: other sorts with preferences, paging rules, `rankingNote`.
6. **Checks and docs**:
   - quickstart live checks: about 6 paid requests (hotel searches in 5 cities, one car search in Rome), and
     free OpenTripMap and flight requests;
   - README endpoints;
   - ADR 0008 amendment (food is measured by how many places are nearby, not their quality);
   - sprint 3 log.

Each step ends with all tests passing. The whole feature is one pull request. The app screens (`chocomousse`,
their own spec from the approved Figma page) follow once this is merged.

## Risks

| Risk | Effect | Response |
|---|---|---|
| The first ranked hotel list waits 3–5 s for 40 OpenTripMap requests | Slower first load | Counts are kept 30 days per hotel, so most later lists need none; 10 s limit each, then the "count as average" fallback |
| OpenTripMap is down or has no key | Interests can't be measured | Ranked on budget and comfort with a warning (FR-011, Figma S5) |
| Counts don't reflect quality (ADR 0008) | A food lover gets "lots of restaurants", not "good restaurants" | The breakdown says what was counted; a rated source can replace `INearbyPlaces` later without an API change |
| Distances (1 / 1.5 / 2 km) feel wrong in some cities | Rankings look odd | They're settings, checked against 5 cities in the quickstart (SC-001, SC-002) |
| Weights feel too weak or too strong (20 / 50 / 90) | Sliders seem to do little | SC-001 to SC-003 checked live; the app's mapping can change without an API change |

## Complexity Tracking

None. No constitution violations.
