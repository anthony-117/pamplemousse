# Research: Itinerary

Decided on 2026-11-06 from the existing sources and the spec 007 research. No request was made to any source;
the numbers below are starting values, checked live in the quickstart and changeable without an API change
(FR-005).

## R-1 · Where each kind of place comes from

| Kind in the plan | Source | Already in the code |
|---|---|---|
| Sight | the destination's top attractions: Wikidata, then OpenTripMap (Sprint 1) | yes, `IPlaceProvider` Attraction, cached 7 days / 24 h |
| Restaurant | OpenTripMap `foods` near the centre (Sprint 1) | yes, `IPlaceProvider` Restaurant, cached 24 h |
| Nature | OpenTripMap `natural,gardens_and_parks`, named, within 6 km of the centre | new `IPlaceProvider` Nature, same client, cached 24 h |
| Nightlife | OpenTripMap `bars,pubs,nightclubs`, named, rate 2 or more, within 3 km | new `IPlaceProvider` Nightlife, same client, cached 24 h |
| Event | Ticketmaster for the trip's dates (Sprint 1) | yes, `IEventProvider`, cached 6 h |
| Weather | Open-Meteo, 16 days (Sprint 1) | yes, `IWeatherProvider`, cached 3 h |

Two new free OpenTripMap requests per destination a day, at most. The spec 007 research showed parks within 8 km of
central Rome number 72, so 6 km gives enough for a 30-day trip. Restaurants are already capped at the 500 nearest,
so a hotel outside the centre may have few. Lunch and dinner then take the nearest found, and the walk says how far.

## R-2 · How many of each kind

Let `P` be the pace (Relaxed 2, Balanced 3, Packed 4) and `S` the place slots of the trip: `P` per full day, and
on the arrival and departure days `min(P, ⌊free hours ÷ 3⌋)`.

- **Sights and nature share the slots** by their weights: sights = `round(S × wCulture ÷ (wCulture + wNature))`,
  nature = the rest. When both are 0, half each.
  - Culture High (90), Nature Low (20): 82 % sights. On 15 slots that is 12 sights and 3 parks, which meets SC-003.
- **Nightlife**: an evening place on `round(days × wNightlife ÷ 100)` days, the latest days first, never on the
  departure day. Not counted in `P`.
- **Events** (FR-008): when Culture or Nightlife is at least 35 (above Low), at most one a day. The best is the
  first by start time, ties broken by id. An evening event replaces that day's nightlife place.
- **Food**: below 70, lunch and dinner are the nearest unused restaurant. At 70 or more (High), they are the
  best-known restaurant within 800 m: OpenTripMap rate, then rating, then nearest.

"Low" means a weight under 35 and "High" 70 or more, so the app's 10/20, 50 and 90 all fall in the right band.

## R-3 · Grouping places into days

**Options**: k-means (needs a random start, so not deterministic without care), sorting by angle around the hotel
(splits a dense centre badly), or greedy seeds.

**Decision**: greedy seeds, which are simple, deterministic and keep each day compact.
1. Take the chosen places in rank order (the source's order, ties broken by id).
2. For each day, longest first: the first unassigned place is the seed; add the nearest unassigned places to it
   until the day's quota is met.
3. Order each day's places by nearest neighbour from the hotel.
4. The day's title is "Day n · {seed} area", or "Arrival" or "Departure" on those days.

## R-4 · The timeline of a day

| Item | Start | Duration |
|---|---|---|
| Day start | 09:00, or the arrival time (R-5) | |
| Sight | after the walk | 2 h (1 h 30 when Packed) |
| Nature | after the walk | 1 h 30 |
| Lunch | after the place that ends closest to 12:30, and not before 12:00 | 1 h |
| Dinner | 19:00, or after the last place | 1 h 15 |
| Nightlife | after dinner | 1 h, ending by 22:00 |
| Event | its own time, or 20:00 when unknown | 2 h, cut to end by 22:00; an event starting after 21:00 is not placed |
| Free time | fills any gap of 2 h or more, "Free afternoon" or "Free morning" | |

Walking (spec): straight line × 1.3, at 4.5 km/h, rounded up to 5 minutes. The first walk is from the hotel.
A place that would end after 18:30 moves to the next day with room, or is dropped.

## R-5 · Arrival and departure

- **Landing**: `DepartureAt + DurationMinutes`. When the duration is not known, the fastest duration of the same
  route in the stored list is used, then 4 h, with a note.
- **First day** starts at landing + 1 h + the airport-to-hotel time, rounded up to 15 minutes. An arrival after
  18:00 leaves only dinner.
- **Airport to hotel**: 45 minutes for every destination for now, stated in the plan's notes (FR-007 allows a
  fixed estimate).
- **Last day** ends at return departure − 3 h. A departure before 12:00 leaves only breakfast time, so no places.
- **No flight**: the first day starts at 14:00 and the last day ends at 12:00, with a note (spec Edge Cases).

## R-6 · The plan's match (FR-009)

Weights `w` are the six preferences, 0 to 100.

| Part | Value 0–1 | Weight |
|---|---|---|
| Hotel | its spec 007 match | `Σ all six w` |
| Flight | its spec 007 match | `wBudget + wComfort` (left out with no flight) |
| Car | its spec 007 match, when added | `wBudget + wComfort` |
| Days | see below | `wCulture + wNature + wNightlife + wFood` |

**Days fit** is the weighted average, by those four weights, of:
- Culture: `1 − |sights share − asked share|`, where the asked share is `wCulture ÷ (wCulture + wNature)`. This
  only drops when there weren't enough sights.
- Nature: the same for parks.
- Nightlife: evenings planned ÷ evenings asked, capped at 1; 1 when none were asked.
- Food: meals with a restaurant ÷ meals in the plan; at High, only the meals that got a well-known restaurant
  (rate 2 or more) count.

**Total** = `Σ value × weight ÷ Σ weight`, shown as a whole percentage. When every weight is 0 the plan shows no
match, as spec 007 does.

**Reason**, in fixed wording from the strongest and weakest part:
- "Great hotel for you, days full of sights"
- "Good flight, but few parks nearby"

## R-7 · Swapping without calling a source

**Finding**: the hotel and car lists are kept in Redis for a while, then rebuilt from Firestore's saved results,
or by calling the source again. A swap a day later could therefore call the paid source, which FR-011 forbids.

**Decision**: the plan keeps the top 20 of each ranked list it used as **alternatives**: a copy of each offer
with its match. Swapping picks from these, and the rebuild uses the plan's copies, so it calls no source. The
place lists are fetched again, which is free and cached. If they fail, the plan keeps its old days with a warning.

**Adding a car**: the first time, the plan runs the ranked car search once, as opening the Cars tab would
(FR-002). Its top 20 become the car alternatives. Removing and re-adding uses those.

## R-8 · Storage size

A 30-day Packed plan has about 30 × 9 = 270 items, plus 60 alternatives with a short copy each. That is about
150–250 KB, well under Firestore's 1 MB a document. One document per plan; the list view reads a summary from the
same document with a field mask.
