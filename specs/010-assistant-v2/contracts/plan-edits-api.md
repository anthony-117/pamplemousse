# API Contract: Plan edits and rebuild

Additions to [spec 008's itinerary contract](../../008-itinerary/contracts/itinerary-api.md). Same sign-in and
owner rules: another traveller's plan answers `404`. Each change returns the whole plan, rebuilt by the itinerary's
rules (FR-013).

## Additions to the plan and its summary

- `GET /plans` items (and plan cards) gain `destinationId`, `adults`, `tripBudget` (or null), `pace`, `lat` and `lng`
  (the city centre).
- The plan gains `edits`, the day edits the traveller made, kept through every rebuild:

```json
"edits": [
  { "day": 2, "action": "replace", "itemId": "wd:Q10285",
    "newItem": { "kind": "place", "id": "otm:N123", "name": "Villa Borghese" } },
  { "day": 3, "action": "remove", "itemId": "wd:Q243", "newItem": null }
]
```

## Alternatives for a day

`GET /api/v1/plans/{id}/days/{day}/alternatives?itemId=&kind=`

- `itemId`: the item to replace; without it, items that fit a new slot on that day.
- `kind`: `sight`, `nature`, `restaurant`, `nightlife` or `event`; default the replaced item's kind, else `sight`.
- At most 6, from the city's kept place lists and that date's events, near the day's other items, ranked by the
  plan's preferences, not already in the plan (FR-017):

```json
{
  "day": 2, "itemId": "wd:Q10285", "kind": "nature",
  "items": [ { …place or event card…, "walkKmFromDay": 1.2 } ],
  "message": null
}
```

`message` says why there are none ("No other nature places are known near day 2").

## Replace or add an item

`POST /api/v1/plans/{id}/days/{day}/items` with `{ "kind": "place", "id": "otm:N123", "replaces": "wd:Q10285" }`

- `kind` is `place` or `event`; `id` must be among the day's alternatives.
- Without `replaces`, the item goes in the day's next free slot.
- `422 day-full` when the day has no free slot for its pace; `detail` says so ("Day 2 is full for a balanced pace.
  Replace a place instead.").
- `400` naming the field: an unknown day, an item not among the alternatives, `replaces` not on that day.

## Remove an item

`DELETE /api/v1/plans/{id}/days/{day}/items/{itemId}` → the plan. The item's time becomes free time, and stays so
through later rebuilds.

## Rebuild

`POST /api/v1/plans/{id}/rebuild` with any of
`from`, `to`, `adults`, `originId`, `preferences`, `pace`, `tripBudget` (a missing field keeps the plan's):

- Builds the plan again in place, as `POST /plans` would, keeping its id, name, status and edits (FR-012a).
- Keeps the traveller's choices: a hotel or flight swapped in stays while the new offers include it (for new dates,
  the same airline, flight number and airports), a car while the dates are the same. Otherwise the best offer is
  taken, and `notes` says when a choice was lost ("The hotel you chose isn't offered for this trip any more, so the
  best match was chosen."; "Car prices depend on the dates, so your car was removed. Add one again for the new
  dates.").
- Edits stay on their day when the rebuild chooses or groups places differently: an edited item placed on another day
  comes back to its day, and a replacement whose item isn't chosen any more still brings its place (a restaurant or
  evening out takes the day's dinner or night out). Only the edits of days that are gone are dropped, with a note.
- Same `400` and `422 no-places` as `POST /plans`. On any failure the saved plan is unchanged.
