# Implementation Plan: Itinerary

**Branch**: `008-itinerary` | **Date**: 2026-11-06 | **Spec**: [spec.md](spec.md)

**Status**: Approved by the owner on 2026-11-06, with the spec changes below

## Summary

A deterministic `PlanBuilder` in Domain turns a trip, its chosen offers and the destination's places into days,
a total and a match. An Application handler gathers the inputs in parallel from what already exists: the ranked
hotel and flight lists of spec 007 (best match first), the attractions and restaurants of Sprint 1, two new free
OpenTripMap lists (nature, nightlife), events and weather. It then saves the plan as a draft under the signed-in
traveller. Swaps pick from the top 20 offers kept in the plan, so they never call a source.

This plan covers the API. The itinerary and My plans screens get their own plan in `chocomousse`, from the
approved Sprint 4 Figma page (I0–I6, W4), after this is merged.

**Depends on** spec 009 (accounts): the signed-in traveller and the `travellers/{uid}` document. It is built after
009 is merged.

## Technical Context

**Language/Version**: C# on .NET 10

**Primary Dependencies**: none new.

**Storage**:
- Firestore `travellers/{uid}/plans/{planId}`, one document per plan (R-8), with memory when no project is set.
- Redis is used only through the existing providers' caches.

**Testing**: xUnit.
- Domain: `PlanBuilder` on hand-built places, offers and events (most of the tests).
- Application: handlers with fakes.
- Infrastructure: the two new OpenTripMap lists against canned JSON, and the plan store on the Firestore emulator.
- Api: `WebApplicationFactory` with test tokens.

**Target Platform**: Linux container on the Oracle server (ADR 0015)

**Project Type**: Web service

**Performance Goals**:
- A 5-day plan in under 5 s when the searches are stored (SC-001). The builder itself takes under 50 ms; the rest
  is the parallel source calls, mostly cached.
- Opening a saved plan in under 1 s (SC-007): one Firestore read.

**Constraints**:
- No paid request on a swap or on opening a plan (FR-011, FR-013).
- At most two new free OpenTripMap requests per destination a day.
- The same input gives the same plan (SC-005).

**Scale/Scope**:
- one Domain module;
- six handlers, one store, two place providers;
- one controller;
- small additions to `TravelProfile` and the places route.

## Spec changes found while planning

These need your OK together with this plan.

1. **FR-011, adding a car**: the first time a car is added, the ranked car search runs once, which may be one paid
   request if that search isn't stored. This is the same rule as FR-002 for the hotel and flight. After that,
   swapping and re-adding use the kept list (R-7).
2. **FR-007, airport to hotel**: one fixed 45-minute estimate for every destination for now, stated in the plan's
   notes (R-5).
3. **FR-005, nature and nightlife places**: two new OpenTripMap lists per destination, free and cached a day. The
   places route accepts `nature` and `nightlife` as well (R-1).

## Constitution Check

| Principle | How this plan meets it | Result |
|---|---|---|
| I. Layered Architecture | `PlanBuilder` and `Plan` in Domain with no dependency (FR-018); handlers in Application reuse the existing search handlers; Firestore and OpenTripMap in Infrastructure | Pass |
| II. Every Feature Is Tested | Builder rules (slots, grouping, timeline, arrival and departure, events, meals, free time, total, match, determinism), each Edge Case, handlers, providers on canned JSON, endpoints including another traveller's plan | Pass |
| III. Zero Lint Findings | Normal build gate | Pass |
| IV. Partial Failure | Each source can fail on its own, giving a warning; no plan only when there are no places at all (spec) | Pass |
| V. Low Cost First | Reuses stored searches; swaps call no source; two free OpenTripMap lists a day per destination | Pass |
| VI. Stable API Contract | New routes; additions only to `/me` and the places route ([contract](contracts/itinerary-api.md)) | Pass |
| VII. Simplicity | One builder with plain rules (R-2 to R-6); one document per plan; no new package | Pass |
| Configuration and Security | No new variable; plans and preferences never logged; another traveller's plan is a 404 | Pass |

## Project Structure

### Documentation (this feature)

```text
specs/008-itinerary/
├── spec.md · plan.md · research.md · data-model.md · quickstart.md
├── contracts/itinerary-api.md
└── tasks.md             # next step, after approval
```

### Source Code

```text
src/
├── Pamplemousse.Api/
│   ├── Controllers/PlansController.cs           # new
│   ├── Contracts/PlanRequests.cs                # new
│   ├── ViewModels/PlanViewModels.cs             # new
│   ├── Mappings/PlanMappings.cs                 # new; reuses the hotel, flight and car item mappings of spec 007
│   └── Contracts/ProfileRequests.cs · ViewModels/ProfileViewModels.cs   # pace
├── Pamplemousse.Application/
│   ├── Abstractions/Plans.cs                    # IPlanStore
│   ├── Itinerary/PlanInputs.cs                  # gathers offers, places, events, weather in parallel; warnings
│   ├── Itinerary/Commands/ BuildPlan · ChangePlan · SwapOffer · DeletePlan
│   ├── Itinerary/Queries/ ListPlans · GetPlan
│   └── Travellers/Commands/DeleteAccount/       # deletes plans first
├── Pamplemousse.Domain/
│   ├── Itinerary/                               # Pace, TripRequest, PlanOffers, PlacePools, PlanBuilder (Slots, DayGrouping, DayTimeline, PlanTotals, PlanScoring, PlanReasons), Plan
│   ├── Places/Place.cs                          # PlaceCategory Nature, Nightlife
│   └── Travellers/TravelProfile.cs              # Pace
└── Pamplemousse.Infrastructure/
    ├── Providers/OpenTripMapPlaceProvider.cs    # Nature and Nightlife providers
    ├── Storage/FirestorePlans.cs · InMemoryPlans.cs
    └── DependencyInjection.cs

tests/
├── Pamplemousse.Domain.Tests/Itinerary/         # every rule of R-2 to R-6, every Edge Case, SC-002 to SC-005 on fixed data
├── Pamplemousse.Application.Tests/Itinerary/    # inputs in parallel, each source failing, no places, 50 limit, swap uses alternatives only, car added once
├── Pamplemousse.Infrastructure.Tests/           # nature and nightlife lists on canned JSON; plan store round trip, owner scoping, delete all
└── Pamplemousse.Api.Tests/Itinerary/            # each route, 400 naming the field, 404 for another traveller, 409 at 50
```

**Structure Decision**: one builder with no dependency, fed with plain lists, so the rules are tested on hand-made
data and the assistant (TG-11) can reuse them. The ranked lists come from the existing search handlers with
`sort=match`. That keeps the cache, the saved results and the request limit exactly as in spec 007. Copies are
kept in the plan, so a saved plan never depends on a source again.

## How a plan is built

1. Check the request (Domain): dates, travellers, preferences, pace, budget, origin, each naming the field.
2. Refuse at 50 plans (`409`).
3. In parallel, each with its own timeout and warning:
   - ranked hotels (spec 007, `sort=match`, top 20);
   - ranked flights when there is an origin (round trip, top 20);
   - sights, restaurants, nature and nightlife;
   - events for the dates;
   - weather.
4. No places at all: `422 no-places`.
5. `PlanBuilder.Build` with the best hotel and flight, and no car.
6. Save as a draft with the copies and alternatives; return `201`.

A swap loads the plan, picks the offer from its alternatives, fetches the place lists again (free, cached), and
rebuilds. If the places fail, it keeps the old days, updates the offer, total and match, and adds a warning.

## Delivery order

Starts once spec 009 is merged.

1. **Domain**: `Pace`, `PlanBuilder` and `Plan`, fully tested on fixed data (Story 1's rules and every Edge Case).
2. **Providers**: nature and nightlife lists on canned JSON.
3. **Story 1**: `PlanInputs`, `BuildPlan`, store, `POST` and `GET /plans/{id}`.
4. **Story 3**: list, rename, status, delete; account deletion removes plans; pace in the profile.
5. **Story 2**: alternatives route, swap, add and remove the car.
6. **Checks and docs**:
   - quickstart live checks;
   - README routes;
   - ADR 0002 note: the itinerary is built by fixed rules, not the LLM;
   - sprint 4 log.

Each step ends with all tests passing. The whole feature is one pull request.

## Risks

| Risk | Effect | Response |
|---|---|---|
| OpenTripMap names few parks or bars in some cities | Nature or nightlife can't be met | Days fit drops and says so; free time fills the gaps; checked in 5 cities (quickstart 12) |
| Restaurants are only near the centre (500 cap) | Long walks to lunch from an outer hotel | The walk is shown; the match's food part drops; a per-hotel list could come later |
| Flight durations missing from the fares | Arrival time is a guess | The same route's fastest duration, else 4 h, with a note (R-5) |
| Greedy grouping makes one uneven day | A day with a long walk | Checked against SC-002 (under 12 km) on 5 cities; the rule is easy to change |
| Starting values (durations, 35 / 70 bands) feel wrong | Days look too full or too empty | All in one settings record; the API doesn't change |
| A plan document grows near 1 MB | Save fails | About 250 KB at most (R-8); a test builds a 30-day Packed plan and checks the size |

## Complexity Tracking

None. No constitution violations.
