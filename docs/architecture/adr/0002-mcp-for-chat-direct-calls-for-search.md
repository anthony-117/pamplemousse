# 0002 — MCP for the chatbot, direct typed calls for core search
Date: 2026-10-26 · Status: Accepted

## Context
Weighted search ("Rome Complete: 96% match") must be stable and explainable. The chatbot must be able to act on the plan.

## Decision
- Core search calls providers through typed C# provider interfaces, caches results, and scores them with deterministic C# code.
- The same services are exposed as tools on our own MCP server for the chatbot (`search_flights`, `swap_day`, …).
- External MCP servers (OctoTrip) are consumed by the backend as an MCP client, never by the Flutter app.

## Consequences
- Rankings never depend on the LLM.
- Providers can be swapped without touching the app or the chatbot.

## Note, 2026-11-06 (spec 008)
The itinerary follows the same rule: the days, the total and the plan's match are built by `PlanBuilder` in Domain,
deterministic C# with no outside dependency, from the ranked lists of spec 007. The same inputs always give the same
plan. The assistant (TG-11) will change a plan through these rules, never by writing days itself.
