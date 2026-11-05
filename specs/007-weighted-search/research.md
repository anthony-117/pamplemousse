# Research: Weighted search

Checked on 2026-11-05 with the free OpenTripMap key (about 12 requests). No paid request was used.

## R-1 · Counting places near a hotel

**Question**: Can one OpenTripMap request per destination give the restaurants, bars and parks near every
hotel (spec FR-008 as first written)?

**Finding**: No. `/places/radius` returns at most 500 places, nearest first, whatever `limit` says (5000 tried,
json and geojson). Around Rome's centre with an 8 km radius:

| Kinds | Returned | Farthest |
|---|---|---|
| `restaurants,cafes` | 500 (capped) | 1.35 km |
| `bars,pubs,nightclubs` | 500 (capped) | 6.5 km |
| `natural,gardens_and_parks` | 72 | 8 km |

So a city-wide request only covers the centre, and a hotel 2 km out would look like it has nothing nearby.

**Per hotel** (two requests each):

| Hotel position | Food and nightlife within 1 km | Food | Nightlife | Nature within 2 km |
|---|---|---|---|---|
| Via Nazionale (centre) | 435 | 392 | 43 | 21 |
| Trastevere | 385 | 332 | 53 | 13 |
| EUR (outer) | 39 | 31 | 8 | 2 |

These are under the 500 cap even in central Rome, and they clearly separate central and outer hotels.

**Decision**: Two OpenTripMap requests per hotel, made the first time that hotel is ranked:
- `restaurants,cafes,bars,pubs,nightclubs` within 1 km, split by kind;
- `natural,gardens_and_parks` within 2 km.

The counts are stored per hotel for 30 days (Redis; hotels don't move, and places change slowly). The first
ranking of a 20-hotel search makes 40 free requests, run 4 at a time, which takes about 3–5 s. After that,
re-ranking makes none. The free plan allows 5,000 requests a day.

A count that reaches 500 is kept as 500 ("500 or more"). Since counts are compared within the list, that only
flattens differences between very dense areas.

**Spec change**: this replaces FR-008's "no request per hotel" with "one pair of free requests per hotel, stored
30 days". The cost stays bounded and free, and no paid source is touched.

## R-2 · What counts as culture

Culture uses the destination's attraction list that the app already shows: Wikidata, ranked by the number of
Wikipedia editions, with OpenTripMap as a fallback (ADR 0011). That's 20 attractions, cached for a day under
`attractions:{destinationId}`. A hotel's culture count is how many of those 20 are within 1.5 km.

`interesting_places` from OpenTripMap was also tried, but it returns 500 small items (fountains, statues)
within the centre and would reward any central hotel equally.

## R-3 · Measures on a fixed scale

| Kind | Measure | Scale |
|---|---|---|
| Hotel | Review score | score / 10 |
| Hotel | Stars | stars / 5 |
| Flight | Stops, each leg | 0 stops = 1, 1 stop = 0.5, 2 or more = 0; the two legs are averaged |
| Flight | Dates | Exact 1 · Nearby 0.7 · Same month 0.4 · Any return 0.2 |
| Car | Size class | Mini 0.2 · Economy 0.35 · Compact 0.5 · Intermediate 0.65 · Standard 0.75 · Full-size 0.85 · Premium, Luxury, SUV, People carrier 1 · unknown 0.5 |
| Car | Company rating | rating / 10 |
| Car | Gearbox | automatic 1 · manual 0 |
| Car | Free cancellation | yes 1 · no 0 |

Comfort is the average of the parts known for that offer.

**Journey time** has no fair fixed scale: 3h 45m is good from Beirut to Rome and impossible from Beirut to
Tokyo. It is measured against the list instead: the shortest journey ÷ this journey. The spec listed it among
the fixed-scale measures, and this changes that.

## R-4 · Reasons

The reason is built from the offer's two largest contributions (weight × measure). Each criterion has a short
phrase per kind, chosen by the measure:

| Criterion | High measure (≥ 0.8) | Middle (0.5–0.8) |
|---|---|---|
| Budget | "Cheapest option" when 1, else "Cheap" | "Good price" |
| Culture | "Near {n} sights" | "Some sights nearby" |
| Food | "Lots of food nearby" | "Food nearby" |
| Nature | "Near parks" | "A park nearby" |
| Nightlife | "Lively at night" | "Some bars nearby" |
| Comfort, hotel | "Great reviews" | "Good reviews" |
| Comfort, flight | "Direct, short journey" when 0 stops | "Short journey" |
| Comfort, car | "Roomy, automatic" when automatic | "Well rated" |

A measure below 0.5 is never used in a reason. If neither of the two qualifies, the reason is "Good all-round
match" when the match is at least 70%, otherwise "Weaker on what matters most to you". Over budget always reads
"Over your {budget} budget".

The phrases are code constants in Domain, checked by tests. They can be reworded without changing the API.

## R-5 · Hotels on other pages

A hotel search keeps 20 hotels (one source page) by default. Fetching page 2 costs a paid request, and would
re-order page 1. So sort=match ranks only the hotels already fetched for the search: `totalPages` is the number
of pages fetched, and asking for a later page returns it empty with a message. Other sorts keep today's paging.

## R-6 · Changes after the live checks (5 Nov 2026)

With Low at 20 and price scored linearly from cheapest to dearest, Rome failed SC-001: 6 of the top 10 were
among the 10 cheapest, against a target of 8. There were two causes:

- **Weight balance.** One High (90) was outweighed by five Lows (5 × 20 = 100).
- **Price scale.** One dear hotel ($1,371) squeezed the rest together, so the 8th cheapest still scored 76%.

Two changes, approved by the owner:

- The app's Low sends **10** instead of 20.
- Price is scored **by position**: 1 − (offers cheaper than this one) ÷ (offers − 1). The cheapest is 1 and the
  dearest 0. Offers with the same price share a position. This matches the budget detail ("the 4th cheapest of 20").

Measured on the same live lists:

| Low | Price score | Rome SC-001 | Paris SC-001 |
|---|---|---|---|
| 20 | linear | 6/10 | 8/10 |
| 20 | by position | 7/10 | 8/10 |
| 10 | linear | 7/10 | 9/10 |
| 10 | by position | 9/10 | 10/10 |

SC-002 (culture) was unchanged in every case.
