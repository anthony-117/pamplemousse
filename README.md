# pamplemousse — TravelGenie backend

.NET 10 API: location info, search and weighted scoring, itinerary builder, chat agent (Gemini Flash via `Microsoft.Extensions.AI`), TravelGenie's own MCP server, and the Telegram bot channel.

- Hosting: Docker on Google Cloud Run
- Data: Postgres + pgvector (Neon/Supabase), Redis (Upstash)
- External sources: OctoTrip MCP (flights, cars), Travelpayouts (hotels), Google Places + OpenTripMap, Ticketmaster, Open-Meteo, Frankfurter, mledoze/countries, Nager.Date, Passport Index, US State Dept + UK FCDO advisories
- Clients: `chocomousse` (Flutter, sibling repo) via OpenAPI; Telegram bot via webhook

Project docs (studies, architecture, ADRs, schemas) live in the OneDrive FYP folder: `Final Year Project - USEK - 2026-2027`.

## Structure
```
src/
  Pamplemousse.AppHost/         Aspire: runs the API + Postgres (pgvector) + Redis locally
  Pamplemousse.ServiceDefaults/ logging, health checks, telemetry
  Pamplemousse.Api/             endpoints, grouped under Features/<feature>/
  Pamplemousse.Domain/          entities + scoring engine (pure C#)
  Pamplemousse.Infrastructure/  persistence, cache, providers, AI
tests/
  Pamplemousse.Domain.Tests/    unit tests
  Pamplemousse.Api.Tests/       integration tests (WebApplicationFactory)
```

## Requirements
- .NET SDK 10.0.403+ (pinned in `global.json`)
- Docker Desktop (only for the Aspire AppHost: Postgres and Redis run as containers)

## Commands
```bash
dotnet build Pamplemousse.slnx
dotnet test Pamplemousse.slnx
dotnet run --project src/Pamplemousse.Api          # API only, no database
dotnet run --project src/Pamplemousse.AppHost      # API + Postgres + Redis + Aspire dashboard (Docker running)
docker build -t pamplemousse .                     # production image (port 8080)
```

## Endpoints (Sprint 0)
| Method | Path | Purpose |
|---|---|---|
| GET | `/api/v1/system/ping` | Name, version, environment, server time |
| GET | `/alive` | Liveness (all environments, used by Cloud Run) |
| GET | `/health` | Readiness incl. dependencies (Development only) |
| GET | `/openapi/v1.json` | OpenAPI document (Development only) |

Errors are returned as RFC 9457 Problem Details (`application/problem+json`).

## Conventions
- Package versions live only in `Directory.Packages.props`; shared build settings in `Directory.Build.props` (nullable on, warnings as errors).
- The AppHost uses Aspire's NuGet packages (`AspireUseCliBundle=false`). After installing the Aspire CLI (https://get.aspire.dev), switch it to `true`.

## Secrets
Never commit keys. Use `dotnet user-secrets` locally and Google Secret Manager in production.
