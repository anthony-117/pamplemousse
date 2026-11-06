# Tasks: Itinerary

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md),
[contracts/itinerary-api.md](contracts/itinerary-api.md). Plan approved by the owner on 2026-11-06; spec 009 merged.

Each phase ends with every test passing (`dotnet test`) and no analyzer finding.

## Phase 1 · Domain (FR-005 to FR-010, FR-018)

- [ ] T001 `Itinerary/Pace.cs`: `Pace` (Relaxed 2, Balanced 3, Packed 4), parsing that names `pace`.
- [ ] T002 `Itinerary/TripRequest.cs`: destination, dates (1–30 days, field `to`), adults (1–8), currency, origin,
  weights, pace, trip budget (> 0, field `tripBudget`), the destination's UTC offset.
- [ ] T003 `Itinerary/PlanInputs.cs`: `PlanOffers`, `PlacePools`.
- [ ] T004 `Itinerary/BuiltPlan.cs`: days, items, references, total, match.
- [ ] T005 `Itinerary/PlanBuilder.cs`: slots (R-2), choice, grouping (R-3), timeline (R-4), arrival and departure
  (R-5), events, nightlife, free time, weather, total (FR-010), match (R-6).
- [ ] T006 `Itinerary/Plan.cs`: the saved plan; name, status, alternatives (R-7), swap, car, the 50 limit.
- [ ] T007 Tests in `Pamplemousse.Domain.Tests/Itinerary/`: every rule above, every Edge Case of the spec, SC-002 to
  SC-005 on fixed data, a 30-day Packed plan.

## Phase 2 · Places (R-1)

- [ ] T008 `PlaceCategory.Nature` and `Nightlife`; `OpenTripMapNatureProvider` (6 km, named) and
  `OpenTripMapNightlifeProvider` (3 km, rate 2+), cached 24 h; registered.
- [ ] T009 Places route accepts `nature` and `nightlife`.
- [ ] T010 Tests on canned JSON.

## Phase 3 · Story 1: build and open a plan

- [ ] T011 `Abstractions/Plans.cs`: `IPlanStore`.
- [ ] T012 `Itinerary/PlanInputs` gatherer: ranked hotels and flights (spec 007 handlers, `sort=match`), places,
  events, weather in parallel; each failure a warning.
- [ ] T013 `BuildPlan` (50 limit, `no-places`), `GetPlan`.
- [ ] T014 `FirestorePlanStore`, `InMemoryPlanStore`; registered.
- [ ] T015 `PlansController` `POST` and `GET /plans/{id}`; view models; `409` and `422` problem codes.
- [ ] T016 Tests: handlers with fakes, store round trip, endpoints (another traveller's plan is `404`).

## Phase 4 · Story 3: my plans

- [ ] T017 `ListPlans`, `ChangePlan` (name, status), `DeletePlan`; routes.
- [ ] T018 Account deletion removes the plans first.
- [ ] T019 `Pace` in the profile (`GET` and `PUT /me`).
- [ ] T020 Tests.

## Phase 5 · Story 2: swap

- [ ] T021 Alternatives route; `PUT /plans/{id}/{hotel|flight|car}`; `DELETE /plans/{id}/car`.
- [ ] T022 The first car runs the ranked car search once; swaps call no source; failed places keep the old days.
- [ ] T023 Tests.

## Phase 6 · Checks and docs

- [ ] T024 README routes; ADR 0002 note; sprint 4 log; research notes on what changed while building.
- [ ] T025 Quickstart checks that need no account; the owner runs the signed-in ones.
