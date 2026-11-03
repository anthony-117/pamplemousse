# Feature Specification: Hotel prices for a trip

**Feature Branch**: `002-hotels`

**Created**: 2026-11-03

**Status**: Approved by the owner on 2026-11-03

**Input**: User description: "TG-07: As Fares, I want to see hotels and their prices for my dates."

## Clarifications

### Session 2026-11-03

- Q: Which source? → A: The Booking COM API on RapidAPI (ADR 0016), live prices.
- Q: How old can a shown price be? → A: Up to 12 hours (ADR 0017).
- Q: Keep old prices? → A: Yes, as price history (ADR 0017).
- Q: Where does "Book" go? → A: The hotel's own Booking.com page, with the dates and travellers filled in. No partner link.
- Q: Who can search? → A: Adults only (1–8, default 2), one room. Children and several rooms come later.
- Q: Sorting and filtering? → A: Store as much of the source's data as possible, then sort and filter on our own stored data. Use the source's sorting and filtering only where our data can't answer.
- Q: Where are Redis and Firestore set up? → A: In this feature, the first to need them.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See hotels and their prices for my trip (Priority: P1)

Fares is planning 5 days in Rome for two. They open hotels for Rome with their dates and number of
travellers, and get a list of places to stay. Each one shows its name, photo, stars, review score,
location and the total price for the stay with taxes and fees included.

**Why this priority**: This is the backlog story. After flights, the stay is the largest part of a
trip budget, and later features (weighted search, itineraries) depend on these results.

**Independent Test**: Request hotels for Rome and valid dates through the API, and confirm the list
comes back with every field and a total price on each hotel. Works without any other new feature.

**Acceptance Scenarios**:

1. **Given** a destination, valid dates and a number of adults, **When** Fares asks for hotels, **Then** they receive a list of places to stay available for those dates.
2. **Given** a list of hotels, **When** Fares reads a hotel, **Then** they see its name, main photo, stars when it has them, review score and count when it has them, position on the map, and total price for the stay in their chosen currency.
3. **Given** any hotel price, **When** Fares reads it, **Then** it is the total for the whole stay with taxes and fees included, the price per night is also given, and the response says when prices were fetched.
4. **Given** two adults, **When** Fares asks for hotels, **Then** no price shown is for a room that holds fewer than two people.
5. **Given** no currency is chosen, **When** Fares asks for hotels, **Then** prices are in the default currency (USD), as elsewhere in the app.
6. **Given** the same request was answered less than 12 hours ago, **When** Fares asks again, **Then** they get the same results without a new call to the source.
7. **Given** a place with few hotels, such as Zahlé, **When** Fares asks for hotels, **Then** places nearby are included and marked as near the city.
8. **Given** no hotel is available for those dates, **When** Fares asks for hotels, **Then** the list is empty, with a message and a link to search the booking site, not an error.

---

### User Story 2 - Open a hotel to book it (Priority: P2)

Fares picks a hotel and opens it on the booking site, to see the rooms and book. TravelGenie does not
sell the stay itself.

**Why this priority**: Without this a price is only information. It depends on Story 1.

**Independent Test**: Take a returned hotel, open its link, and confirm it reaches that hotel on the
booking site with the same dates and number of travellers.

**Acceptance Scenarios**:

1. **Given** a hotel in the list, **When** Fares opens it, **Then** they reach that hotel on the booking site with their dates and number of travellers filled in.
2. **Given** a hotel's link has already been fetched, **When** anyone opens it again, **Then** no new call to the source is made.

---

### User Story 3 - Sort and filter (Priority: P3)

Fares wants the cheapest first, or only well-rated hotels with at least 3 stars and free cancellation.
TravelGenie answers from the hotel data it has already stored, so changing a sort or filter doesn't
cost a new call to the source.

**Why this priority**: Useful but not essential; the list already shows price, stars and rating. It
depends on the stored data from Story 1.

**Independent Test**: Request hotels for a search already answered, sorted by price and filtered by
a minimum review score, and confirm the order, that no hotel falls below the score, and that no call
to the source was made.

**Acceptance Scenarios**:

1. **Given** sort by price, review score, stars or distance from the centre, **When** hotels are returned, **Then** they are in that order.
2. **Given** filters on any stored field (for example minimum stars, minimum review score, price range, type of stay, free cancellation), **When** hotels are returned, **Then** every hotel matches them.
3. **Given** a search already answered, **When** Fares changes only the sort or filters, **Then** no new call to the source is made.
4. **Given** filters that nothing matches, **When** Fares asks, **Then** they get the empty outcome in Story 1, scenario 8.

---

### Edge Cases

- **Source down, rate-limited or a broken answer**: the last saved prices for that search are returned, labelled with the time they were fetched. If there are none, an empty list with a warning and a link to the booking site.
- **Monthly request limit almost used up (95%)**: same as source down; no new call is made.
- **API key not configured**: no hotels, and a warning naming the missing variable. The app does not crash.
- **Unknown destination**: not found.
- **A city the source doesn't know by name**: the nearest city it knows is used, and the response says which.
- **Dates in the past, reversed, or over 30 days apart**: rejected, using the same rules as the rest of the API.
- **Adults outside 1–8**: rejected, naming the field.
- **A hotel with no price, or a price of zero or less**: left out.
- **The same hotel reported twice**: shown once.
- **A hotel with no reviews or no stars**: shown, with those fields empty, not zero.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Users MUST be able to request hotels by giving a destination, check-in and check-out dates, and the number of adults (default 2, from 1 to 8), for one room.
- **FR-002**: Each hotel MUST include: name, main photo, stars when known, review score out of 10 and review count when known, latitude and longitude, total price for the stay with taxes and fees, price per night, and currency.
- **FR-003**: A price MUST only come from a room that holds at least the requested number of adults.
- **FR-004**: Prices MUST be the total including taxes and fees the source reports; when the source gives a base price and excluded taxes separately, System MUST add them.
- **FR-005**: The response MUST say when its prices were fetched from the source.
- **FR-006**: System MUST return up to 20 hotels per page, and users MUST be able to ask for the next page.
- **FR-007**: The default order MUST be the source's recommended order. Users MUST be able to sort by price, review score, stars and distance from the centre.
- **FR-008**: Users MUST be able to filter on stored hotel fields, at least: minimum stars, minimum review score, price range, type of stay, and free cancellation when the source gives it.
- **FR-008a**: Sorting and filtering MUST run on TravelGenie's stored data. The source's own sorting or filtering MUST be used only when the stored data can't answer, for example when too few stored hotels match.
- **FR-008b**: System MUST store every field the source returns for a hotel, from search results and from hotel details, and not only the fields shown today, so that new sorts and filters need no new calls.
- **FR-008c**: How many result pages are fetched per search, and whether hotel details are fetched for every listed hotel or only when one is opened, MUST be settings, so the amount of stored data can grow with the request plan.
- **FR-009**: Users MUST be able to choose the currency; the default MUST be USD.
- **FR-010**: Each hotel MUST lead to that hotel's own page on Booking.com, with the dates and number of adults filled in. No partner link is used.
- **FR-011**: Results MUST be reused for 12 hours for identical requests (ADR 0017).
- **FR-012**: Every call to the source MUST be saved as one price-history record: the search, the time, and each hotel's ID and total price (ADR 0017).
- **FR-013**: A city's source IDs and each hotel's fixed details (name, address, stars, photos, position, link) MUST be stored and reused, so they are not fetched again for every search.
- **FR-014**: A failing or unconfigured source, or a used-up monthly limit, MUST return the last saved prices for that search with their time, or an empty list with a warning, never a failed response.
- **FR-015**: The hotel source MUST be replaceable without changing how hotels are requested or returned.
- **FR-016**: Invalid input (bad dates, adults out of range, unknown currency format) MUST return a clear error naming the field.
- **FR-017**: No personal data MUST be logged or stored.

### Key Entities

- **Hotel offer**: one place to stay for a search: hotel, total price, price per night, currency, whether it is near rather than in the city, and when the price was fetched.
- **Hotel**: everything the source tells us about a place to stay that doesn't depend on dates: source ID, name, type of stay, address, stars, photos, position, review score and count, facilities, policies, booking link, and the source's full original data. Stored and refreshed weekly.
- **Hotel search**: what was asked: destination, check-in, check-out, adults, currency, sort, filters, page.
- **Price snapshot**: one saved call to the source: the search, the time, and each hotel's ID and price.
- **City mapping**: a TravelGenie destination and its IDs in the source.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For Rome, Paris, Beirut and 7 other cities checked by hand, dates 1–3 months ahead return at least 10 hotels.
- **SC-002**: 100% of returned hotels have a name, photo, position and total price, and 100% of prices are for a room that fits the requested adults.
- **SC-003**: For 10 hotels checked by hand, the total is within 5% of the cheapest suitable room on the booking site in at least 8 cases.
- **SC-004**: A repeated identical request returns in under 1 second.
- **SC-005**: A first request returns in under 5 seconds, or returns saved prices or a warning instead of holding up the page.
- **SC-006**: With the source down, no key, or the limit used up, 100% of requests still succeed, with saved prices or a warning.
- **SC-007**: 10 of 10 sampled links open the right hotel with the right dates and travellers.

## Assumptions

- **Scope is the backend API only.** The hotels screen in `chocomousse` is a separate spec, with an approved Figma design first.
- The source is the Booking COM API on RapidAPI (ADR 0016), key `RAPIDAPI_KEY`, free Basic plan (530 requests a month) during development.
- This feature is the first to use Redis and Firestore (ADR 0017); setting them up is part of its plan. Firestore needs a Firebase service account from the owner.
- One room; children and several rooms are out of scope.
- Each page of results and each hotel's details costs one request. On the free plan (530 a month), searches fetch one page and details only when a hotel is opened; the settings in FR-008c can raise this on the Pro plan.
- Booking happens on the booking site; there is no in-app booking.
- No user account is needed, as with the rest of the current API.
- Hotel offers feed weighted search (TG-09) later, but scoring is not part of this feature.
