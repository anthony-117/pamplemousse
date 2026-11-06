# API Contract: Itinerary

Every route needs the sign-in of spec 009. A plan of another traveller answers `404`.

## `POST /api/v1/plans`

Builds a plan and saves it as a draft.

```json
{
  "destinationId": "3169070",
  "from": "2027-04-10",
  "to": "2027-04-14",
  "adults": 2,
  "currency": "USD",
  "originId": "276781",
  "preferences": { "budget": 90, "culture": 90, "food": 50, "nature": 20, "nightlife": 20, "comfort": 50 },
  "pace": "balanced",
  "tripBudget": 1200
}
```

- `originId`, `tripBudget` and `pace` are optional; a missing preference is 50 (spec 007).
- `201` with the plan and `Location: /api/v1/plans/{id}`.
- `400` naming the field:
  - dates in the past or longer than 30 days;
  - an unknown destination or origin;
  - a preference outside 0–100;
  - an unknown `pace`;
  - `tripBudget` ≤ 0.
- `409` `code: "too-many-plans"` at 50 plans.
- `422` `code: "no-places"` when no places at all could be found (spec Edge Cases).

## The plan

```json
{
  "id": "6f1c…",
  "name": "Rome, 5 days",
  "status": "draft",
  "destination": { "id": "3169070", "name": "Rome", "countryCode": "IT" },
  "from": "2027-04-10", "to": "2027-04-14", "adults": 2, "currency": "USD", "originId": "276781",
  "preferences": { "budget": 90, "culture": 90, "food": 50, "nature": 20, "nightlife": 20, "comfort": 50 },
  "pace": "balanced", "tripBudget": 1200,
  "match": {
    "percent": 91,
    "reason": "Great hotel for you, days full of sights",
    "parts": [
      { "part": "hotel", "weight": 300, "percent": 94 },
      { "part": "flight", "weight": 140, "percent": 88 },
      { "part": "days", "weight": 180, "percent": 89 }
    ],
    "days": [
      { "criterion": "culture", "weight": 90, "percent": 100 },
      { "criterion": "nature", "weight": 20, "percent": 67 },
      { "criterion": "nightlife", "weight": 20, "percent": 100 },
      { "criterion": "food", "weight": 50, "percent": 100 }
    ]
  },
  "hotel":  { "offer": { "...": "hotel list item of spec 007, with match" }, "checkedAt": "2027-04-01T10:02:00Z" },
  "flight": { "offer": { "...": "flight list item, with match" }, "checkedAt": "…" },
  "car": null,
  "total": {
    "amount": 939, "currency": "USD",
    "flight": 412, "hotel": 527, "car": null, "events": 0,
    "unpriced": 11, "otherCurrency": 0, "overBudgetBy": null
  },
  "days": [
    {
      "number": 2, "date": "2027-04-11", "title": "Day 2 · Colosseum area",
      "weather": { "summary": "Sunny", "maxC": 22, "minC": 12, "rainChance": 10 },
      "walkKm": 6.4,
      "items": [
        {
          "kind": "sight", "start": "09:15", "minutes": 120, "name": "Colosseum", "walkKm": 1.2, "price": null,
          "ref": { "kind": "place", "id": "wd:Q10285", "lat": 41.8902, "lng": 12.4922, "imageUrl": "…", "url": null }
        }
      ]
    }
  ],
  "notes": ["The time from the airport to the hotel is estimated at 45 minutes."],
  "warnings": [],
  "createdAt": "…", "updatedAt": "…"
}
```

## `GET /api/v1/plans`

The traveller's plans, newest first.

```json
{
  "items": [
    {
      "id": "6f1c…", "name": "Rome, 5 days", "status": "draft", "destinationName": "Rome",
      "from": "2027-04-10", "to": "2027-04-14", "total": 939, "currency": "USD", "matchPercent": 91,
      "imageUrl": "…", "updatedAt": "2027-04-01T10:02:00Z"
    }
  ],
  "count": 1,
  "max": 50
}
```

## `GET /api/v1/plans/{id}`

The plan as saved (FR-013). No source is called.

## `PATCH /api/v1/plans/{id}`

`{ "name": "Rome with Maya" }`, `{ "status": "planned" }`, or both. `200` with the plan; `400` naming the field.

## `GET /api/v1/plans/{id}/alternatives/{kind}`

`kind` is `hotels`, `flights` or `cars`. Returns the kept ranked list (R-7), each item a spec 007 list item with
its match. For `cars`, the first call runs the ranked car search once if the plan has none yet.

## `PUT /api/v1/plans/{id}/{kind}`

`kind` is `hotel`, `flight` or `car`, with body `{ "offerId": "bk:1234" }`. Swaps it in and rebuilds the days,
total and match. Returns `200` with the plan. `400` names `offerId` when it is not among the alternatives.

## `DELETE /api/v1/plans/{id}/car`

Removes the car. Returns `200` with the plan.

## `DELETE /api/v1/plans/{id}`

`204`.

## Changes to existing routes

- `GET` and `PUT /api/v1/me` gain `pace` (`relaxed`, `balanced` or `packed`; default `balanced`).
- `GET /api/v1/destinations/{id}/places?category=` accepts `nature` and `nightlife`.
