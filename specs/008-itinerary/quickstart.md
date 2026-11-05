# Quickstart: Itinerary

Run the API (`pamplemousse-api`, port 5064) with every key set and sign-in working (spec 009). The owner runs
the signed-in calls. Use dates 1–3 months ahead.

Budget: no new paid request when the hotel searches of these cities are still stored from spec 007's checks.
Otherwise one RapidAPI request per city (5 at most), plus one car search in Rome for check 7. OpenTripMap,
Ticketmaster, Open-Meteo and flights are free.

| # | Check | Expect |
|---|---|---|
| 1 | Build Rome, 5 days, from Beirut, all preferences 50, Balanced | `201`; 5 days; a hotel, a flight, a total, a match; under 5 s (SC-001) |
| 2 | The days of 1 (SC-002) | no place twice; each day's walk under 12 km; no item before 09:00 or after 22:00 apart from flights |
| 3 | Culture 90, Nature 20, then the opposite (SC-003) | at least twice as many sights as parks, then the reverse |
| 4 | Relaxed, Balanced, Packed (SC-004) | 2, 3, 4 places per full day on average (± 0.5) |
| 5 | Build 1 twice (SC-005) | the same days, total and match |
| 6 | Swap the hotel for the 2nd alternative | days, walks, total and match change; no RapidAPI call in the logs |
| 7 | Add a car, then remove it | in the total, then gone; one car search the first time only |
| 8 | List, open, rename "Rome with Maya", mark planned, delete one (Story 3) | each shows in the list; opening takes under 1 s (SC-007) |
| 9 | Open the plan of check 1 with the second test account (SC-006) | `404` |
| 10 | Build with no `originId` | no flight; day 1 from 14:00; last day ends 12:00; the note |
| 11 | Build with `OPENTRIPMAP_API_KEY` unset | days from Wikidata sights only; a warning; meals missing with a note |
| 12 | Build Paris, Beirut, Istanbul and Lisbon, as checks 1–4 | as for Rome |
| 13 | The 50-plan limit | covered by tests only; building 50 plans live isn't worth it |
| 14 | API logs after 1–13 | no plan names, preferences, ids or emails |
