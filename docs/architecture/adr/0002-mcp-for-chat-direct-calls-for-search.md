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
