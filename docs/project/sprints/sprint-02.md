# Sprint 2 review — in progress

Hotels and cars are still to do. This records the Flights feature (TG-06), built with Spec Kit: [spec and plan](../../../specs/001-flights/).

## Delivered: Flights
`GET /api/v1/destinations/{id}/flights`, backend only. The screen in `chocomousse` needs its own spec and an approved Figma design.

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
- **Airline names:** in 34 of 98 sampled fares, the source's `airline` field differs from the carrier in the fare's own booking link (Rome–Athens: `GQ` in the field, `FR` in the link), so the airline and flight number shown may be wrong. The link's format is undocumented, so we do not parse it.
- **Few options:** several routes return only 1–3 fares, because fares must be within 3 days on both legs.
- **Embedded lists:** no published terms were found for Travelpayouts' city and airline lists, either allowing or restricting them. To be asked of their support.

## Next
- Decide how to show airlines.
- Hotels (TG-07), cars as links (TG-08), Redis in Aspire.
