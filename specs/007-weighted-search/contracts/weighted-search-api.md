# API Contract: Weighted search

No new route. The three existing lists accept the same new parameters and return a `match` on each item.

```http
GET /api/v1/destinations/{id}/hotels?…&sort=match&budget=90&culture=90&food=50&nature=20&nightlife=20&comfort=50&tripBudget=1500
GET /api/v1/destinations/{id}/flights?origin=…&sort=match&budget=90&comfort=50&tripBudget=1500
GET /api/v1/destinations/{id}/cars?…&sort=match&budget=90&comfort=50&tripBudget=1500
```

## New query parameters

| Name | Lists | Default | Rule |
|---|---|---|---|
| `sort` | all | as today | new value `match`. Flights gain `sort` (`cheapest`, the current order, or `match`) |
| `budget`, `comfort` | all | 50 | whole number 0–100 |
| `culture`, `food`, `nature`, `nightlife` | all | 50 | 0–100. Accepted everywhere so the app can send one set, but they only count for hotels |
| `tripBudget` | all | none | > 0, in the request's `currency` |

`400` names the field for a value out of range, a non-number, or `tripBudget` ≤ 0. Existing parameters and their
rules don't change.

A `match` is computed when `sort=match`, or when any preference or `tripBudget` is sent with another sort. In the
second case the order is the sort asked for (spec Story 3).

## Response additions

Each item gains `match` (`null` when no match was computed):

```json
{
  "id": "bk:558347",
  "name": "Hotel Artemide",
  "...": "existing fields unchanged",
  "match": {
    "percent": 94,
    "reason": "Near 12 sights, great reviews",
    "overBudget": false,
    "parts": [
      { "criterion": "budget",    "weight": 90, "percent": 78,  "known": true, "detail": "$612, the 4th cheapest of 20" },
      { "criterion": "culture",   "weight": 90, "percent": 100, "known": true, "detail": "12 top sights within 1.5 km" },
      { "criterion": "food",      "weight": 50, "percent": 85,  "known": true, "detail": "392 restaurants and cafés within 1 km" },
      { "criterion": "nature",    "weight": 20, "percent": 40,  "known": true, "detail": "6 parks and natural sites within 2 km" },
      { "criterion": "nightlife", "weight": 20, "percent": 60,  "known": true, "detail": "43 bars and pubs within 1 km" },
      { "criterion": "comfort",   "weight": 50, "percent": 92,  "known": true, "detail": "Reviews 9.1 / 10 · 4 stars" }
    ]
  }
}
```

- Parts come in the order budget, culture, food, nature, nightlife, comfort, and only those that apply to the
  kind with a weight above 0. Flights and cars have `budget` and `comfort` only.
- `known: false` means the data was missing and the part counted as 50% (spec edge cases); `detail` says
  "Unknown".

The response gains:

```json
{
  "weights": { "budget": 90, "culture": 90, "food": 50, "nature": 20, "nightlife": 20, "comfort": 50 },
  "tripBudget": 1500,
  "rankingNote": null
}
```

- `weights` and `tripBudget` echo what was used (`null` when no match was computed).
- `rankingNote` is set when sort=match had nothing to rank on: "None of your preferences apply to cars, so they
  are in the usual order." Items are then in the default order with `match: null` (FR-010).
- When places near hotels couldn't be counted, `warnings` gains "Places near the hotels couldn't be checked
  just now, so culture, food, nature and nightlife count as average." (FR-011)

### Hotels: paging with sort=match

Ranking covers the hotels already fetched for the search, 20 by default (research R-5). `totalPages` is the
number of pages fetched. A later page is empty, with the message "All the ranked hotels are on the earlier
pages." No new call to the paid source is made for ranking (FR-007).

## Examples

| Request | Result |
|---|---|
| `…/hotels?sort=match&budget=90&culture=10&food=10&nature=10&nightlife=10&comfort=10` | close to cheapest first |
| `…/flights?…&sort=match&budget=10&comfort=90` | direct flights before flights with stops |
| `…/cars?…&sort=match&budget=0&comfort=0` | usual order, `rankingNote` set, no `match` |
| `…/hotels?sort=match&culture=101` | `400`, field `culture` |
