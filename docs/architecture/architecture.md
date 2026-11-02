# Architecture Overview (draft)

```
 ┌──────────────────────────┐        HTTPS (REST + SSE/SignalR)        ┌──────────────────────────────────────┐
 │ chocomousse (Flutter)    │ ───────────────────────────────────────▶ │ pamplemousse (.NET 10 API)           │
 │ Android · iOS · Web      │   OpenAPI-generated Dart client          │                                      │
 │ Google Maps SDK          │                                          │  Location info service               │
 └──────────────────────────┘                                          │  Search + weighted scoring (C#)      │
                                                                       │  Itinerary builder                   │
                                                                       │  Chat agent (Gemini Flash via        │
                                                                       │    Microsoft.Extensions.AI)          │
                                                                       │  Own MCP server (tools for the agent)│
                                                                       │  Provider interfaces ───────────┐    │
                                                                       └──────┬──────────┬───────────────┼────┘
                                                                              │          │               │
                                                                    Postgres + pgvector  Redis    External sources
                                                                    (Neon/Supabase)    (Upstash)  (MCP + REST)
```

## External sources (behind provider interfaces)
| Interface | Source | Protocol |
|---|---|---|
| `IFlightProvider` | Travelpayouts (Aviasales) Data API — cached prices (ADR 0010) | REST |
| `ICarRentalProvider` | OctoTrip rental cars | MCP |
| `IHotelProvider` | Travelpayouts | REST |
| `IPlaceProvider` | Attractions: Wikidata (ADR 0011), fallback OpenTripMap · Restaurants: OpenTripMap (Google Places on hold) | SPARQL + REST |
| `IEventProvider` | Ticketmaster Discovery | REST |
| `ILocationInfoProvider` | Open-Meteo, Frankfurter, REST Countries, Nager.Date, Passport Index, travel-advisory.info | REST / CSV |

## Telegram channel (Sprint 9)
```
 Telegram user ──▶ Telegram Bot API ──HTTPS webhook──▶ pamplemousse /api/v1/telegram/webhook
                                                         │  (secret-token header checked)
                                                         ▼
                                     same chat agent, search, itinerary and plan services
```
The bot is another client of the same backend, like the Flutter app: no separate server. See [ADR 0007](adr/0007-telegram-bot.md).

## Rules
- The Flutter app and the Telegram bot only talk to `pamplemousse`.
- Scoring is deterministic C# code; the LLM never ranks.
- Prices are fetched live (cached ~1 h); place data cached ~24 h.
- RAG (pgvector + Gemini embeddings) stores user trips and preferences only.
- Secrets live in the host's secret manager, never in the app or in these documents.

Diagrams (draw.io, Mermaid, PNG exports) go in `diagrams/`. Decisions go in `adr/`.
