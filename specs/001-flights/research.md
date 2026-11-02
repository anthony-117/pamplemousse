# Research: Flight prices for a trip

Checked on 2026-11-02. No Travelpayouts token exists yet, and its help pages block automated reading.
**Confirmed** means seen directly. **From a search snippet** means a search result quoted the official article. **Unconfirmed** must be checked in the first implementation step.

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

## R-7: To confirm with a token (first implementation step)

1. Response field names, including `link`, `flight_number`, `transfers`, `return_transfers`, `duration`, `airline`.
2. Whether a month-level search works for round trips, and the behaviour of `unique` and `sorting`.
3. Which currencies are supported, and the error for an unsupported one.
4. How the marker attaches to links, and that the links open the right route and dates.
5. Whether prices vary by passenger or cabin (the spec assumes one adult, economy).
6. Whether the fare's data says when the price was seen, to show in the response.
7. The terms for using and embedding the public lists.

Sources: [Aviasales Data API](https://support.travelpayouts.com/hc/en-us/articles/203956163-Aviasales-Data-API), [API rate limits](https://support.travelpayouts.com/hc/en-us/articles/4402565416594-API-rate-limits), `api.travelpayouts.com/data/en/{cities,airports,airlines}.json`.
