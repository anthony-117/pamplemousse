---
description: "Task list for flight prices for a trip"
---

# Tasks: Flight prices for a trip

**Input**: Design documents from `/specs/001-flights/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/flights-api.md, quickstart.md

**Tests**: Required. The constitution (principle II) requires unit and endpoint tests, including failure paths, with no internet calls.

**Organization**: Tasks are grouped by user story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no unfinished dependency)
- **[US1], [US2], [US3]**: The user story the task belongs to

## Phase 1: Setup

**Purpose**: Clear the last open point and add configuration

- [x] T001 Check Travelpayouts' terms for embedding `cities.json`, `airports.json` and `airlines.json`, record the result in specs/001-flights/research.md (R-3), and stop to ask the owner if embedding is not allowed
- [x] T002 Add `TravelpayoutsApiToken` and `TravelpayoutsMarker` through `Env.Get` in src/Pamplemousse.Api/Env.cs and tests in tests/Pamplemousse.Api.Tests/EnvTests.cs
- [x] T003 [P] Add `TRAVELPAYOUTS_API_TOKEN` and `TRAVELPAYOUTS_MARKER`, without values, to .env.example
- [x] T004 Add the two settings to src/Pamplemousse.Infrastructure/ExternalApiOptions.cs and pass them from src/Pamplemousse.Api/Program.cs

---

## Phase 2: Foundational

**Purpose**: Flight rules and city lookup, needed by every story

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [x] T005 [P] Add `TripType` and `DateMatch` enums and the `FlightOffer` and `FlightCity` records in src/Pamplemousse.Domain/Flights/FlightOffer.cs
- [x] T006 Add `FlightSearch` with its factory (same flight city rejected on field `origin`, currency format on field `currency`, default USD and round trip, return date ignored for one way) in src/Pamplemousse.Domain/Flights/FlightSearch.cs
- [x] T007 [P] Test `FlightSearch` rules in tests/Pamplemousse.Domain.Tests/Flights/FlightSearchTests.cs
- [x] T008 Add `IFlightProvider`, `IFlightCities`, `FlightResults` and `CityMatch` in src/Pamplemousse.Application/Abstractions/Providers.cs
- [x] T009 Write scripts/update-flight-data.py to build src/Pamplemousse.Infrastructure/ReferenceData/Data/flight-cities.tsv.gz and airlines.tsv.gz from the public lists, keeping only cities with a flightable airport, then run it
- [x] T010 Add `FlightCityStore` (nearest flight city within 150 km, airline name lookup) in src/Pamplemousse.Infrastructure/ReferenceData/FlightCityStore.cs
- [x] T011 [P] Test `FlightCityStore` (Rome→ROM, Byblos→BEY, Zermatt→SIR, a point in mid-ocean→null, known and unknown airline) in tests/Pamplemousse.Infrastructure.Tests/ReferenceData/FlightCityStoreTests.cs
- [x] T012 Register `FlightCityStore` as `IFlightCities` in src/Pamplemousse.Infrastructure/DependencyInjection.cs

**Checkpoint**: All tests pass; cities map to flight codes.

---

## Phase 3: User Story 1 - See recent flight prices for my trip (Priority: P1) 🎯 MVP

**Goal**: Fares gets flights cheapest first for a route and dates, with nearby and same-month fallback, clearly marked as recent prices.

**Independent Test**: Request flights for a route and valid dates through the API with a stubbed source, and confirm the order, the fields, the labels and the warnings.

### Tests for User Story 1

- [x] T013 [P] [US1] Add canned responses from the real API, and test field mapping, `FR` + `166` becoming `FR166`, airline names, zero and missing prices left out, duplicates shown once, currency always sent, token sent as a header and absent from the URL, in tests/Pamplemousse.Infrastructure.Tests/Providers/TravelpayoutsFlightProviderTests.cs
- [x] T014 [P] [US1] In the same file, test the date levels: exact stops after one call; empty exact uses month results for nearby on both legs; no nearby gives same month; month boundary asks both months; past fares dropped; one-way uses `one_way=true`; repeated request served from cache
- [x] T015 [P] [US1] In the same file, test failures: missing token throws `ProviderNotConfiguredException` with no call, 429 and 500 raise errors, broken JSON raises an error, and unknown currency (400) raises `DomainException` on field `currency`
- [x] T016 [P] [US1] Test the handler (unknown destination or origin → not found, same flight city rejected, no flight city in range → empty with a message, nearby city reported, source failure → warning and empty list, missing token → warning naming the variable, limit clamped to 1–50, price note present, message set for non-exact levels and for no fares, with a search link) in tests/Pamplemousse.Application.Tests/Flights/SearchFlightsHandlerTests.cs
- [x] T017 [US1] Test the endpoint (200 with the contract's shape, missing `origin` → 400 naming it, same city → 400, bad dates → 400, bad `tripType`/`limit`/`currency` → 400, unknown ids → 404, no token → 200 with a warning, existing endpoints unaffected) in tests/Pamplemousse.Api.Tests/Destinations/FlightEndpointsTests.cs, with a fake provider in tests/Pamplemousse.Api.Tests/TestApiFactory.cs so no test calls the internet

### Implementation for User Story 1

- [x] T018 [US1] Implement `TravelpayoutsFlightProvider` (exact call, month fallback, date levels, 1-hour cache, token in `X-Access-Token`, currency always sent) in src/Pamplemousse.Infrastructure/Providers/TravelpayoutsFlightProvider.cs
- [x] T019 [US1] Register the provider with a 5-second timeout in src/Pamplemousse.Infrastructure/DependencyInjection.cs
- [x] T020 [US1] Implement the query, handler and response in src/Pamplemousse.Application/Flights/Queries/SearchFlights/SearchFlightsHandler.cs, using `SectionLoader` for failures, and register it in src/Pamplemousse.Application/DependencyInjection.cs
- [x] T021 [P] [US1] Add view models in src/Pamplemousse.Api/ViewModels/FlightViewModels.cs and hand-written mapping in src/Pamplemousse.Api/Mappings/FlightMappings.cs
- [x] T022 [US1] Add `SearchFlightsRequest` to src/Pamplemousse.Api/Contracts/DestinationRequests.cs and the `GetFlightsAsync` action to src/Pamplemousse.Api/Controllers/DestinationsController.cs
- [x] T023 [US1] Run the first checks in specs/001-flights/quickstart.md against the running API

**Checkpoint**: User Story 1 works alone through the API.

---

## Phase 4: User Story 2 - Open a flight to book it (Priority: P2)

**Goal**: Each flight has a working link carrying the partner marker, and an empty result has a search link.

**Independent Test**: Take a returned link, open it, and confirm the route, the dates and the marker.

### Tests for User Story 2

- [x] T024 [P] [US2] Test link building (site address added to the fare's relative link, marker added when configured, link intact without a marker, warning when the marker is missing, search link for the requested route and dates) in tests/Pamplemousse.Infrastructure.Tests/Providers/TravelpayoutsFlightProviderTests.cs

### Implementation for User Story 2

- [x] T025 [US2] Add booking links, the search link and the missing-marker warning in src/Pamplemousse.Infrastructure/Providers/TravelpayoutsFlightProvider.cs
- [x] T026 [US2] Pass the warning and search link through src/Pamplemousse.Application/Flights/Queries/SearchFlights/SearchFlightsHandler.cs and check its test in tests/Pamplemousse.Application.Tests/Flights/SearchFlightsHandlerTests.cs
- [x] T027 [US2] Open 10 returned links in a browser for SC-006 and record the result

**Checkpoint**: User Stories 1 and 2 work.

---

## Phase 5: User Story 3 - Direct flights only (Priority: P3)

**Goal**: Fares can ask for direct flights only.

**Independent Test**: Request direct flights only, and confirm every returned flight has zero stops.

### Tests for User Story 3

- [x] T028 [P] [US3] Test that `direct=true` is sent and that any fare with stops is still left out, in tests/Pamplemousse.Infrastructure.Tests/Providers/TravelpayoutsFlightProviderTests.cs
- [x] T029 [P] [US3] Test `directOnly=true` through the endpoint, including no flights being an empty result and not an error, in tests/Pamplemousse.Api.Tests/Destinations/FlightEndpointsTests.cs

### Implementation for User Story 3

- [x] T030 [US3] Pass `DirectOnly` from the request to the source and include it in the cache key, in src/Pamplemousse.Infrastructure/Providers/TravelpayoutsFlightProvider.cs

**Checkpoint**: All user stories work.

---

## Phase 6: Polish

**Purpose**: Measure the result and update the documentation

- [x] T031 Run all live checks in specs/001-flights/quickstart.md (SC-001 to SC-007) and write the results in docs/project/sprints/sprint-02.md, including what did not pass
- [ ] T032 Bring the results for Beirut routes to the owner, to decide whether a second source is needed
- [x] T033 [P] Update README.md (endpoint, environment variables, data source and its attribution, updating the flight data)
- [x] T034 [P] Tick the Travelpayouts row in docs/setup/api-keys-checklist.md, and update docs/schemas/domain/domain-model.md for `FlightOffer`
- [x] T035 Run .githooks/pre-push (formatting, analyzers, every test, Docker image), and confirm Pamplemousse.Architecture.Tests still passes
- [x] T036 Confirm no token value appears in any committed file or log output

---

## Dependencies & Execution Order

- **Setup (Phase 1)**: first. T001 can stop the plan's embedding approach.
- **Foundational (Phase 2)**: after Setup. Blocks all stories.
- **User Story 1 (Phase 3)**: after Foundational.
- **User Story 2 (Phase 4)**: after User Story 1, because links belong to returned flights.
- **User Story 3 (Phase 5)**: after User Story 1; independent of User Story 2.
- **Polish (Phase 6)**: after the stories are done.

Within a story, tests come first or alongside the code, and all must pass before the story's checkpoint.

### Parallel opportunities

- Phase 2: T005 and T007 and T011 touch different files; T007 and T011 need T006 and T010 written first if tests are not written first.
- User Story 1: T013–T016 are test files that can be written together; T021 is independent of T018 and T019.
- Polish: T033 and T034.

## Implementation Strategy

**MVP first**: Phases 1–3 deliver User Story 1: stop there, check it, and it can already be demonstrated. Then add User Story 2, which is needed before the feature is useful for booking, then User Story 3.

One pull request is planned for the whole feature, with one commit per phase.
