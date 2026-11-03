# 0016 — Booking COM API as the main travel source
Date: 2026-11-03 · Status: Accepted · Replaces ADR 0014 and the flights source of ADR 0010

## Context
Travelpayouts closed its hotel API (Hotellook) after 20 Oct 2025, and its cached flight fares are thin: Beirut–Rome for 10–14 Jan 2027 and Paris–Rome for 10–14 Dec 2026 returned no fare for the exact dates.

The "Booking COM" API on RapidAPI (`booking-com18`, by ntd119) was tested on 3 Nov 2026 with 40 requests. Full report: OneDrive `01-research/07-booking-com18-api-evaluation.md`.

| Service | Finding |
|---|---|
| Hotels and rentals | Rome, Paris, Beirut and Zahlé found by name; 8 of 10 totals within 3% of Booking.com |
| Flights | Live fares from Gotogate: 1,020 options Paris–Rome, 443 Beirut–Rome; the checked price matched Gotogate's site; correct airline and flight number |
| Car rentals | 500 cars at Rome airport, 204 at Beirut; prices not verified; about 30 s per search |
| Attractions | 6,643 in Rome, 3,805 in Paris, none in Beirut; 3 of 3 prices exact |
| Taxis | No data returned |

It is an unofficial copy of Booking.com's and Gotogate's data, sold by one seller. Its documentation page did not load, so endpoints come from tests and open-source users.

## Decision
- The owner chose to rely heavily on this API. It is the main source for hotels, flights, car rentals and attractions. Taxis are not used.
- Each service sits behind its own Application interface (`IHotelProvider`, `IFlightProvider`, …), so the source can be replaced (constitution, principle IV).
- Travelpayouts stays as the flight fallback when the API fails or its monthly requests run out.
- Wikidata and OpenTripMap stay for free attraction and restaurant lists (ADR 0011). This API adds bookable tickets with prices.
- The key is `RAPIDAPI_KEY` in `.env`. Development uses the free Basic plan (530 requests a month).
- Storage and caching follow ADR 0017.

## Still open
- **Commission:** the API's links go to Booking.com and Gotogate without our partner ID. Whether bookings go through those links or through Travelpayouts partner links (Booking.com, Agoda, Trip.com, Aviasales) is decided in each feature's spec.
- **Pro plan:** the Pro plan ($15 a month, 40,500 requests) needs the owner's approval before real users (constitution, principle V).
- **Cheapest flights first:** flights come 10 per page, sorted by "recommended", and sorting by price wasn't found. This must be solved in the flights spec.

## Consequences
- Live prices and much better coverage, Beirut included, from one key.
- One unofficial seller: it can break or stop without notice. Fallbacks and saved prices (ADR 0017) keep pages working.
- Using copied Booking.com data without a partnership is acceptable for the university project. It must be reviewed before a commercial launch.
- The request limit is hard, so every call goes through the cache and request counter in ADR 0017.
