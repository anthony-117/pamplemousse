# 0014 — No live flight source for the MVP
Date: 2026-11-02 · Status: Superseded by 0016

## Context
The Travelpayouts Data API (ADR 0010) holds cached fares built from what people search. Thin routes are often empty: Beirut to 11 cities, for one January and one February trip, returned a fare on 3 of 22 searches, even with the any-return level.

Options checked on 2 Nov 2026:

| Source | Finding |
|---|---|
| Aviasales Search API (live) | Requires 50,000 confirmed monthly users, no exceptions |
| Kiwi.com API (live) | Requires 50,000 monthly users |
| Trip.com through Travelpayouts | Commission links only; no price data on its program page or on Travelpayouts' list of brands with APIs |
| SerpApi (Google Flights data) | 250 free searches a month, but it relies on scraping Google; no legal cover on the free plan |
| SearchApi | 100 free requests, then $40 a month; same concern |
| Duffel | Test mode has made-up prices; live terms not confirmed |

## Decision
Stay with Travelpayouts. No live flight source for the MVP. Routes with no fares show a message and a link to live prices on the booking site.

## Consequences
- $0, one source, commission kept, and no dependence on scraping.
- Thin routes, Beirut especially, often show no price. Demos should use routes that work, such as Paris–Rome or London–New York, and the limit is stated in the report.
- `IFlightProvider` keeps the source replaceable. Kiwi.com and the Aviasales Search API are the upgrade path at 50,000 monthly users.
- The draft of spec 002 (a SerpApi fallback) is not pursued.
