# Data Model: Itinerary

## Domain (`Pamplemousse.Domain/Itinerary/`)

All of this is pure code with no dependency (FR-018). The handler gathers the inputs; `PlanBuilder` returns the
same plan for the same inputs.

### Inputs

| Type | Fields |
|---|---|
| `Pace` | enum `Relaxed`, `Balanced` (default), `Packed`; `PlacesPerDay` 2 / 3 / 4 |
| `TripRequest` | destination (id, name, centre), `DateRange`, adults, currency, departure city id?, `Weights`, `Pace`, trip budget? |
| `PlanOffers` | hotel `Ranked<HotelOffer>`?, flight `Ranked<FlightOffer>`?, car `Ranked<CarOffer>`? |
| `PlacePools` | sights, nature, nightlife, restaurants (`Place` lists, in source order), events, weather days |

### `PlanBuilder.Build(TripRequest, PlanOffers, PlacePools) → BuiltPlan`

The steps of research R-2 to R-6: slots, choice, grouping, timeline, total, match.

### Output

```text
BuiltPlan
  Days            IReadOnlyList<PlanDay>
  Total           PlanTotal
  Match           PlanMatch?          // null when every weight is 0
  Notes           IReadOnlyList<string>   // "Airport to hotel estimated at 45 minutes", "No departure city…"
  Warnings        IReadOnlyList<string>   // a source that failed

PlanDay
  Number, Date, Title, Weather (WeatherDay?), WalkKm, Items

PlanItem
  Kind            Arrival | Departure | Sight | Nature | Restaurant | Nightlife | Event | FreeTime
  Start           TimeOnly
  Duration        TimeSpan
  Name            string
  WalkKm          double              // from the previous item or the hotel; 0 for the first after arrival
  Price           Money?
  Ref             ItemRef             // what it is a copy of

ItemRef
  Kind            place | event | flight | hotel
  Id              string              // "wd:Q10285", "tm:Z698…", "tp:…", "bk:558347"
  Location        GeoPoint?
  ImageUrl, Url   string?

PlanTotal
  Amount          Money               // trip currency
  Flight, Hotel, Car, Events   Money?
  Unpriced        int                 // items with no known price
  OtherCurrency   int                 // left out (Edge Cases)
  OverBudgetBy    Money?

PlanMatch
  Percent         int
  Parts           [{ part: hotel|flight|car|days, weight, percent }]
  DaysParts       [{ criterion: culture|nature|nightlife|food, weight, percent }]
  Reason          string
```

### `Plan`, the saved entity

```text
Plan
  Id              string (new GUID)
  Owner           TravellerId         // spec 009
  Name            1–60 chars; default "{Destination}, {n} days"
  Status          Draft | Planned
  Request         TripRequest
  Chosen          PlanOffers          // copies, with match and the time the price was checked
  Alternatives    hotels[≤20], flights[≤20], cars[≤20]   // copies with match (R-7)
  Built           BuiltPlan
  CreatedAt, UpdatedAt
```

Rules: rename (1–60 characters), set status, swap an offer (it must be among the alternatives; the days are rebuilt
by `PlanBuilder`), add or remove the car. `MaxPlans = 50` lives here.

## Changes to existing types

- `Preferences` (spec 007) is unchanged; pace is a separate argument, so ranking ignores it.
- `TravelProfile` (spec 009) gains `Pace`, default Balanced. `GET` and `PUT /me` gain `pace`.
- `PlaceCategory` gains `Nature` and `Nightlife` (R-1). `GET /destinations/{id}/places?category=` accepts them too,
  which is an addition.

## Application

```text
Abstractions/Plans.cs
  IPlanStore      CountAsync(owner) · AddAsync(plan) · GetAsync(owner, id) · ListAsync(owner) · SaveAsync(plan) · DeleteAsync(owner, id) · DeleteAllAsync(owner)

Itinerary/PlanInputs.cs                 // runs the ranked hotel and flight searches (existing handlers, sort=match), places, events, weather in parallel; each failure → warning
Itinerary/Commands/BuildPlan/           // checks the 50 limit, builds, saves as draft
Itinerary/Commands/ChangePlan/          // rename, status
Itinerary/Commands/SwapOffer/           // hotel, flight, car (add / swap / remove)
Itinerary/Commands/DeletePlan/
Itinerary/Queries/ListPlans/ · GetPlan/
```

`DeleteAccountHandler` (spec 009) calls `IPlanStore.DeleteAllAsync` first.

## Firestore

```text
travellers/{uid}/plans/{planId}
  name, status, destinationId, destinationName, from, to, adults, currency, originId,
  weights {six}, pace, tripBudget,
  chosen { hotel, flight, car }          // copies
  alternatives { hotels[], flights[], cars[] }
  days [ { number, date, title, weather, walkKm, items [ … ] } ]
  total { … }, match { … }, notes [], warnings [],
  createdAt, updatedAt
```

The list reads `name, status, destinationName, from, to, total.amount, match.percent, chosen.hotel.photo` with a
field mask, newest `updatedAt` first.
