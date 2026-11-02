# Sprint 1 review — "Pick Rome and see everything about it"
Dates: 27 Oct 2026 (built ahead of the planned 10–23 Nov window) · Status: code complete, PRs open

## Delivered
| Story | Result |
|---|---|
| TG-02 Country facts, weather, currency, holidays | ✅ Live: Open-Meteo (forecast ≤ 16 days, else last year), Frankfurter (ECB), Nager.Date, mledoze countries |
| TG-03 Visa and safety for my passport | ✅ Passport Index (bundled) + US State Dept level 1–4 |
| TG-04 Attractions and restaurants on a map | ✅ Attractions from Wikidata ranked by fame (ADR 0011, 28 Oct): Rome → Colosseum, St Peter's, Sistine Chapel, Pantheon; Paris → Eiffel Tower, Louvre, Notre-Dame. OpenTripMap fills in and serves restaurants. Map is a pin preview until a Maps key exists |
| TG-05 Events during my dates | ✅ Live with Ticketmaster (key set 27 Oct): Rome returns events for the trip dates |
| City search | ✅ 34,189 cities, accent-insensitive, biggest first |

## Pull requests
- pamplemousse: `feat/sprint-1-destinations` — https://github.com/anthony-117/pamplemousse/pull/new/feat/sprint-1-destinations
- chocomousse: `feat/sprint-1-destinations` — https://github.com/anthony-117/chocomousse/pull/new/feat/sprint-1-destinations

## Quality
- Backend: 39 tests (search, overview, partial failure, missing keys, validation, Env, reference data). 0 warnings.
- App: 12 tests (Env, models, debounced search, overview with failed section). Analyzer clean.
- Checked live in the browser (phone + desktop) against the real backend: Rome overview with real weather, rate, visa, advisory; Beirut shows "Do Not Travel" and the missing USD→LBP rate as a warning.

## Bugs found and fixed during the sprint
- Query enums were case-sensitive and bad input returned 500 → case-insensitive + 400 everywhere.
- App crashed on web when `app.env` was missing → any load error now keeps defaults (test added).
- Layout overflows on 390 px phones (weather strip) → content-sized rows.

## Next
- ~~Add the API keys and re-check~~ — done 27 Oct.
- Sprint 2: flights (Travelpayouts Data API, ADR 0010), hotels (Travelpayouts), cars as links + Redis cache in Aspire.
