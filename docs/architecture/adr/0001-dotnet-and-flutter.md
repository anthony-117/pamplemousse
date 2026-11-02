# 0001 — .NET 10 backend + Flutter frontend
Date: 2026-10-26 · Status: Accepted

## Context
TravelGenie needs a backend that calls many providers in parallel and runs an AI agent, and a frontend usable mostly on phones.

## Decision
Backend in .NET 10 (`pamplemousse`), frontend in Flutter (`chocomousse`), each in its own Git repository. The API contract is OpenAPI; the Dart client is generated from it.

## Consequences
- Strong typing, performance, official C# MCP SDK, `Microsoft.Extensions.AI`.
- AI examples are mostly Python/TS; provider SDKs are hand-written HTTP clients.
- Flutter web is weak for SEO; chat streaming needs SSE or SignalR.

See tech stack study (OneDrive: `01-research/02-tech-stack-study.md`).
