# Quickstart: Hotel prices for a trip

## Setup

1. In `pamplemousse/.env` (git-ignored), set:
   - `RAPIDAPI_KEY`: already there.
   - `FIREBASE_PROJECT_ID`: the Firebase project's ID.
   - `GOOGLE_APPLICATION_CREDENTIALS`: full path to the service account JSON file, kept **outside** the repo.
   Without the last two, Aspire starts the Firestore emulator and nothing is kept between runs.
2. Start Docker, then `dotnet run --project src/Pamplemousse.AppHost`.

## Live checks (about 25 requests of the monthly 530)

| # | Check | Requests | Expect |
|---|---|---|---|
| 1 | `GET /api/v1/destinations/3169070/hotels?from=…&to=…` (Rome, 1–3 months ahead) | 2 (auto-complete + search) | ≥ 10 hotels, all with name, photo, position, total (SC-001, SC-002) |
| 2 | Same request again | 0 | Same result in under 1 s (SC-004) |
| 3 | Same with `sort=price&minReview=8` | 0 | Cheapest first, none under 8 (Story 3) |
| 4 | Paris, Beirut and 7 more cities | 16 | ≥ 10 hotels each (SC-001) |
| 5 | Zahlé | 2 | Nearby places marked `nearCity` |
| 6 | Open 5 hotels (`GET /api/v1/hotels/{id}`) and their links | 5 | Right hotel, dates and adults on Booking.com (SC-007); occupancy fits (R-3) |
| 7 | For 5 of those, compare totals with the booking site | 0 | Within 5% in at least 4 (SC-003) |
| 8 | Try `searchresults.html?dest_id={id}&dest_type=hotel` by hand | 0 | Decides R-5 |
| 9 | Remove `RAPIDAPI_KEY`, repeat check 1 with new dates | 0 | 200, empty list, warning naming the variable (SC-006) |
| 10 | Set `MonthlyRequestLimit` to the current count, repeat check 1 | 0 | 200, saved prices with their time and the limit warning |
| 11 | In the Firebase console | 0 | `cities`, `hotels` and `priceSnapshots` documents present |

Record the results in `research.md` and the sprint review.
