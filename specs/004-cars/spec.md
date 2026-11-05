# Feature Specification: Car rentals for a trip

**Feature Branch**: `004-cars`

**Created**: 2026-11-05

**Status**: Draft, waiting for the owner's approval

**Input**: User description: "TG-08: As Fares, I want to see car rental options."

## Clarifications

### Session 2026-11-05

- Q: Which source? → A: The Booking COM API on RapidAPI (ADR 0016), `/car/search`, the same key and monthly quota as hotels.
- Q: Where is the car picked up? → A: Picked up and returned at the same place. By default the city centre (the destination's own position, no extra request). The traveller can switch to one of the city's airports, found with one look-up per city that is then stored. One-way rentals come later.
- Q: What does the traveller set? → A: The driver's age only (default 30, from 18 to 99). Pick-up and return are at 10:00 on the trip's first and last day. Under 25 or over 70, a note says the rental company may charge extra.
- Q: Which price? → A: The normal price Booking.com shows to anyone not signed in, not the member price the source sometimes adds. Checked on 5 Nov: 8 of 9 Rome cars within 3% of the site.
- Q: The search takes 20–30 s. When does it run? → A: Only when the traveller opens Cars, with a progress state. Results are kept for 1 day (ADR 0017), so coming back is instant.
- Q: Where does "View deal" go? → A: The source's link for each car opens "page not found" on Booking.com (checked again on 5 Nov). The link opens Booking.com's car search for the same place, dates and driver age instead, where the traveller picks the car. No partner link.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See rental cars and their prices for my trip (Priority: P1)

Fares and Anthony are spending 4 days in Rome. Fares opens Cars for Rome with the trip's dates, and
gets a list of cars to rent in the city centre. Each one shows the car, its size, gearbox, seats and
bags, the rental company and its rating, where to pick it up, and the total price for the rental.

**Why this priority**: This is the backlog story. A car is part of many trip budgets, and weighted
search (TG-09) will weigh it with flights and hotels.

**Independent Test**: Request cars for Rome and valid dates through the API, and confirm the list
comes back with every field and a total price on each car. Works without any other new feature.

**Acceptance Scenarios**:

1. **Given** a destination and valid dates, **When** Fares asks for cars, **Then** they receive a list of cars available to pick up and return at the city centre from 10:00 on the first day to 10:00 on the last day.
2. **Given** a list of cars, **When** Fares reads a car, **Then** they see its model ("or similar"), size class, photo, gearbox, seats, doors, bags, air-con, mileage, the rental company's name, logo and rating when it has one, the pick-up place's name, type and distance from the centre, and the total price.
3. **Given** any car price, **When** Fares reads it, **Then** it is the total for the whole rental in their chosen currency, the price per day is also given, whether to pay now or at pick-up is shown, free cancellation is shown when offered, and the response says when prices were fetched.
4. **Given** the deposit and damage excess the source reports, **When** Fares reads a car, **Then** they are shown with their own currency, since they are often in the local currency.
5. **Given** no currency is chosen, **When** Fares asks for cars, **Then** prices are in USD, as elsewhere in the app.
6. **Given** the same request was answered less than 1 day ago, **When** Fares asks again, **Then** they get the same results at once, without a new call to the source.
7. **Given** no car is available, **When** Fares asks, **Then** the list is empty, with a message and a link to search Booking.com, not an error.

---

### User Story 2 - Pick up at the airport and set the driver's age (Priority: P2)

Fares lands at Fiumicino and is 23. They switch the pick-up place to the airport and set their age,
and the list updates for those.

**Why this priority**: The city centre and age 30 give useful prices for most trips; this makes them
right for the rest. It depends on Story 1.

**Independent Test**: List Rome's pick-up places, request cars at Fiumicino for a 23-year-old, and
confirm the cars are picked up at or near Fiumicino and the young-driver note is present.

**Acceptance Scenarios**:

1. **Given** a destination, **When** Fares asks for its pick-up places, **Then** they get the city centre first, then the city's airports by name and code.
2. **Given** an airport is chosen, **When** Fares asks for cars, **Then** the cars are for pick-up and return at that airport.
3. **Given** a driver's age, **When** Fares asks for cars, **Then** the prices are for a driver of that age.
4. **Given** a driver under 25 or over 70, **When** cars are returned, **Then** the response carries a note that the rental company may charge a young or senior driver fee.
5. **Given** a city's airports were already looked up, **When** anyone asks again, **Then** no new call to the source is made.

---

### User Story 3 - Open a car to book it (Priority: P2)

Fares picks a car and goes to Booking.com to book it. TravelGenie does not rent the car itself.

**Why this priority**: Without it a price is only information. It depends on Story 1.

**Independent Test**: Take a returned car, open its link, and confirm Booking.com's car search opens
with the same place, dates and driver age.

**Acceptance Scenarios**:

1. **Given** a car in the list, **When** Fares opens it, **Then** Booking.com's car search opens with the same pick-up place, dates, times and driver age filled in.
2. **Given** the link opens a search rather than the car itself, **When** Fares reads the car in TravelGenie, **Then** the app tells them to look for that car and company in the results.

---

### User Story 4 - Sort and filter (Priority: P3)

Fares wants the cheapest automatic with at least 5 seats and free cancellation. TravelGenie answers
from the cars it has already stored, so changing a sort or filter doesn't cost a new search.

**Why this priority**: Useful but not essential; 500 cars are hard to read without it. It depends on
the stored results of Story 1.

**Independent Test**: Request cars for a search already answered, sorted by price and filtered to
automatics with 5 or more seats, and confirm the order, that every car matches, and that no call to
the source was made.

**Acceptance Scenarios**:

1. **Given** sort by recommended (the source's order), price or the company's rating, **When** cars are returned, **Then** they are in that order.
2. **Given** filters on size class, gearbox, minimum seats, minimum bags, rental company, pick-up place type, free cancellation or maximum price, **When** cars are returned, **Then** every car matches them.
3. **Given** a search already answered, **When** Fares changes only the sort or filters, **Then** no new call to the source is made.
4. **Given** filters that nothing matches, **When** Fares asks, **Then** they get the empty outcome in Story 1, scenario 7.

---

### Edge Cases

- **Source down, rate-limited, slow past the time limit, or a broken answer**: the last saved results for that search are returned, labelled with the time they were fetched. If there are none, an empty list with a warning and a link to Booking.com.
- **Monthly request limit almost used up (95%, shared with hotels)**: same as source down; no new call is made.
- **API key not configured**: no cars, and a warning naming the missing variable. The app does not crash.
- **Two travellers ask for the same new search at once**: only one call is made; the other waits for its result.
- **Unknown destination**: not found.
- **A city with no airport the source knows**: only the city centre is offered.
- **A pick-up place that isn't one of the destination's**: rejected, naming the field.
- **Dates in the past, reversed, or over 30 days apart**: rejected, using the same rules as the rest of the API. A one-day trip is a one-day rental.
- **Driver age outside 18–99**: rejected, naming the field.
- **A car with no price, or a price of zero or less**: left out.
- **The same car from the same company and place reported twice**: shown once, at the lower price.
- **A car the source marks as an ad**: shown in its place, marked as sponsored by the source.
- **More than 500 cars**: the source returns at most 500; the response says when it may have more.
- **A car with no rating, photo or bag count**: shown, with those fields empty, not zero.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Users MUST be able to request cars by giving a destination, the trip's first and last day, a pick-up place (default the city centre) and the driver's age (default 30, from 18 to 99). Pick-up and return are at the same place, at 10:00.
- **FR-002**: Users MUST be able to list a destination's pick-up places: the city centre, then the airports the source knows for that city, with name and IATA code.
- **FR-003**: Each car MUST include: model, "or similar" wording, size class, photo, gearbox, seats, doors, big and small bags, air-con, mileage, fuel policy, rental company name and logo, company rating out of 10 and review count when known, pick-up place name, type (terminal, shuttle, rental centre, train station, downtown), address and distance from the destination, total price, price per day, currency, pay now or at pick-up, free cancellation, deposit and damage excess with their currency, and whether the source marks it as sponsored.
- **FR-004**: The price MUST be the normal price the source reports (`drive_away_price_before` when given, otherwise `drive_away_price`), not a member discount.
- **FR-005**: The response MUST say when its prices were fetched from the source.
- **FR-006**: System MUST return up to 20 cars per page, and users MUST be able to ask for the next page.
- **FR-007**: The default order MUST be the source's recommended order. Users MUST be able to sort by price and by the company's rating.
- **FR-008**: Users MUST be able to filter by size class, gearbox, minimum seats, minimum bags, rental company, pick-up place type, free cancellation and maximum price; the response MUST list the values available for each filter in this search.
- **FR-008a**: Sorting and filtering MUST run on TravelGenie's stored results, with no new call to the source.
- **FR-008b**: System MUST store every field the source returns for a car, not only the fields shown today, so that new sorts and filters need no new calls.
- **FR-009**: Users MUST be able to choose the currency; the default MUST be USD.
- **FR-010**: Each car MUST carry a link to Booking.com's car search with the same place (by coordinates), dates, times and driver age. No partner link is used.
- **FR-011**: Results MUST be reused for 1 day for identical requests (ADR 0017), and only one call MUST be made when the same new search is asked for at once.
- **FR-012**: Every call to the source MUST be saved as one price-history record: the search, the time, and each car's ID, company, place and total price (ADR 0017).
- **FR-013**: A city's airports from the source MUST be stored and reused, and checked again monthly.
- **FR-014**: A failing, slow or unconfigured source, or a used-up monthly limit, MUST return the last saved results for that search with their time, or an empty list with a warning, never a failed response.
- **FR-015**: A driver under 25 or over 70 MUST get a note that extra fees may apply.
- **FR-016**: The car source MUST be replaceable without changing how cars are requested or returned.
- **FR-017**: Invalid input (bad dates, age out of range, unknown pick-up place, bad currency format) MUST return a clear error naming the field.
- **FR-018**: No personal data MUST be logged or stored; the driver's age is part of the search, not linked to anyone.

### Key Entities

- **Car offer**: one car to rent for a search: car, rental company, pick-up place, total price, price per day, currency, payment and cancellation terms, deposit and excess, sponsored flag, and when the price was fetched.
- **Car search**: what was asked: destination, pick-up place, first and last day, driver age, currency, sort, filters, page.
- **Pick-up place**: the city centre or an airport: name, IATA code for airports, position.
- **Price snapshot**: one saved call to the source: the search, the time, and each car's ID, company, place and price.
- **City mapping** (exists since hotels): gains the city's car pick-up airports.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For Rome, Paris, Beirut and 7 other cities checked by hand, dates 1–3 months ahead return at least 20 cars at the city centre, and at least 20 at the main airport where there is one.
- **SC-002**: 100% of returned cars have a model, size class, company, pick-up place and total price.
- **SC-003**: For 10 cars checked by hand on Booking.com, the total is within 5% of the site's price in at least 8 cases.
- **SC-004**: A repeated identical request, or a change of sort or filter, returns in under 1 second.
- **SC-005**: A first request returns within 40 seconds, or returns saved results or a warning instead.
- **SC-006**: With the source down, no key, or the limit used up, 100% of requests still succeed, with saved results or a warning.
- **SC-007**: 10 of 10 sampled links open Booking.com's car search with the right place, dates and driver age.

## Assumptions

- **Scope is the backend API only.** The cars screen in `chocomousse` is a separate spec, with an approved Figma design first.
- The source is the Booking COM API on RapidAPI (ADR 0016), key `RAPIDAPI_KEY`, the same free Basic plan (530 requests a month) and monthly counter as hotels. One car search costs 1 request and returns everything (up to 500 cars, about 2.3 MB), so there are no extra pages to fetch. One airport look-up per city costs 1 request.
- Checked on 5 Nov 2026 (2 requests): the source's place IDs are base64-encoded coordinates, so the city centre needs no look-up; Rome city centre, 4–8 Dec, driver 30, returned 500 cars from 15 companies in 23 s; Booking.com's site showed 1,054 for the same search.
- Redis and Firestore are already set up by hotels (ADR 0017); cars use the same lock, counter and stores.
- Same pick-up and return place; one-way rentals, other times and extras (child seats, extra driver, insurance) are out of scope.
- Booking happens on Booking.com; there is no in-app booking.
- No user account is needed, as with the rest of the current API.
- Car offers feed weighted search (TG-09) later, but scoring is not part of this feature.
