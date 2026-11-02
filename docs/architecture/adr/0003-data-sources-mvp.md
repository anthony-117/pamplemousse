# 0003 — MVP data sources
Date: 2026-10-26 · Status: Accepted · Flights superseded by [0010](0010-flights-travelpayouts-data-api.md) (27 Oct 2026)

## Context
Budget as low as possible. Amadeus Self-Service shut down in July 2026; most big travel APIs require partner approval.

## Decision
| Category | Source |
|---|---|
| Flights | OctoTrip MCP |
| Car rentals | OctoTrip rental cars MCP |
| Hotels | Travelpayouts (fallback: HasData Google Hotels) |
| Places | Google Places + OpenTripMap |
| Tours | Skipped for MVP |
| Events | Ticketmaster Discovery |
| Map | Google Maps Flutter SDK |
| Routing | Skipped for MVP |
| Location info | Open-Meteo, Frankfurter, ~~REST Countries~~ mledoze/countries dataset, Nager.Date, Passport Index, ~~travel-advisory.info~~ US State Dept + UK FCDO (amended 2026-10-26 after live checks (OneDrive: `01-research/05-api-exploration.md`)) |

## Consequences
- About $0 in development; set a Google Cloud budget alert.
- OctoTrip is new and may be unreliable → behind `IFlightProvider` / `ICarRentalProvider`. **Live check: it returns 403 `ua_blocked` to automated clients — access must be requested (xltnapps@gmail.com) before Sprint 2.**
- Travelpayouts hotel data may be links only → verify after approval.

See data sources study (OneDrive: `01-research/04-data-sources-study.md`).
