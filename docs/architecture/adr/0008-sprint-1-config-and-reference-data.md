# 0008 — Configuration files and bundled reference data (Sprint 1)
Date: 2026-10-27 · Status: Accepted

## Context
Sprint 1 needs city search, country facts and visa rules, plus API keys that do not exist yet. Postgres would only hold read-only reference data at this point; no user data is stored before Sprint 3–4.

## Decision
1. **One env file per repo.** `pamplemousse/src/Pamplemousse.Api/Env.cs` and `chocomousse/lib/env.dart` list every environment value the code reads, each through `Get(name, default)` (environment value if set and not blank, otherwise the default).
   - Backend: values from the process environment, filled from the nearest `.env` (git-ignored; `.env.example` committed). Keys: `OPENTRIPMAP_API_KEY`, `TICKETMASTER_API_KEY`, `CORS_ALLOWED_ORIGINS`.
   - App: values from `assets/env/app.env` (git-ignored; `app.env.example` committed). Settings only (`API_BASE_URL`, `DEFAULT_PASSPORT`, `DEFAULT_CURRENCY`) — the file ships inside the app, so never secrets.
2. **Missing keys are not errors.** Each provider throws "not configured" and the section returns a warning naming the variable; the rest of the page loads.
3. **Reference data is bundled, not in Postgres (yet).** GeoNames cities15000 (34,189 cities), mledoze countries (250) and the Passport Index (39,601 rules) are gzip files embedded in `Pamplemousse.Infrastructure` (~1 MB) and searched in memory. Postgres arrives with saved trips (Sprint 3–4); the tables in the database schema stay the target.
4. **Weather beyond the 16-day forecast** shows last year's observed weather for the same dates (Open-Meteo archive), labelled as such.
5. **Map:** a lightweight pin preview until a Google Maps key is configured.
6. **US State Department** level (1–4) is the single safety score; UK FCDO text is not used yet.
7. **Google Places is on hold (amended 27 Oct 2026).** Restaurants come from OpenTripMap's `foods` category (restaurants, cafés, bars) with the same key as attractions. Trade-off: no ratings, price levels or opening hours, so Sprint 3 cannot score restaurants on food quality or budget until a richer source is chosen. Revisit before Sprint 3 (options: Google Places at the Pro tier, Geoapify, Foursquare). This changes the restaurants line of ADR 0003.
8. **Food in weighted search is a count, not a quality (amended 5 Nov 2026, spec 007).** Weighted search still needs no richer source: food, nightlife and nature are how many OpenTripMap places are within 1–2 km of each hotel (two free requests per hotel, kept 30 days, two hotels at a time to stay under the free key's rate limit). Google Places stays on hold. See `specs/007-weighted-search/research.md` R-1.

## Consequences
- The app works end-to-end with zero keys (weather, currency, holidays, visas, safety, countries, cities).
- First overview call per city is slow (~5–8 s) while caches are cold; later calls come from Redis/memory.
- Updating reference data = re-running the build script and committing new data files.
