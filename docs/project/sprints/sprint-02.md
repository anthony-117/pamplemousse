# Sprint 2 review — in progress

Cars are still to do. This records the Flights feature (TG-06) and the hotels backend (TG-07), built with Spec Kit: [flights](../../../specs/001-flights/), [hotels](../../../specs/002-hotels/).

## Delivered: Hotels (backend)
`GET /api/v1/destinations/{id}/hotels` and `GET /api/v1/hotels/{hotelId}`, from the Booking COM API (ADR 0016). Searches are kept in Redis for 12 hours and every hotel, city and price is stored in Firestore (ADR 0017). The hotels screen in `chocomousse` needs its own spec and an approved Figma design.

| Story | Result |
|---|---|
| Hotels and prices for my dates | ✅ Total with taxes and fees, per night, for a room that fits the adults; saved prices with their time when the source fails |
| Open a hotel to book it | ✅ Details fetched once a week; the Booking.com link has the dates, adults and one room |
| Sort and filter | ✅ By price, review score, stars, distance; filters on stars, score, price, type of stay, free cancellation. No new call to the source |

### Live checks, 3 Nov 2026
Real key and real Firestore, local API without Aspire. 10 requests of the monthly 530.

| Check | Result |
|---|---|
| Rome, 15–19 Jan 2027 | 20 hotels, all with name, photo, position and total. 15 s on the first search ever: the one-time city lookup took 5.7 s at the source, and Firestore connected for the first time (now done at startup) |
| Same search, sorted by price, review ≥ 8 | 17 hotels, cheapest first, none under 8.4; 0.6 s, no new call |
| Paris and Beirut, first search ever | 20 hotels each; 7–7.5 s, including the one-time city lookup |
| Rome, other dates (city already known) | 3.3–4.4 s, after the Firestore writes were combined |
| Open a hotel twice | Details and link right; second open 0.3–0.6 s with no new call. Found and fixed: an empty translated name from the source replaced the hotel's name |

Still to check by hand (quickstart): prices against Booking.com for 10 hotels (SC-003), 10 links (SC-007), Zahlé marked "near the city", and whether a link built from the hotel ID alone opens the right hotel (R-5).

### Known limits
- **Free plan:** one page of 20 hotels per search, and details only when a hotel is opened. So the free-cancellation and type-of-stay filters only cover hotels already opened; the response says how many.
- **First search for a city** is slower (about 7 s), once per city.
- **The last price of a hotel** is kept for the last dates searched; opening it for other dates shows no price until it is searched for those dates.

## Delivered: Flights
`GET /api/v1/destinations/{id}/flights`. The screen is in `chocomousse`, with its own spec (`specs/001-flight-screen`) and an approved Figma design. A missing partner marker is now logged for us, not sent to the traveller.

| Story | Result |
|---|---|
| Recent prices for a route and dates | ✅ Cheapest first; exact dates, then within 3 days, then the same month, then for round trips any return date, each labelled |
| Open a flight to book it | ✅ Links open the right route and dates and carry the partner marker. Commission only shows in the Travelpayouts dashboard after a real sale |
| Direct flights only | ✅ |

## Live checks, 2 Nov 2026
Round trips requested for 10–14 Dec 2026, against the real source.

| Route | Date level | Our first price | Figure on booking site | Difference |
|---|---|---|---|---|
| Paris–Rome | Nearby | $82 | $77 | +6% |
| Beirut–Paris | None | – | $248 | No fares in the source |
| London–New York | Nearby | $809 | $507 | **+60%** |
| Beirut–Dubai | Same month | $305 | $307 | −1% |
| Madrid–Rome | Exact | $74 | $75 | −1% |
| Berlin–Istanbul | Nearby | $138 | $137 | +1% |
| Rome–Athens | Same month | $117 | $124 | −6% |
| Beirut–Istanbul | Nearby | $161 | $158 | +2% |
| Paris–Tokyo | Nearby | $807 | $765 | +5% |
| Dubai–London | Exact | $449 | $387 | +16% |

The site figure is the first price on its results page for the same dates, not necessarily the same flight.

| Criterion | Target | Result |
|---|---|---|
| SC-001 Routes with a flight | At least 8 of 10 | ✅ 9 of 10 |
| SC-002 Complete, labelled flights | 100% | ✅ |
| SC-003 Repeated request | Under 1 s | ✅ At most 0.04 s |
| SC-004 First request | Under 5 s, or a warning | ✅ At most 1.6 s; 5 s timeout |
| SC-005 No token | 100% still succeed, with a warning | ✅ |
| SC-006 Links | 10 of 10 right route, dates, marker | ✅ Route and dates on 10 of 10; marker in all 10 links as sent. The site moves it to a cookie, and that cookie remained from earlier visits, so it does not prove each link separately |
| SC-007 Price within 25% | At least 7 of 10 | ✅ 8 of 9 routes that had a price |

One date range, on one day: an indication, not a measurement.

Tests: 221 passing, no analyzer findings.

## Known limits
- **Beirut:** coverage stays thin, even with the any-return level added on 2 Nov. Beirut to 11 cities (Rome, London, Madrid, Athens, Berlin, Geneva, Amsterdam, Cairo, Doha, Larnaca, Paris), for one January and one February trip: 3 of 22 searches returned a fare, and only for Paris and Madrid. The booking site showed live prices from $248 for Beirut–Paris when the source had none. Only a live source would fix this, and the owner decided against one for the MVP ([ADR 0014](../../architecture/adr/0014-no-live-flight-source-for-mvp.md)). Thin routes show a link to live prices.
- **The source is built from searches:** after the booking links were opened for the checks above, Beirut–Paris returned a fare at the price seen on the site, and other prices moved to match. Opening a route probably adds it to the data, so a re-run of the checks reads 10 of 10 routes, and that figure is not independent. The 9 of 10 above was measured first.
- **Airline names:** in 34 of 98 sampled fares, the source's `airline` field differs from the carrier in the fare's own booking link (Rome–Athens: `GQ` in the field, `FR` in the link), so the airline and flight number may be wrong. The link's format is undocumented, so we do not parse it. The API still returns these fields, and the app does not show them.
- **Few options:** several routes return only 1–3 fares, because fares must be within 3 days on both legs.
- **Embedded lists:** no published terms were found for Travelpayouts' city and airline lists, either allowing or restricting them. To be asked of their support.

## Next
- Hotels screen (spec and Figma in `chocomousse`), cars (TG-08) from the same API.
