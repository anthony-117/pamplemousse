# Sprint 0 review — "Set up everything so every sprint can ship"
Dates: 26–27 Oct 2026 (planned 27 Oct – 9 Nov) · Status: done except deployment (carried over)

## Goal
TG-01 — As a developer, I want both repos, CI and a deployed "hello world" so that every sprint can ship. Plus the groundwork before any code: studies, architecture decisions, schemas and an approved design.

## Delivered
| Item | Result |
|---|---|
| Backend repo `pamplemousse` | ✅ .NET 10 (SDK 10.0.113) + Aspire 13.6.4: AppHost with Postgres (pgvector) and Redis, ServiceDefaults, Api (feature folders), Domain, Infrastructure, tests. `/api/v1/system/ping`, `/alive`, Problem Details errors, OpenAPI in development. Dockerfile for Cloud Run (port 8080) |
| App repo `chocomousse` | ✅ Flutter 3.47.0 / Dart 3.13.0 for Android, iOS and web; feature-first structure; Riverpod + dio; go_router with Discover · Plans · Assistant · Profile; floating icon-only nav on phones, web header on wide screens; Profile shows the backend ping |
| Design applied to code | ✅ Theme tokens from Figma v0.2 (indigo #4338CA, coral #FF7A59), Plus Jakarta Sans bundled, logo v0.3 mark |
| CI (GitHub Actions) | ✅ Backend: restore, format check, build, test, Docker image. App: format check, analyze, test, web release build |
| Deployed "hello world" | ❌ Not done — needs the user's Google Cloud (Cloud Run) and Firebase Hosting accounts. Carried over |

## Planning and design delivered
| Area | Result |
|---|---|
| Research (`01-research`) | Concept brief, tech-stack, hosting, data-sources, live API exploration, cost/budget study (MVP ≈ $0/month) |
| Architecture (`02-architecture/adr`) | ADR 0001 .NET + Flutter · 0002 MCP for chat, direct calls for search · 0003 MVP data sources · 0004 hosting · 0005 AI stack · 0006 repo structure · 0007 Telegram bot (Sprint 9) |
| Schemas (`03-schemas`) | Domain model, database schema, Sprint 1 API contract |
| Management (`00-management`) | Agile process, backlog TG-01…TG-16, timeline (2-week sprints), API-keys checklist, dev-environment guide |
| Figma | Design v0.2 (mobile 01–13, web W1–W9) **approved 27 Oct**; logo v0.3 **approved 26 Oct**; project presentation final |

## Pull requests
- pamplemousse #1 `chore/sdk-10.0.113` — pin SDK 10.0.113 / runtime 10.0.13 · merged
- chocomousse #1 `chore/flutter-3.47.0` — target Flutter 3.47.0 / Dart 3.13.0 · merged

## Quality
- Backend: 10 tests (ping/alive endpoints, `Money` value object). Warnings are errors.
- App: 4 tests (app shell navigation, backend status). Analyzer clean.
- Hardened later on 27 Oct (ADR 0009): strict linting in both repos, tests for every feature, CI requires every test to pass (no coverage %), pre-push hook runs every CI step. Today: backend 101 tests, app 39 tests.

## Problems met and how they were handled
- Aspire CLI install was blocked → Aspire used through NuGet packages (`AspireUseCliBundle=false`).
- OctoTrip MCP (flights, cars) rejects automated clients (403) → access must be requested; Sprint 2 risk.
- Amadeus Self-Service shut down (July 2026), REST Countries v3 deprecated, travel-advisory.info dead → replaced by mledoze countries dataset and US State Dept advisories.
- Figma Starter plan limits (3 pages, MCP call cap) → moved to the education account.
- Toolchain versions changed mid-sprint (Flutter 3.47.0, .NET SDK 10.0.113) → pinned in both repos through PRs.

## Carry-over / next
- Deploy: backend to Cloud Run, app to Firebase Hosting (needs your accounts).
- ~~Request OctoTrip access~~ — not needed: flights use the Travelpayouts Data API (ADR 0010).
- ~~Fill `OPENTRIPMAP_API_KEY` and `TICKETMASTER_API_KEY`~~ — done 27 Oct, both verified live.
- ~~Approve the "Sprint 1 — as built" Figma page~~ — approved 27 Oct.
- Sprint 1 (city overview) is already built — see `sprint-01.md`.
