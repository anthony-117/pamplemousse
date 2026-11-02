# pamplemousse — TravelGenie backend

.NET 10 API: location info, search and weighted scoring, itinerary builder, chat agent (Gemini Flash via `Microsoft.Extensions.AI`), TravelGenie's own MCP server, and the Telegram bot channel.

- Hosting: Docker on Google Cloud Run
- Data: Postgres + pgvector (Neon/Supabase), Redis (Upstash)
- External sources: Travelpayouts (flights + hotels, Sprint 2), Wikidata + Wikipedia (attractions), OpenTripMap (restaurants, attractions fallback), Ticketmaster, Open-Meteo, Frankfurter, mledoze/countries, GeoNames, Nager.Date, Passport Index, US State Dept advisories
- Clients: `chocomousse` (Flutter, sibling repo) via OpenAPI; Telegram bot via webhook

Documentation about the code is in [`docs/`](docs/README.md), and the project's rules are in the [constitution](.specify/memory/constitution.md). Non-code material (Figma, presentations, studies) is in the OneDrive FYP folder.

## Structure
```
src/
  Pamplemousse.AppHost/         Aspire: runs the API + Postgres (pgvector) + Redis locally
  Pamplemousse.ServiceDefaults/ logging, health checks, telemetry
  Pamplemousse.Api/             HTTP only: Controllers/, Contracts/, ViewModels/, Mappings/, Errors/, Middleware/
  Pamplemousse.Application/     use cases: Destinations/Queries/<UseCase>/ (query, handler, response), Abstractions/
  Pamplemousse.Domain/          entities, value objects, business rules; no dependencies
  Pamplemousse.Infrastructure/  implements Application's interfaces: providers, cache, reference data
tests/
  Pamplemousse.Domain.Tests/ · Application.Tests/ · Infrastructure.Tests/ · Api.Tests/ · Architecture.Tests/
```
Dependencies point inwards: `Api → Application → Domain`, and `Infrastructure → Application`. `Pamplemousse.Architecture.Tests` fails the build pipeline if a layer breaks this (ADR 0012).

To add a use case: create its folder under `Application/.../Queries/` (query, handler, response), register the handler in `Application/DependencyInjection.cs`, add a controller action with a view model and mapping, and tests for each layer.

## Requirements
- .NET SDK 10.0.113 (pinned in `global.json`, patch roll-forward only; runtime 10.0.13)
- Docker Desktop (only for the Aspire AppHost: Postgres and Redis run as containers)

## Commands
```bash
dotnet build Pamplemousse.slnx
dotnet test Pamplemousse.slnx
dotnet run --project src/Pamplemousse.Api          # API only, no database
dotnet run --project src/Pamplemousse.AppHost      # API + Postgres + Redis + Aspire dashboard (Docker running)
docker build -t pamplemousse .                     # production image (port 8080)
```

## Configuration (`Env.cs`)
Every environment variable the API reads is in `src/Pamplemousse.Api/Env.cs`, each through `Env.Get(name, default)` (environment value if set, otherwise the default).
At startup the nearest `.env` file fills in variables that are not already set. Copy `.env.example` to `.env` and fill in:

| Variable | Used for | Without it |
|---|---|---|
| `TRAVELPAYOUTS_API_TOKEN` | Flights | Flights return a warning |
| `TRAVELPAYOUTS_MARKER` | Partner ID on booking links | Links work, but earn no commission; a warning says so |
| `OPENTRIPMAP_API_KEY` | Restaurants, attractions fallback, their details | Restaurants return a warning; attractions still come from Wikidata |
| `TICKETMASTER_API_KEY` | Events | Events section returns a warning |
| `CORS_ALLOWED_ORIGINS` | Browser origins allowed in production (comma-separated) | Only localhost (Development) |

Weather, exchange rates, holidays, visas, advisories, countries and cities need no key.

## Endpoints
| Method | Path | Purpose |
|---|---|---|
| GET | `/api/v1/destinations?q=rom&limit=10` | City search (≥ 2 characters, accent-insensitive, biggest cities first) |
| GET | `/api/v1/destinations/{id}` | City + country |
| GET | `/api/v1/destinations/{id}/overview?passport=LB&currency=USD&from=&to=` | Weather, exchange rate, holidays, visa, safety advisory for the dates |
| GET | `/api/v1/destinations/{id}/places?category=attraction\|restaurant&limit=20` | Top attractions or restaurants |
| GET | `/api/v1/destinations/{id}/events?from=&to=` | Events during the dates |
| GET | `/api/v1/destinations/{id}/flights?origin=&from=&to=&tripType=&directOnly=&currency=&limit=` | Recent flight prices, cheapest first ([contract](specs/001-flights/contracts/flights-api.md)) |
| GET | `/api/v1/places/{id}` | Place details (`otm:…`) |
| GET | `/api/v1/countries` | Countries + currencies for pickers |
| GET | `/api/v1/system/ping` | Name, version, environment, server time |
| GET | `/alive` | Liveness (all environments, used by Cloud Run) |
| GET | `/health` | Readiness incl. dependencies (Development only) |
| GET | `/openapi/v1.json` | OpenAPI document (Development only) |

City ids are GeoNames ids (Rome = `3169070`). Dates default to a 5-day trip starting in 30 days; trips are at most 30 days.
Each overview section is independent: if a source fails or has no key, that section is `null` and `warnings` says why.
Errors are returned as RFC 9457 Problem Details (`application/problem+json`).

## Data sources and attribution
| Data | Source | Licence / key |
|---|---|---|
| Cities | [GeoNames](https://www.geonames.org) cities15000 | CC BY 4.0, bundled |
| Countries | [mledoze/countries](https://github.com/mledoze/countries) | ODbL, bundled |
| Visa rules | [Passport Index dataset](https://github.com/ilyankou/passport-index-dataset) | MIT, bundled |
| Weather | [Open-Meteo](https://open-meteo.com) (forecast ≤ 16 days, otherwise last year's weather) | CC BY 4.0, no key |
| Exchange rates | [Frankfurter](https://frankfurter.dev) (ECB) | no key |
| Holidays | [Nager.Date](https://date.nager.at) | no key |
| Safety | US State Department travel advisories | public domain, no key |
| Flights | [Travelpayouts](https://www.travelpayouts.com) (Aviasales) Data API: recent prices, not live | free partner token |
| Flight cities, airlines | Travelpayouts public lists, bundled; rebuild with `python scripts/update-flight-data.py` | terms to confirm |
| Attractions | Wikidata, ranked by number of Wikipedia editions; details from the Wikipedia summary; OpenTripMap fills in when Wikidata is down or has too few (ADR 0011) | free, no key |
| Restaurants | OpenTripMap (`foods`: restaurants, cafés, bars; no ratings, prices or hours) | free key |
| Events | Ticketmaster Discovery | free key |

Bundled files live in `src/Pamplemousse.Infrastructure/ReferenceData/Data/` (gzip, embedded in the assembly).

## Git hooks (run once per clone)
```bash
sh scripts/install-hooks.sh
```
This sets `core.hooksPath` to the versioned `.githooks/` folder. The **pre-push** hook runs `dotnet format --verify-no-changes`, the build with all analyzers, every test and the Docker image build
— every CI step; Docker Desktop must be running — and **blocks the push if any of them fails**. (Emergency bypass: `git push --no-verify`; CI still runs.)

## Linting (like Rust's clippy)
Every project builds with the .NET analyzers (`AnalysisLevel=latest-recommended`), **Roslynator** and **Meziantou.Analyzer**,
plus the `.editorconfig` style rules (`EnforceCodeStyleInBuild`). `TreatWarningsAsErrors` makes any finding a build error.
Deliberate exceptions are listed, with a reason, at the bottom of `.editorconfig`.
```bash
dotnet format Pamplemousse.slnx --verify-no-changes   # formatting + style (CI fails if anything would change)
dotnet format Pamplemousse.slnx                       # fix formatting and style automatically
dotnet build Pamplemousse.slnx                        # analyzers
```

## Tests
Every feature ships with tests, and CI requires **every test to pass**:

| Project | Kind | What it covers |
|---|---|---|
| `Pamplemousse.Domain.Tests` | Unit | Pure domain logic (Money, DateRange, weather codes) |
| `Pamplemousse.Application.Tests` | Unit | Each use-case handler with fake providers |
| `Pamplemousse.Infrastructure.Tests` | Unit | Each provider against canned JSON (`StubHttp`, no internet), the cache, reference data |
| `Pamplemousse.Api.Tests` | Unit + integration | `Env`, error mapping; every endpoint through HTTP (`WebApplicationFactory`) |
| `Pamplemousse.Architecture.Tests` | Rules | Layer dependencies, handler naming |

```bash
dotnet test Pamplemousse.slnx
```

## Conventions
- Package versions live only in `Directory.Packages.props`; shared build settings in `Directory.Build.props` (nullable on, warnings as errors).
- The AppHost uses Aspire's NuGet packages (`AspireUseCliBundle=false`). After installing the Aspire CLI (https://get.aspire.dev), switch it to `true`.

## Secrets
Never commit keys. Use `dotnet user-secrets` locally and Google Secret Manager in production.
