# Implementation Plan: Flight prices for a trip

**Branch**: `001-flights` | **Date**: 2026-11-02 | **Spec**: [spec.md](spec.md)

**Status**: Awaiting owner approval. No code starts before it (constitution, Development Workflow).

## Summary

Add `GET /api/v1/destinations/{id}/flights`. The traveller gives a departure city and dates; the API maps both
cities to flight codes, reads recent prices from the Travelpayouts Data API (ADR 0010), and returns them
cheapest first, with labelled nearby dates when the exact dates have no fare, and booking links.

## Technical Context

**Language/Version**: C# on .NET 10 (SDK 10.0.113)

**Primary Dependencies**: No new packages. Typed `HttpClient`, `JsonCache`, and the standard resilience handler already set by ServiceDefaults

**Storage**: No database. Responses cached about 1 hour; city and airline lists embedded as compressed files

**Testing**: xUnit: Domain, Application, Infrastructure with `StubHttp`, Api with `WebApplicationFactory`, plus the Architecture tests

**Target Platform**: Linux container on Google Cloud Run

**Project Type**: Web service

**Performance Goals**: First request under 5 s, repeated request under 1 s (SC-003, SC-004)

**Constraints**: About $0; never call the internet from tests; a source failure is a warning, not an error

**Scale/Scope**: One endpoint, one provider, about 3,600 flight cities, about 1,200 airlines

**Unconfirmed until a token exists**: response field names, booking link format, supported currencies, month-level date search, whether fares differ by passengers. See [research.md](research.md), R-7. Task 1 of implementation confirms these against the real API.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | How this plan meets it | Result |
|---|---|---|
| I. Layered Architecture | Handler in Application; provider and city lookup behind Application interfaces; rules in Domain; controller only translates | Pass |
| II. Every Feature Is Tested | Unit and endpoint tests listed in Project Structure; failure paths covered; canned JSON only; live check in [quickstart.md](quickstart.md) | Pass |
| III. Zero Lint Findings | Normal build gate, no exceptions needed | Pass |
| IV. Partial Failure | Provider failure or missing token gives an empty list and a warning through `SectionLoader`; 5 s timeout | Pass |
| V. Low Cost First | Free partner account; responses cached; no paid service | Pass |
| VI. Stable API Contract | New route under `/api/v1`; nothing existing changes; view models with hand mapping; [contract](contracts/flights-api.md) | Pass |
| VII. Simplicity | No new framework; embedded files like the existing reference data; no database | Pass |
| Configuration and Security | `TRAVELPAYOUTS_API_TOKEN` and `TRAVELPAYOUTS_MARKER` through `Env.cs`, added to `.env.example`; token sent in a header so it never appears in logged URLs; no personal data logged | Pass |

Re-checked after design: all pass. No violations to justify.

## Project Structure

### Documentation (this feature)

```text
specs/001-flights/
├── spec.md · plan.md · research.md · data-model.md · quickstart.md
├── contracts/flights-api.md
└── tasks.md             # next step, after approval
```

### Source Code (repository root)

```text
src/
├── Pamplemousse.Api/
│   ├── Controllers/DestinationsController.cs   # new GetFlightsAsync action
│   ├── Contracts/DestinationRequests.cs        # new SearchFlightsRequest
│   ├── ViewModels/FlightViewModels.cs          # new
│   ├── Mappings/FlightMappings.cs              # new
│   └── Env.cs · Program.cs                     # token and marker
├── Pamplemousse.Application/
│   ├── Abstractions/Providers.cs               # IFlightProvider, IFlightCities
│   ├── Flights/Queries/SearchFlights/          # query, handler, response
│   └── DependencyInjection.cs
├── Pamplemousse.Domain/
│   └── Flights/                                # FlightOffer, FlightSearch, FlightCity, TripType rules
└── Pamplemousse.Infrastructure/
    ├── Providers/TravelpayoutsFlightProvider.cs # new
    ├── ReferenceData/FlightCityStore.cs         # new: nearest-city lookup, airline names
    ├── ReferenceData/Data/flight-cities.json.gz · airlines.json.gz
    └── ExternalApiOptions.cs · DependencyInjection.cs

scripts/update-flight-data.py                    # rebuilds the two embedded files

tests/
├── Pamplemousse.Domain.Tests/Flights/           # search rules
├── Pamplemousse.Application.Tests/Flights/      # handler: exact and nearby order, labels, empty result, warnings
├── Pamplemousse.Infrastructure.Tests/Providers/ # canned JSON: fields, bad prices, duplicates, missing token, 429, broken JSON, cache
├── Pamplemousse.Infrastructure.Tests/ReferenceData/ # Rome→ROM, Byblos→BEY, nothing within range
└── Pamplemousse.Api.Tests/Destinations/         # endpoint: 200, 400s naming the field, 404, warnings
```

**Structure Decision**: Follows ADR 0012. Flights get their own `Application/Flights/` folder, and the route stays under destinations because a search belongs to a destination city.

## Delivery order

1. **Confirm with a real token** (needs your Travelpayouts account), and correct this plan if anything differs.
2. **Domain and flight cities** (no external calls).
3. **Story 1:** provider, handler, endpoint, including nearby dates and failure paths.
4. **Story 2:** booking links and partner marker.
5. **Story 3:** direct-only filter.
6. **Checks and docs:** the live checks in `quickstart.md`, README endpoints and data sources, keys checklist, sprint review.

Each step ends with all tests passing. The whole feature is one pull request unless it grows too large.

## Risks

| Risk | Effect | Response |
|---|---|---|
| Cached fares are sparse on quiet routes | Empty results | Nearby dates, a search link, and SC-001 measures it. If it fails, we discuss a second source |
| Nearest airport is small (Assisi → Perugia) | Few or no fares although Rome has many | Accept for now; the response says which airport was used. Revisit if live checks show it matters |
| Field names differ from what we expect | Rework in one class | Everything source-specific stays in `TravelpayoutsFlightProvider`; confirmed first |
| Use of Travelpayouts lists is restricted | Replace the embedded files | Check terms with the account (R-3) |

## Complexity Tracking

None. No constitution violations.
