# Research: Flight prices for a trip

Checked on 2026-11-02. Travelpayouts' help pages block automated reading, so R-1 to R-6 were written before a token existed; R-7 and R-8 are the results with a real token, and they take precedence.

## R-1: Which endpoint

- **Decision**: `GET https://api.travelpayouts.com/aviasales/v3/prices_for_dates`.
- **Rationale**: Chosen in ADR 0010. Without a token it answers `Unauthorized`, so the endpoint exists (Confirmed). The token goes in the `X-Access-Token` header, keeping it out of URLs and logs.
- **Parameters** (from a search snippet): `origin`, `destination`, `departure_at`, `return_at`, `one_way`, `direct`, `currency`, `unique`, `sorting`, `limit`, `page`.
- **Limit** (from a search snippet): 600 requests per minute, far above our use.
- **Currency trap** (from a search snippet): prices default to rubles, so `currency` MUST always be sent.

## R-2: Mapping a TravelGenie city to a flight code

- **Decision**: Use the nearest city that has a flightable airport, by straight-line distance, within 150 km. Prefer the same-country match only if live checks show a need.
- **Evidence** (Confirmed, public list `data/en/cities.json`, no token): 9,653 cities, 3,607 with a flightable airport, all with coordinates. Run against real coordinates:

  | City | Result | Distance |
  |---|---|---|
  | Rome | ROM | 1 km |
  | Byblos | BEY (Beirut) | 29 km |
  | Zahle | BEY (Beirut) | 37 km |
  | Tripoli (Lebanon) | BEY (Beirut) | 69 km |
  | Versailles | PAR (Paris) | 17 km |
  | Assisi | PEG (Perugia) | 19 km |
  | Zermatt | SIR (Sion) | 41 km |
  | Ushuaia | USH | 0 km |

- **Alternatives rejected**: Matching by name is unsafe, because `Rome` also matches three US cities and `London` also matches Canada. Calling a lookup service for each request adds a failure point for data that barely changes.
- **Why 150 km**: It covers realistic airport trips. The figure is a judgment call, and easy to change.

## R-3: Embedding the lists

- **Decision**: Embed flight cities (about 83 KB compressed) and airline names (about 10 KB) in Infrastructure, like the existing reference data, with `scripts/update-flight-data.py` to refresh them.
- **Rationale**: Works offline, is fast, and tests need no internet.
- **Open**: The licence for these lists is not confirmed. Check when the account exists. If restricted, download at startup or use another open list.

## R-4: Nearby dates (±3 days)

- **Decision**: Ask for the exact dates first. Only if that returns nothing, ask for the whole month of the departure date (plus the neighbouring month when ±3 days crosses a month boundary), keep fares within 3 days, and drop any that are in the past.
- **Rationale**: Exact results usually avoid the second call. A snippet shows `departure_at` accepting a month (`2023-07`). Unconfirmed: how a month-level round trip behaves.
- **Fallback if months do not work**: up to 6 extra per-date calls, cached and well inside the rate limit.

## R-5: Booking links

- **Decision**: Build the link from the fare's own `link` value on the booking site, and add `marker` when configured. For the empty-result search link, use the site's search address, e.g. `https://www.aviasales.com/search/PAR1012ROM14121`.
- **Evidence**: That address responds with 200 (Confirmed), but whether it opens the right search, and how the marker attaches, is Unconfirmed.

## R-6: Time limits and retries

- **Decision**: A 5-second client timeout, and the standard resilience handler ServiceDefaults already adds to every client (Confirmed in `Extensions.cs`) for retries on 429 and 5xx. A timeout or failure becomes a warning through `SectionLoader`. Failures are not cached; empty results are, for 1 hour.

## R-7: Checked with a real token (2026-11-02)

Token added to the git-ignored `.env`. The marker is still missing.

| Question | Result |
|---|---|
| Field names | `airline` (code), `flight_number` (number only), `departure_at`, `return_at`, `transfers`, `return_transfers`, `duration`, `duration_to`, `duration_back`, `price`, `link`, `origin_airport`, `destination_airport`, `gate` |
| Times | Local time with an offset, e.g. `2026-12-16T06:20:00+01:00` |
| Month search, round trip | Works: `departure_at=2026-12&return_at=2026-12` |
| One way | Works: `one_way=true`, no `return_at` needed |
| Direct only | Works: `direct=true`, all results had 0 transfers |
| Currency | `usd`, `eur`, `lbp` work. Unknown code returns 400. **No currency returns rubles**, so it must always be sent |
| Link | Relative path such as `/search/PAR1612ROM20121?t=...`, to be added to `https://www.aviasales.com` |
| When a price was seen | No field for it; the link has `search_date`, e.g. 30102026, about 3 days old in the sample |
| Airports | `origin_airport` can be far from the city (Paris fare from BVA, Beauvais), so the response should show it |
| Speed | 52 calls in a row, none over 5 s, most under 1 s. One earlier call exceeded 25 s: the 5 s timeout is needed |

Still open: how the marker attaches (needs the real marker), that links open the right route in a browser, whether prices vary by passengers, and the terms for embedding the public lists.

## R-8: Fare coverage, a problem for FR-009 and SC-001

Round trips for 10–14 Dec 2026, about 5 weeks ahead, on one day's data:

| Route | Exact dates | Within ±3 days (both ways) | Same month |
|---|---|---|---|
| Paris–Rome | 0 | 3 | 15 |
| Beirut–Paris | 0 | 0 | 0 |
| London–New York | 0 | 1 | 18 |
| Beirut–Dubai | 0 | 0 | 5 |
| Madrid–Rome | 16 | 5 | 11 |
| Berlin–Istanbul | 0 | 6 | 39 |
| Rome–Athens | 0 | 0 | 3 |
| Beirut–Istanbul | 0 | 1 | 11 |
| Paris–Tokyo | 0 | 3 | 15 |
| Dubai–London | 57 | 4 | 19 |
| **Routes with a fare** | **2 of 10** | **7 of 10** (with exact) | **9 of 10** |

Also tried: Beirut–Rome had no round trips in the month; Perugia–Paris had 1; Sion–London had none.

- Exact dates alone would look empty on most routes, so nearby dates matter more than expected.
- ±3 days reaches 7 of 10, below SC-001's 8 of 10.
- Same-month fares reach 9 of 10, but the dates may be far from the trip.
- **Beirut is weak in this source**: Beirut–Paris and Beirut–Rome have no round trips in the whole month.
- This is one date range on one day, so it is an indication, not a measurement.

**Decision needed from the owner** before the plan is approved.

Sources: [Aviasales Data API](https://support.travelpayouts.com/hc/en-us/articles/203956163-Aviasales-Data-API), [API rate limits](https://support.travelpayouts.com/hc/en-us/articles/4402565416594-API-rate-limits), `api.travelpayouts.com/data/en/{cities,airports,airlines}.json`.
