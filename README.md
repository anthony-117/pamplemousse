# pamplemousse — TravelGenie backend

.NET 10 API: location info, search and weighted scoring, itinerary builder, chat agent (Gemini Flash via `Microsoft.Extensions.AI`), TravelGenie's own MCP server, and the Telegram bot channel.

- Hosting: Docker on Google Cloud Run
- Data: Redis (12-hour hotel and 1-day car search results, locks, request counts) and Firestore (hotels, cities, price history, car searches), ADR 0017; Postgres + pgvector for the chatbot later
- External sources: Booking COM API on RapidAPI (hotels and rental cars, ADR 0016), Travelpayouts (flights), Wikidata + Wikipedia (attractions), OpenTripMap (restaurants, attractions fallback, places near hotels for weighted search), Ticketmaster, Open-Meteo, Frankfurter, mledoze/countries, GeoNames, Nager.Date, Passport Index, US State Dept advisories
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
| `TRAVELPAYOUTS_MARKER` | Partner ID on booking links | Links work, but earn no commission; the server log says so |
| `OPENTRIPMAP_API_KEY` | Restaurants, attractions fallback, their details, places near hotels | Restaurants return a warning; attractions still come from Wikidata; weighted hotel search counts food, nature and nightlife as average |
| `TICKETMASTER_API_KEY` | Events | Events section returns a warning |
| `RAPIDAPI_KEY` | Hotels and cars (Booking COM API) | Hotels and cars return saved prices or a warning; car pick-up is the city centre only |
| `FIREBASE_PROJECT_ID` | Firebase project: sign-in (ADR 0019) and Firestore for hotels, cities, price history, car searches and travel profiles | Every route that needs a sign-in answers 503; data is kept in memory only; the log says so |
| `GOOGLE_APPLICATION_CREDENTIALS` | Full path to the Firebase service account JSON, kept outside the repo; also deletes Firebase accounts | Google's default credentials are tried |
| `GEMINI_API_KEY` | Assistant (Google AI Studio, free tier) | The assistant answers with a warning |
| `GEMINI_MODEL` | Gemini model for the assistant, default `gemini-2.5-flash` | The default is used |
| `CORS_ALLOWED_ORIGINS` | Browser origins allowed in production (comma-separated) | Only localhost (Development) |

Weather, exchange rates, holidays, visas, advisories, countries and cities need no key.

## Endpoints
Every route needs `Authorization: Bearer <Firebase ID token>` from a confirmed account, apart from ping, `/alive`, `/health` and
the API reference. A refusal is Problem Details with a `code`: `sign-in-required` (401), `email-not-confirmed` (403),
`recent-sign-in-required` (401) or `sign-in-not-configured` (503) ([contract](specs/009-accounts/contracts/accounts-api.md)).

| Method | Path | Purpose |
|---|---|---|
| GET | `/api/v1/destinations?q=rom&limit=10` | City search (≥ 2 characters, accent-insensitive, biggest cities first) |
| GET | `/api/v1/destinations/{id}` | City + country |
| GET | `/api/v1/destinations/{id}/overview?passport=LB&currency=USD&from=&to=` | Weather, exchange rate, holidays, visa, safety advisory for the dates |
| GET | `/api/v1/destinations/{id}/places?category=attraction\|restaurant\|nature\|nightlife&limit=20` | Top attractions, restaurants, parks or nightlife |
| GET | `/api/v1/destinations/{id}/events?from=&to=` | Events during the dates |
| GET | `/api/v1/destinations/{id}/flights?origin=&from=&to=&tripType=&directOnly=&currency=&limit=&sort=&budget=&culture=&food=&nature=&nightlife=&comfort=&tripBudget=` | Recent flight prices, cheapest first or by match ([contract](specs/001-flights/contracts/flights-api.md), [weighted search](specs/007-weighted-search/contracts/weighted-search-api.md)) |
| GET | `/api/v1/destinations/{id}/hotels?from=&to=&adults=&currency=&sort=&minStars=&minReview=&minPrice=&maxPrice=&stayType=&freeCancellation=&page=&budget=&culture=&food=&nature=&nightlife=&comfort=&tripBudget=` | Hotels with the total for the stay, sorted and filtered on stored data, or ranked by match ([contract](specs/002-hotels/contracts/hotels-api.md), [weighted search](specs/007-weighted-search/contracts/weighted-search-api.md)) |
| GET | `/api/v1/hotels/{hotelId}?from=&to=&adults=&currency=` | One hotel's details and its Booking.com link (`bk:…`) |
| GET | `/api/v1/destinations/{id}/car-places` | Where a rental car can be picked up: the city centre, then airports within 50 km |
| GET | `/api/v1/destinations/{id}/cars?from=&to=&place=&driverAge=&currency=&sort=&size=&gearbox=&minSeats=&minBags=&company=&pickUpType=&maxPrice=&freeCancellation=&page=&budget=&culture=&food=&nature=&nightlife=&comfort=&tripBudget=` | Rental cars with the total for the rental, sorted and filtered on one saved search, or ranked by match ([contract](specs/004-cars/contracts/cars-api.md), [weighted search](specs/007-weighted-search/contracts/weighted-search-api.md)) |
| GET | `/api/v1/places/{id}` | Place details (`otm:…`) |
| POST | `/api/v1/assistant/messages` | Trip assistant: answers the last message with TravelGenie's data (Gemini + tools), with cards into the screens ([contract](specs/003-assistant/contracts/assistant-api.md)) |
| GET | `/api/v1/countries` | Countries + currencies for pickers |
| GET | `/api/v1/me` | The signed-in traveller's travel profile, made on the first call ([contract](specs/009-accounts/contracts/accounts-api.md)) |
| PUT | `/api/v1/me` | Replace the name, passport, home currency, home city, default preferences and pace |
| DELETE | `/api/v1/me` | Delete the profile, the plans and the sign-in (needs a sign-in from the last 5 minutes) |
| POST | `/api/v1/plans` | Build a day-by-day plan from the best hotel and flight for the preferences, saved as a draft ([contract](specs/008-itinerary/contracts/itinerary-api.md)) |
| GET | `/api/v1/plans` | The traveller's plans, newest first (50 at most) |
| GET | `/api/v1/plans/{id}` | A plan as saved; no source is called |
| PATCH | `/api/v1/plans/{id}` | Rename, or mark draft or planned |
| DELETE | `/api/v1/plans/{id}` | Delete a plan |
| GET | `/api/v1/plans/{id}/alternatives/{hotels\|flights\|cars}` | The kept ranked list to swap from (the first car list runs the car search once) |
| PUT | `/api/v1/plans/{id}/{hotel\|flight\|car}` | Swap in another offer and rebuild the days, total and match |
| DELETE | `/api/v1/plans/{id}/car` | Remove the car |
| GET | `/api/v1/system/ping` | Name, version, environment, server time |
| GET | `/alive` | Liveness (all environments, used by Cloud Run) |
| GET | `/health` | Readiness incl. dependencies (Development only) |
| GET | `/openapi/v1.json` | OpenAPI document (Development only) |
| GET | `/scalar` | [Scalar](https://scalar.com) API reference: browse and try every endpoint (Development only) |

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
| Hotels, rental cars | [Booking COM API](https://rapidapi.com/ntd119/api/booking-com18) on RapidAPI: live Booking.com prices (ADR 0016). Free Basic plan, 530 requests a month shared by hotels and cars; new calls stop at 95% | RapidAPI key |
| Flights | [Travelpayouts](https://www.travelpayouts.com) (Aviasales) Data API: recent prices, not live | free partner token |
| Flight cities, airlines | Travelpayouts public lists, bundled; rebuild with `python scripts/update-flight-data.py` | terms to confirm |
| Attractions | Wikidata, ranked by number of Wikipedia editions; details from the Wikipedia summary; OpenTripMap fills in when Wikidata is down or has too few (ADR 0011) | free, no key |
| Restaurants | OpenTripMap (`foods`: restaurants, cafés, bars; no ratings, prices or hours) | free key |
| Events | Ticketmaster Discovery | free key |
| Assistant | Google Gemini through `Microsoft.Extensions.AI` (ADR 0005, ADR 0018); answers only from the sources above, called as tools | Google AI Studio key |

Bundled files live in `src/Pamplemousse.Infrastructure/ReferenceData/Data/` (gzip, embedded in the assembly).

## Releases
Pushing a tag such as `v1.2.0` runs the tests and publishes an ARM64 image to GitHub's container registry. That doesn't make it live: [`factory_mousse`](https://github.com/anthony-117/factory_mousse) decides which version runs.

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
