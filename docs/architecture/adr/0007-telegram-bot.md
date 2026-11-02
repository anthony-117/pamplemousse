# 0007 — Telegram bot with full feature parity
Date: 2026-10-27 · Status: Accepted

## Context
Many users would rather plan in a chat app than install another app. The user wants a Telegram bot that can do everything the website can, connected directly to the .NET backend.

## Decision
- A Telegram bot (created with @BotFather) sends updates by **webhook** to `pamplemousse` at `POST /api/v1/telegram/webhook`. The request's `X-Telegram-Bot-Api-Secret-Token` header is checked before anything runs.
- The bot is a thin adapter: it turns Telegram messages and buttons into calls to the **same** services the app uses (search, overview, scoring, itinerary, plans) and to the same chat agent and MCP tools. No logic is duplicated.
- Features: city search and overview, places, plan setup (preferences via inline buttons), ranked results, itinerary, assistant chat, my plans, booking links.
- Telegram accounts are linked to TravelGenie accounts with a one-time code from the app's Profile screen, so plans appear in both.
- Library: `Telegram.Bot` (NuGet, MIT). Bot token in Google Secret Manager / `dotnet user-secrets`.
- Lives in `Pamplemousse.Api/Features/Telegram/`; Telegram's API calls are in `Infrastructure`.

## Consequences
- Cost: free (Telegram Bot API has no fees). Cloud Run must be reachable over HTTPS, which it is by default.
- Telegram's UI is text + buttons + images, so rich screens (map, sliders) become simplified messages or links to the web app.
- Adds Sprint 9 (6–19 Apr 2027) and shortens testing to 2 weeks.
