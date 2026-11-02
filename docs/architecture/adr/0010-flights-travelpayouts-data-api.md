# 0010 — Flights from the Travelpayouts (Aviasales) Data API
Date: 2026-10-27 · Status: Accepted · Supersedes the flights row of [0003](0003-data-sources-mvp.md)

## Context
ADR 0003 chose the OctoTrip MCP for flights. Live checks return 403 `ua_blocked` to automated clients, so the backend cannot use it without OctoTrip's permission. Alternatives compared on 27 Oct 2026:
- Booking.com Demand API: Managed Affiliate Partner contract only, aimed at sites with real traffic; AI use needs written approval.
- Kiwi Tequila, Skyscanner, Amadeus: approval or commercial contracts; closed to new small developers.
- Duffel: self-serve, free test mode, paid per booking when live. A booking API, not a price reader.
- Google Flights scrapers (SerpApi and similar): small free tiers, then paid; depend on scraping.
- Travelpayouts Data API: self-serve with a free partner account; free.

## Decision
Flights come from the **Travelpayouts (Aviasales) Data API**, `prices_for_dates` (v3), behind `IFlightProvider`.
- Auth: partner token (`X-Access-Token` header). Booking links carry the partner marker.
- Env variables in `pamplemousse/.env`: `TRAVELPAYOUTS_API_TOKEN`, `TRAVELPAYOUTS_MARKER`. Missing values produce a warning, like the other providers (ADR 0008).
- Results are cached in Redis/memory (the data is itself a cache).

## Consequences
- $0 and no approval needed, the same account serves hotels (ADR 0003).
- Prices are **cached**, not live: the cheapest fares Aviasales users found recently (about 48 h, up to 7 days depending on the query). Some dates or routes return nothing. The UI says "recent price" and every flight opens the booking site through the partner link to see the live price.
- Live search needs 50,000 monthly active users, so it is out of reach for the MVP.
- Rate limits exist (429 on excess); the provider retries with back-off and relies on the cache.
- No in-app flight booking. Duffel test mode may be added later only to demonstrate a booking flow.
- OctoTrip stays an optional extra if access is ever granted; nothing depends on it.
- Car rentals (also OctoTrip in 0003) are still open; recommendation: affiliate links only, since TG-08 is "Could".
