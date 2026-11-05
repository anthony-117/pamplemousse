# Quickstart: Weighted search

Run the API (`pamplemousse-api`, port 5064) with `RAPIDAPI_KEY`, `OPENTRIPMAP_API_KEY` and
`TRAVELPAYOUTS_API_TOKEN` set. Use dates 1–3 months ahead.

Budget: about 6 paid RapidAPI requests (hotel searches in 5 cities and one car search in Rome, fewer when some
are still cached). OpenTripMap and flights are free.

| # | Check | Expect |
|---|---|---|
| 1 | Rome hotels, `sort=match`, all preferences 50 | 20 hotels, each with `match`, 6 parts, a reason |
| 2 | Same, `budget=90`, the rest 10 (SC-001) | at least 8 of the top 10 among the 10 cheapest |
| 3 | Same, `culture=90`, the rest 10 (SC-002) | the top 5 have more sights within 1.5 km on average than the bottom 5 |
| 4 | Repeat 3 with other preferences (SC-004) | under 1 s; no new RapidAPI or OpenTripMap call in the logs |
| 5 | Repeat 1 twice (SC-005) | identical order, matches and reasons |
| 6 | Beirut → Rome flights, `sort=match`, `budget=10&comfort=90` (SC-003) | direct before 1 stop when both exist; only budget and comfort in the parts |
| 7 | Rome cars, `sort=match`, `comfort=90` | larger and automatic cars higher; 2 parts only |
| 8 | Rome hotels with `tripBudget=300` | hotels over $300 last, `overBudget: true`, reason "Over your $300 budget" |
| 9 | Cars, `budget=0&comfort=0&sort=match` | usual order, `rankingNote`, `match: null` |
| 10 | Hotels with `OPENTRIPMAP_API_KEY` unset | ranked, interests `known: false`, the warning |
| 11 | Paris, Beirut, Istanbul and Lisbon hotels, checks 2 and 3 (SC-001, SC-002) | as for Rome |
| 12 | Hotels, `culture=101` | `400` naming `culture` |
