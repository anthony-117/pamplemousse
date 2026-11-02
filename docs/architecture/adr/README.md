# Architecture Decision Records

One short file per decision: `NNNN-title.md`. Never edit an accepted ADR's decision; write a new one that supersedes it.

Template:
```
# NNNN — Title
Date: YYYY-MM-DD · Status: Proposed | Accepted | Superseded by NNNN
## Context
## Decision
## Consequences
```

| # | Decision | Status |
|---|---|---|
| [0001](0001-dotnet-and-flutter.md) | .NET 10 backend + Flutter frontend | Accepted |
| [0002](0002-mcp-for-chat-direct-calls-for-search.md) | MCP for the chatbot, direct typed calls for core search | Accepted |
| [0003](0003-data-sources-mvp.md) | MVP data sources | Accepted (flights superseded by 0010) |
| [0004](0004-hosting.md) | Hosting: Cloud Run, Firebase Hosting, Neon, Upstash | Accepted (backend and data superseded by 0015) |
| [0005](0005-ai-stack.md) | AI stack: Gemini Flash, Gemini embeddings, pgvector | Accepted |
| [0006](0006-repo-structure.md) | Repo structure: layers + feature folders, Riverpod, Aspire, repos outside OneDrive | Accepted (backend layers superseded by 0012) |
| [0007](0007-telegram-bot.md) | Telegram bot with full feature parity, webhook into the .NET backend | Accepted |
| [0008](0008-sprint-1-config-and-reference-data.md) | Env file per repo; bundled reference data; missing keys = warnings | Accepted |
| [0009](0009-linting-and-testing.md) | Clippy-style linting and tests for every feature; every test must pass in CI | Accepted |
| [0010](0010-flights-travelpayouts-data-api.md) | Flights from the Travelpayouts (Aviasales) Data API, cached prices + partner links | Accepted |
| [0011](0011-attractions-from-wikidata.md) | Attractions from Wikidata ranked by fame, OpenTripMap as fallback; one event per show | Accepted |
| [0012](0012-clean-architecture-layers.md) | Four layers (Api, Application, Domain, Infrastructure), one service, plain handlers | Accepted |
| [0013](0013-code-docs-in-the-repo.md) | Code documentation lives in the repo; OneDrive keeps non-code material | Accepted |
| [0014](0014-no-live-flight-source-for-mvp.md) | No live flight source for the MVP; thin routes link to live prices | Accepted |
| [0015](0015-factory-mousse-and-oracle-hosting.md) | Backend on an Oracle Always Free server, released from `factory_mousse` | Accepted |
