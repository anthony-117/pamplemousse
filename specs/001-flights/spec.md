# Feature Specification: Flight prices for a trip

**Feature Branch**: `001-flights`

**Created**: 2026-11-02

**Status**: Draft

**Input**: User description: "TG-06: As Fares, I want to see flights for my dates."

## Clarifications

### Session 2026-11-02

- Q: Where does the departure city come from? → A: The traveller chooses it, required on every request.
- Q: Which trip types? → A: Round trip by default, one way as an option.
- Q: What if no fare exists for the exact dates? → A: Show the nearest dates within ±3 days, clearly labelled as different dates.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See recent flight prices for my trip (Priority: P1)

Fares is planning 5 days in Rome. They give their departure city, the destination and their dates, and get
a list of flights with the cheapest first. Each one shows the airline, times, number of stops and price,
and is clearly marked as a recently seen price, not a guaranteed one.

**Why this priority**: This is the backlog story. Flight cost is usually the largest part of a trip
budget, and later features (weighted search, itineraries) depend on these results.

**Independent Test**: Request flights for a popular route and valid dates through the API, and confirm
the list comes back ordered by price with every field present. Works without any other new feature.

**Acceptance Scenarios**:

1. **Given** a departure city, a destination and valid dates, **When** Fares asks for flights, **Then** they receive flights ordered from cheapest to most expensive.
2. **Given** a list of flights, **When** Fares reads a flight, **Then** they see its airline, departure and return times, stops, duration when known, and price in their chosen currency.
3. **Given** any flight in the list, **When** Fares reads its price, **Then** it is marked as a recent price and the response says when prices may have changed.
4. **Given** no currency is chosen, **When** Fares asks for flights, **Then** prices are shown in the default currency (USD), as elsewhere in the app.
5. **Given** the same request is repeated soon after, **When** Fares asks again, **Then** they get the same results without a new call to the external source.
6. **Given** one way is requested, **When** Fares asks for flights, **Then** the flights have no return and their prices cover one direction.
7. **Given** no fare exists for the exact dates but one exists 2 days later, **When** Fares asks for flights, **Then** that fare is shown with its actual dates and labelled as different from the request.
8. **Given** no fare exists within 3 days of the dates, **When** Fares asks for flights, **Then** the list is empty, with a message and a link to search the booking site.
9. **Given** no departure city, **When** Fares asks for flights, **Then** the request is rejected, naming the missing field.

---

### User Story 2 - Open a flight to book it (Priority: P2)

Fares picks a flight and opens a link to the booking site, to see the live price and buy.
TravelGenie does not sell the ticket itself.

**Why this priority**: Without this a price is only information. It is also how the project earns
affiliate commission. It depends on Story 1.

**Independent Test**: Take the link of a returned flight, open it, and confirm it reaches the booking
site for that route and those dates and carries the project's partner identifier.

**Acceptance Scenarios**:

1. **Given** a flight in the list, **When** Fares opens its link, **Then** they reach the booking site for that route and those dates.
2. **Given** any booking link, **When** it is inspected, **Then** it carries the project's partner identifier.
3. **Given** the partner identifier is not configured, **When** Fares asks for flights, **Then** flights still appear with working links, and a warning says commission tracking is off.

---

### User Story 3 - Direct flights only (Priority: P3)

Fares does not want connections, so asks only for direct flights.

**Why this priority**: Useful but not essential; the stops are already visible in Story 1.

**Independent Test**: Request direct flights only for a route with both kinds, and confirm no returned flight has stops.

**Acceptance Scenarios**:

1. **Given** direct-only is requested, **When** flights are returned, **Then** every flight has zero stops.
2. **Given** direct-only is requested and none exist, **When** Fares asks, **Then** they get the "no flights" outcome in FR-009, not an error.

---

### Edge Cases

- **Source down or rate-limited**: the response is still successful, with no flights and a warning that flights are unavailable right now.
- **Access token not configured**: no flights, and a warning naming the missing variable. The app does not crash.
- **Departure and destination are the same city**: rejected as bad input.
- **Unknown destination or departure city**: not found.
- **City with no airport of its own** (many of the 34,000 cities): uses the nearest city with an airport, and the response says which one. If none is reasonably near, the "no flights" outcome applies.
- **Dates in the past, reversed, or over 30 days apart**: rejected, using the same rules as the rest of the API.
- **A price the source reports as zero or negative, or a flight with no price**: left out of the list.
- **The same flight reported twice**: shown once.
- **Dates far in the future**: treated like any other date; few or no prices are expected.
- **Nearby dates that fall in the past**: never shown.
- **A one-way request with a return date**: the return date is ignored.
- **Unexpected or broken answer from the source**: same as source down.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Users MUST be able to request flights by giving a destination and dates.
- **FR-002**: The traveller MUST choose the departure city on every flight request; a request without one MUST be rejected as bad input. System MUST NOT guess it. Clients MAY remember the last choice.
- **FR-003**: Trips MUST be round trip by default, and users MUST be able to ask for one way instead. A one-way flight has no return time, and its price covers one direction only.
- **FR-004**: System MUST return flights ordered by price, cheapest first, up to a maximum of 50, defaulting to 20.
- **FR-005**: Each flight MUST include: airline, departure time, return time when it has one, stops, duration when known, price with currency, booking link, and when known, the flight number.
- **FR-006**: System MUST mark every price as a recent price, not a live or guaranteed one.
- **FR-007**: Booking links MUST lead to the booking site for that flight's route and dates, and carry the partner identifier when configured.
- **FR-008**: Users MUST be able to ask for direct flights only.
- **FR-009**: When no fare exists for the exact dates, System MUST return fares from the nearest dates, up to 3 days before or after, each clearly labelled with its actual dates and as not matching the request. Exact-date fares MUST always come first. If nothing is found within 3 days, the response MUST be an empty list with a message saying so and a link to search the booking site, not an error.
- **FR-010**: Users MUST be able to choose the currency; the default MUST be USD.
- **FR-011**: A failing or unconfigured flight source MUST produce a warning and an empty list, never a failed response.
- **FR-012**: Results MUST be reused for about an hour for identical requests, to stay within the source's limits.
- **FR-013**: The source of flight data MUST be replaceable without changing how flights are requested or returned.
- **FR-014**: Cities MUST be matched to the codes the flight source uses. When a city has no airport, System MUST use the nearest city that has one and state this in the response.
- **FR-015**: Invalid input (missing departure city, same city twice, bad dates, unknown currency format) MUST return a clear error naming the field.
- **FR-016**: No flights MUST be stored permanently, and no personal data MUST be logged.

### Key Entities

- **Flight offer**: One priced option for a route and dates: airline, flight number, times, stops, duration, price, booking link, source, and whether it matches the exact dates.
- **Flight search**: What was asked: departure city (required), destination, dates, trip type (round trip by default), currency, direct-only.
- **Airport city**: A city as the flight source knows it, with its code, linked to a TravelGenie destination; it may be a nearby city.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For at least 8 of 10 popular routes checked by hand (e.g. Paris–Rome, Beirut–Paris, London–New York), a request for dates 1–3 months ahead returns at least one flight, counting nearby dates.
- **SC-002**: 100% of returned flights have a price, a booking link and the recent-price marking, and 100% of flights from nearby dates are labelled as such.
- **SC-003**: A repeated identical request returns in under 1 second.
- **SC-004**: A first request returns in under 5 seconds, or returns a warning instead of holding up the page.
- **SC-005**: With the source down or no token configured, 100% of requests still succeed, with a warning that explains why.
- **SC-006**: 10 of 10 sampled booking links open the right route and dates on the booking site and carry the partner identifier.
- **SC-007**: Across the 10 routes, the first-listed price is within 25% of the live price on the booking site in at least 7 cases, so that "recent price" is honest enough to plan with.

## Assumptions

- **Scope is the backend API only.** The flight screen in `chocomousse` is a separate spec, and needs an approved Figma design first (design v0.2 has no dedicated flight results screen for this; it appears within plan setup and results).
- The source is the Travelpayouts (Aviasales) Data API (ADR 0010). Its prices are cached, not live, which is why FR-006 exists. Its help pages block automated readers, so the fields and the search options above still need confirming with a real token during planning.
- Booking happens on the partner's site; there is no in-app booking, seat choice or passenger details.
- No user account is needed, as with the rest of the current API.
- One adult, economy class: the source's cached prices do not vary by passenger count or cabin as far as we know; to confirm during planning.
- Multi-city trips are out of scope.
- Flight offers feed weighted search (TG-09) later, but scoring is not part of this feature.
- A free Travelpayouts account is needed before this can be tested live. The token and marker go in `.env` when implementation starts.
