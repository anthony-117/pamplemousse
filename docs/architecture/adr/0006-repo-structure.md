# 0006 — Repository structure
Date: 2026-10-26 · Status: Accepted · Backend layers superseded by [0012](0012-clean-architecture-layers.md), document location by [0013](0013-code-docs-in-the-repo.md) (2 Nov 2026)

## Context
Two Git repos (backend `pamplemousse`, frontend `chocomousse`). Git and build folders inside OneDrive risk corruption and heavy syncing.

## Decision
- Repos live in `C:\Users\antho\Desktop\travel_genie\`, backed up on GitHub. Documents stay in the OneDrive FYP folder.
- **Backend:** layers + feature folders, run locally with **.NET Aspire**.
- **Frontend:** feature-first folders, **Riverpod** for state, go_router for navigation, generated OpenAPI client.

### pamplemousse (.NET 10)
```
pamplemousse/
├── Pamplemousse.slnx
├── global.json                  # pins .NET 10 SDK
├── Directory.Build.props        # shared settings (nullable, warnings as errors)
├── Directory.Packages.props     # central NuGet versions
├── Dockerfile · .github/workflows/
├── src/
│   ├── Pamplemousse.AppHost/          # Aspire: API + Postgres + Redis locally
│   ├── Pamplemousse.ServiceDefaults/  # logging, health checks, telemetry
│   ├── Pamplemousse.Api/              # endpoints grouped by feature
│   │   └── Features/ Destinations/ Search/ Itineraries/ Chat/ Users/
│   │       (+ /mcp endpoint: own MCP server tools)
│   ├── Pamplemousse.Domain/           # entities + scoring engine (pure C#)
│   └── Pamplemousse.Infrastructure/
│       ├── Persistence/   (EF Core, Postgres, pgvector)
│       ├── Caching/       (Redis)
│       ├── Providers/     OctoTrip/ Travelpayouts/ GooglePlaces/ OpenTripMap/ Ticketmaster/ LocationInfo/
│       └── AI/            (Gemini chat + embeddings)
└── tests/
    ├── Pamplemousse.Domain.Tests/     # scoring unit tests
    └── Pamplemousse.Api.Tests/        # integration tests (Testcontainers)
```

### chocomousse (Flutter)
```
chocomousse/
├── lib/
│   ├── main.dart
│   ├── app/          # go_router, theme (Figma tokens), startup
│   ├── core/         # networking, errors, shared widgets, utils
│   └── features/
│       ├── destination/   data/ · presentation/ (screens, widgets, Riverpod providers)
│       ├── search/
│       ├── itinerary/
│       ├── chat/
│       └── profile/
├── packages/api_client/   # generated from pamplemousse's OpenAPI
├── assets/
├── test/ · integration_test/
└── android/ ios/ web/
```

## Consequences
- Scoring engine is pure and unit-testable; providers are swappable behind interfaces.
- Each sprint adds a feature folder on both sides.
- Aspire needs Docker running.
