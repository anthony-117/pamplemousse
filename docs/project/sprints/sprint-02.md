# Sprint 2 review — in progress

This records the Flights feature (TG-06) and the hotels (TG-07) and cars (TG-08) backends, built with Spec Kit: [flights](../../../specs/001-flights/), [hotels](../../../specs/002-hotels/), [cars](../../../specs/004-cars/). The car screens are in `chocomousse` (`specs/004-car-screen`).

## Delivered: Hotels (backend)
`GET /api/v1/destinations/{id}/hotels` and `GET /api/v1/hotels/{hotelId}`, from the Booking COM API (ADR 0016). Searches are kept in Redis for 12 hours and every hotel, city and price is stored in Firestore (ADR 0017). The screens are in `chocomousse`, with their own spec (`specs/002-hotel-screen`) and an approved Figma design: a list with sort, filters and pages (a side panel on wide screens), hotel details, and an adults setting shared with flights. `totalPages` was added to the search response for the pager.

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

### Live check of the screens, 4 Nov 2026
Rome, 4–8 Dec 2026, on phone and wide layouts against the local API: photos, stars, scores and totals show; hotels without reviews hide the score; details open with photos, facilities and the Booking.com link; sorted by lowest price with 4+ stars returns the cheapest first and "Page 1 of 503".

Found: the list's distance is from TravelGenie's point for the city and the details' distance is from Booking.com's city centre, so one hotel can read 3.6 km in the list and 2.1 km in its details.

Still to check by hand (quickstart): prices against Booking.com for 10 hotels (SC-003), 10 links (SC-007), Zahlé marked "near the city", and whether a link built from the hotel ID alone opens the right hotel (R-5).

### Known limits
- **Free plan:** one page of 20 hotels per search, and details only when a hotel is opened. So the free-cancellation and type-of-stay filters only cover hotels already opened; the response says how many.
- **First search for a city** is slower (about 7 s), once per city.
- **The last price of a hotel** is kept for the last dates searched; opening it for other dates shows no price until it is searched for those dates.

## Delivered: Cars (backend)
`GET /api/v1/destinations/{id}/car-places` and `GET /api/v1/destinations/{id}/cars`, from the same Booking COM API as hotels. One call to the source returns every car (up to 500), so a search is kept in Redis for a day and saved as one Firestore snapshot with the source's whole answer, compressed. Sorting, filters and pages never call the source again. A city's airports are looked up once and stored on the city for 30 days. The Figma design is approved (page "Sprint 2 — Cars"). The screens are in `chocomousse`, with their own spec (`specs/004-car-screen`): a list with the pick-up place and driver's age, sort, filters and pages (a side panel on wide screens), car details, and a Car rental entry on the overview. The driver's age is a trip setting, 18–99, default 30.

| Story | Result |
|---|---|
| Cars and prices for my trip | ✅ Normal Booking.com price for the whole rental and per day, pick-up and return at 10:00; the same car at the same desk is shown once, at its lowest price |
| Pick-up at the centre or an airport, and the driver's age | ✅ Centre or airports within 50 km; age 18–99 with a note under 25 and over 70 |
| Sort and filter | ✅ Recommended, price, company rating; size, gearbox, seats, bags, company, pick-up type, price, free cancellation |
| Open Booking.com | ✅ The link opens Booking.com's car search for the same place, dates, times and age. The source's own per-car link opens "page not found", so no link opens one car |

### Live checks, 5 Nov 2026
Real key, local API without Aspire, Rome 4–8 Dec 2026. 4 requests of the monthly 530.

| Check | Result |
|---|---|
| Rome pick-up places | Centre, Ciampino (CIA), Fiumicino (FCO); 2.4 s, once per city |
| Rome centre, age 30 | 415 cars after removing repeats (500 from the source, which counted 1,054); 15.6 s |
| Same search, cheapest automatic first | 182 cars, 0.03 s, no new call |
| Fiumicino, age 23 | 420 cars and the young-driver note; 26.3 s |
| Prices against Booking.com | Fiat 600 at Budget, Rome: 76.76 vs US$77. Opel Corsa at Record Go, Fiumicino: 62.89 vs US$63 |
| Links | Centre and Fiumicino (`ftsType=A`) open the right place, dates, 10:00 and age. Found and fixed: the centre link filled Booking.com's search box with "City centre"; it now says the city |

### Screen check, 5 Nov 2026 (evening)
The app (`chocomousse` web build) against the local API, Rome and Zahlé 5–9 Dec 2026, at 390 px and 1440 px. 8 requests of the monthly 530 that day, counting the failed tries below.

| Check | Result |
|---|---|
| First tries | Three Rome centre searches failed: headers after 5–16 s, then the body still arriving at the 45 s limit. Cause: the client didn't ask for compression, so the source sent several MB of plain JSON. Fixed: both Booking COM clients ask for gzip, deflate or brotli |
| Rome centre, age 30 | 465 cars, 18 s (headers at 14.6 s, the compressed body about 3 s). The card matches the design: Fiat 600 at Budget, US$77 total, US$19 a day, pay now |
| Car details | Photo, company, address, pick-up and return at 10:00, the car's specs, deposit €200, damage excess €3,294, fuel, payment, price box and the Booking.com button |
| Fiumicino, age 23, from the pick-up sheet | 445 cars in 8 s (26.3 s before compression) and the young-driver note; Opel Corsa at Record Go US$63 |
| Lowest price + automatic, from the filter sheet | No new call; the sheet's sizes, pick-up types and companies come from the search, "Filters cover all 445 cars" |
| Zahlé | Only the city centre (no airport the source knows within 50 km); 201 cars in 6 s, nearest desks around Beirut, 33–38 km |
| Wide layout | Side panel with the trip chips and filters, cars beside it |
| Failed search with nothing saved | "Cars could not be searched" with Try again and the Booking.com search |

Still to check (quickstart): 9 more cities and 3 more airports (SC-001), 10 prices (SC-003), 10 links (SC-007), and a wrong key giving saved cars.

### Known limits
- **At most 500 cars per search**, with no paging at the source. Rome has about 1,050, so the response says `mayHaveMore` and links to Booking.com for the rest.
- **An airport search also returns desks in the city:** Fiumicino gave 100 downtown and 120 train-station cars. The pick-up type filter narrows them.
- **First search is slow:** 15–30 s at the source, once a day per place, dates and driver age.
- **A failed search costs a request:** a search the source fails is not saved, so Try again pays again. A search the app gives up on is no longer lost: it runs to the end and is saved for the retry.

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
- Cars: the remaining quickstart checks (more cities and airports, prices, links, a wrong key).
