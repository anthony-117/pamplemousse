# Tasks: Itinerary

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md),
[contracts/itinerary-api.md](contracts/itinerary-api.md). Plan approved by the owner on 2026-11-06; spec 009 merged.

Each phase ends with every test passing (`dotnet test`) and no analyzer finding.

**Done on 2026-11-06.** Where the code ended up differs a little from the names below: `Pace` sits in
`Itinerary/TripRequest.cs`, `PlanOffers` and `PlacePools` in `Itinerary/BuiltPlan.cs`, and the Application use cases in
one file, `Itinerary/PlanHandlers.cs`, beside the gatherer `Itinerary/PlanInputs.cs`. T025: every quickstart check needs
a signed-in account, so all of them are the owner's; checks 2–5 and 13 are also covered by tests on fixed data.

## Phase 1 · Domain (FR-005 to FR-010, FR-018)

- [x] T001 `Itinerary/Pace.cs`: `Pace` (Relaxed 2, Balanced 3, Packed 4), parsing that names `pace`.
- [x] T002 `Itinerary/TripRequest.cs`: destination, dates (1–30 days, field `to`), adults (1–8), currency, origin,
  weights, pace, trip budget (> 0, field `tripBudget`), the destination's UTC offset.
- [x] T003 `Itinerary/PlanInputs.cs`: `PlanOffers`, `PlacePools`.
- [x] T004 `Itinerary/BuiltPlan.cs`: days, items, references, total, match.
- [x] T005 `Itinerary/PlanBuilder.cs`: slots (R-2), choice, grouping (R-3), timeline (R-4), arrival and departure
  (R-5), events, nightlife, free time, weather, total (FR-010), match (R-6).
- [x] T006 `Itinerary/Plan.cs`: the saved plan; name, status, alternatives (R-7), swap, car, the 50 limit.
- [x] T007 Tests in `Pamplemousse.Domain.Tests/Itinerary/`: every rule above, every Edge Case of the spec, SC-002 to
  SC-005 on fixed data, a 30-day Packed plan.

## Phase 2 · Places (R-1)

- [x] T008 `PlaceCategory.Nature` and `Nightlife`; `OpenTripMapNatureProvider` (6 km, named) and
  `OpenTripMapNightlifeProvider` (3 km, rate 2+), cached 24 h; registered.
- [x] T009 Places route accepts `nature` and `nightlife`.
- [x] T010 Tests on canned JSON.

## Phase 3 · Story 1: build and open a plan

- [x] T011 `Abstractions/Plans.cs`: `IPlanStore`.
- [x] T012 `Itinerary/PlanInputs` gatherer: ranked hotels and flights (spec 007 handlers, `sort=match`), places,
  events, weather in parallel; each failure a warning.
- [x] T013 `BuildPlan` (50 limit, `no-places`), `GetPlan`.
- [x] T014 `FirestorePlanStore`, `InMemoryPlanStore`; registered.
- [x] T015 `PlansController` `POST` and `GET /plans/{id}`; view models; `409` and `422` problem codes.
- [x] T016 Tests: handlers with fakes, store round trip, endpoints (another traveller's plan is `404`).

## Phase 4 · Story 3: my plans

- [x] T017 `ListPlans`, `ChangePlan` (name, status), `DeletePlan`; routes.
- [x] T018 Account deletion removes the plans first.
- [x] T019 `Pace` in the profile (`GET` and `PUT /me`).
- [x] T020 Tests.

## Phase 5 · Story 2: swap

- [x] T021 Alternatives route; `PUT /plans/{id}/{hotel|flight|car}`; `DELETE /plans/{id}/car`.
- [x] T022 The first car runs the ranked car search once; swaps call no source; failed places keep the old days.
- [x] T023 Tests.

## Phase 6 · Checks and docs

- [x] T024 README routes; ADR 0002 note; sprint 4 log; research notes on what changed while building.
- [x] T025 Quickstart checks that need no account; the owner runs the signed-in ones.
