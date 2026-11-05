# Sprint 3 review — in progress

This records weighted search (TG-09), built with Spec Kit: [spec, plan and contract](../../../specs/007-weighted-search/).
The Figma design is approved (page "Sprint 3 — Weighted search", 5 Nov 2026). The app screens come next, in `chocomousse`.

## Delivered: weighted search (backend)
No new route. The hotel, flight and car lists accept six preferences from 0 to 100 (`budget`, `culture`, `food`,
`nature`, `nightlife`, `comfort`; 50 when left out) and a `tripBudget`. `sort=match` orders by them. Each offer gets a
`match`: a percentage, a short reason and a breakdown. Any other sort keeps its order and still shows the match. The
score is plain C# in the Domain (ADR 0002), never the LLM.

- **Hotels** use all six. Culture counts the city's top 20 attractions within 1.5 km. Food, nightlife and nature count
  OpenTripMap places within 1–2 km of the hotel: two free requests per hotel, kept 30 days (ADR 0008, item 8).
  Comfort is reviews and stars.
- **Flights** use budget and comfort (stops, journey time, how close the dates are). Flights gain
  `sort=cheapest|match`. Ranking runs before `limit`, so a dearer direct flight is never cut off.
- **Cars** use budget and comfort (size class, company rating, automatic, free cancellation).
- Offers over `tripBudget` come last, marked "Over your $N budget".
- Places that can't be counted count as average, with one warning. All weights at 0 gives the usual order with a note.

| Story | Result |
|---|---|
| Rank hotels by my preferences | ✅ Match, reason and six-part breakdown; no new paid request |
| Rank flights and cars | ✅ Budget and comfort only, as the contract says |
| See why an option ranks where it does | ✅ Each part has its weight, percentage and detail, e.g. "392 restaurants and cafés within 1 km" |

### Live checks, 5 Nov 2026
Real keys, local API. 8 RapidAPI requests of the monthly 530 (Rome twice because a restart cleared the in-memory cache).

| Check | Result |
|---|---|
| SC-001, budget High and the rest Low: top 10 among the 10 cheapest | Rome 9, Beirut 9, Istanbul 9, Lisbon 9 out of 10. Paris was 8 before the change below and 10 in the simulation after it |
| SC-002, culture High: sights near the top 5 vs the bottom 5 | Rome 10.0 vs 2.2, Paris 5.6 vs 0.2, Beirut 17.8 vs 0.4, Istanbul 9.0 vs 0.0, Lisbon 6.2 vs 0.4 |
| SC-003, comfort High: direct before stops (Paris → Rome) | ✅ Beirut → Rome had no recent fares for the dates |
| SC-004, re-ranking with other preferences | 0.4–0.9 s and no new paid or OpenTripMap request. Two re-ranks took 1.1 and 2.5 s, as slow as a plain sort: the time is the Firestore reads every search makes |
| SC-005, the same request twice | Identical order, matches and reasons |
| Trip budget $300 in Rome | All 20 over, last, "Over your $300 budget" |
| Page 2 with sort=match | Empty, "All the ranked hotels are on the earlier pages.", no request |
| `culture=101` | 400 naming `culture` |
| Cars in Rome, comfort High | Larger automatic cars first; budget and comfort only. All weights at 0 gives the note |

Found and fixed:
- OpenTripMap answered 429 when four hotels were counted at once, so some hotels counted as average and the same
  search could differ on a second run. Two hotels now run at a time, and the retries have 20 s.
- Rome first failed SC-001 (6 of 10). One High (90) was outweighed by five Lows (5 × 20), and one dear hotel squeezed
  the linear price score. With the owner's approval, Low is now 10 and price is scored by position in the list
  (research R-6).

### Known limits
- Ranking covers the hotels already fetched for a search, 20 on the free plan.
- The first ranked search for a city is about 5–10 s slower while the places near each hotel are counted, once a month.
- Food is how many restaurants and cafés are near, not how good they are (ADR 0008).
