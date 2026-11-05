# Feature Specification: Itinerary — a complete day-by-day plan with a match score

**Feature Branch**: `008-itinerary`

**Created**: 2026-11-06

**Status**: Approved by the owner on 2026-11-06. Design: Figma v0.2 screen 08 "Itinerary" and web W4 (approved 2026-10-27); screen 11 "My plans" and web W7. A Sprint 4 Figma page comes after this spec is approved.

**Input**: User description: "TG-10: As Fares, I want a complete day-by-day itinerary with a match score."

## Clarifications

### Session 2026-11-06

- Q: Who fills each day with places, meals and events? → A: Deterministic C# code, like the ranking (ADR 0002). It groups the destination's top places by area into days, adds a restaurant near each day's places for lunch and dinner, and puts events on their dates. The same input always gives the same plan. Day titles come from the day's main place ("Day 2 · Colosseum area"). No LLM writes or changes the plan in this feature.
- Q: Which flight, hotel and car go into the plan? → A: The best match of each ranked list (spec 007), using the trip's preferences. The car is left out unless the traveller adds one. The traveller can swap any of them for another offer from the same ranked list, and the days are rebuilt around it.
- Q: How is the plan's overall match worked out? → A: A weighted average of the hotel's match, the flight's match and how well the days fit the traveller's interests (culture, food, nature, nightlife), shown as a whole percentage. The details are in FR-009.
- Q: Is Pace added? → A: Yes, as a seventh preference with three levels: Relaxed, Balanced, Packed. It sets how many places a day holds. It does not change how hotels, flights or cars are ranked.
- Q: Is the plan saved? → A: Yes, to the signed-in traveller's account. The owner turned on Firebase Authentication (email and password, and Google) and decided on 2026-11-06 that **signing in is required before using the app**, with no anonymous use. Sign-in itself (screens, and the API refusing requests without a valid sign-in) is its own feature and spec, delivered before this one. This spec only relies on knowing who is signed in.
- Q: When is a plan saved? → A: Building a plan saves it straight away as a **draft**, so nothing is lost if the app is closed. The traveller can rename it, delete it, or mark it **planned**. "Booked" comes with booking (TG-13).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Get a complete plan for my trip (Priority: P1)

Fares has set their dates, departure city and preferences for 5 days in Rome. They ask for a plan and get
"Rome, 5 days · 91% match": the best hotel and flight for them, and each day laid out with times, places,
lunch and dinner nearby, events on the right evenings, the weather when it is known, and how far they walk.

**Why this priority**: This is the backlog story. Everything else in this spec builds on it.

**Independent Test**: As a signed-in traveller, ask for a plan for Rome with valid dates, a departure city and
preferences, and confirm it has one day per date, each with timed items, a hotel, a flight, a total cost and an
overall match from 0 to 100%.

**Acceptance Scenarios**:

1. **Given** a destination, dates, travellers, a departure city and preferences, **When** Fares asks for a plan, **Then** they get one day for each date of the trip, the best-matching hotel and flight, the total cost, and the plan's match as a whole percentage.
2. **Given** a day in the plan, **When** Fares reads it, **Then** each item has a start time, a name, what it is (sight, park, restaurant, event, travel), how long it lasts, and the walking distance from the item before it.
3. **Given** Culture is High and Nature Low, **When** the plan is built, **Then** the days hold more sights than parks; **given** the opposite, more parks and gardens than sights.
4. **Given** Pace is Relaxed, Balanced or Packed, **When** the plan is built, **Then** a full day holds about 2, 3 or 4 places, plus lunch and dinner.
5. **Given** a flight, **When** the plan is built, **Then** the first day starts after the flight lands and the hotel is reached, and the last day ends in time for the flight home.
6. **Given** an event in the destination on one of the trip's dates, and Nightlife or Culture is not Low, **When** the plan is built, **Then** the best-fitting event is placed on its date at its time, and the day's other items fit around it.
7. **Given** the weather forecast covers a day, **Then** the day shows its summary and temperatures; otherwise the day shows no weather and nothing is invented.
8. **Given** the same trip, preferences and offers, **When** the plan is built twice, **Then** both plans are the same.

---

### User Story 2 - Swap the hotel, flight or car (Priority: P2)

Fares prefers the second hotel in the ranked list because it is closer to the centre. They swap it in, and the
plan's days, distances, cost and match change to fit the new hotel. They also add a rental car.

**Why this priority**: The best match is a starting point; the traveller decides. It depends on Story 1.

**Independent Test**: Swap the hotel of a plan for another from the ranked list and confirm the hotel, the
walking distances, the total and the match change, and that the paid hotel source was not called again.

**Acceptance Scenarios**:

1. **Given** a plan, **When** Fares picks another hotel from the ranked list, **Then** the plan uses it, the days are rebuilt around it, and the cost and match are updated.
2. **Given** a plan, **When** Fares picks another flight, **Then** the first and last days are fitted to its times.
3. **Given** a plan with no car, **When** Fares adds one from the ranked car list, **Then** it is in the plan and the cost; **When** they remove it, it is gone.
4. **Given** any swap, **Then** no new request is made to the paid hotel and car source, since the ranked lists are reused.

---

### User Story 3 - Find my plans again (Priority: P2)

Fares closes the app and comes back the next day. "My plans" lists their plans, newest first, each with its
name, dates, total and match. They open one, rename it "Rome with Maya", mark it planned, and delete an old draft.

**Why this priority**: Without it a plan is lost when the app closes. It depends on Story 1 and on sign-in.

**Independent Test**: Build two plans as one traveller, list them, open, rename, change the status and delete one,
and confirm another signed-in traveller can see none of them.

**Acceptance Scenarios**:

1. **Given** a plan was built, **When** Fares opens My plans, **Then** it is listed as a draft with its name, destination, dates, total and match.
2. **Given** a saved plan, **When** Fares opens it, **Then** they see it exactly as saved, with the prices as they were and the time they were checked.
3. **Given** a saved plan, **When** Fares renames it, marks it planned, or deletes it, **Then** My plans shows the change.
4. **Given** another traveller's plan, **When** Fares asks for it, **Then** they are told it doesn't exist.

---

### Edge Cases

- **No departure city**: the plan is built without a flight; the first day starts at 14:00 and the last day ends at 12:00, and a note says a flight can be added by choosing a departure city.
- **No flight found** for the dates: as above, with a note that no recent fare was found.
- **No hotel found** (source down, nothing available): the plan is still built from the destination's centre, with a warning and no hotel in the cost.
- **Places source down or no key**: the plan keeps its hotel, flight and meals it can find, and a warning says the days could not be filled. If there are no places at all, the plan is not built and the traveller is told why.
- **Fewer places than the days need**: the plan uses what there is and leaves free time ("Free afternoon") rather than repeating a place.
- **A one-day trip**: one day that is both arrival and departure day.
- **A trip longer than the forecast**: days beyond it have no weather.
- **A price in a currency other than the trip's**: not added to the total; the item shows its own price and a note says the total leaves it out.
- **A place, event or offer with no price** (most sights): it shows "price not known" and is not counted in the total, which says so.
- **The total is over the trip budget**: the plan is built anyway, with the amount over.
- **A saved plan's offers have changed price or gone**: the saved plan keeps what it saved; opening it does not fetch prices again.
- **Bad input** (dates in the past, more than 30 days, unknown destination, preferences outside 0–100, unknown pace): rejected, naming the field.
- **Too many plans**: a traveller can keep up to 50 plans; building a 51st is refused with a message to delete one.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A signed-in traveller MUST be able to ask for a plan for a destination, dates (1 to 30 days, not in the past), travellers, an optional departure city, the six preferences of spec 007, an optional total budget and a pace (Relaxed, Balanced, Packed; Balanced if left out).
- **FR-002**: The plan MUST start with the best match of the ranked hotel and flight lists of spec 007 for those inputs, and no car. It MUST reuse those stored searches; a search not yet run MUST be run once, as opening the list would.
- **FR-003**: The plan MUST have one day per date. Each day MUST have a title, its date, its weather when the forecast covers it, its total walking distance, and items in time order.
- **FR-004**: Each item MUST have a kind (arrival, departure, sight, nature, restaurant, nightlife, event, free time), a start time, a duration, a name, the walking distance from the previous item (or the hotel), its price when known, and a reference to the place, event or offer it comes from.
- **FR-005**: The days MUST be built by deterministic code:
  - Places come from the destination's existing lists: top attractions (spec of Sprint 1, Wikidata then OpenTripMap), restaurants, and, from OpenTripMap as used in spec 007, nature sites and nightlife places.
  - How many of each kind the trip uses follows the preferences: a higher Culture gives more sights, a higher Nature more parks and gardens, a higher Nightlife an evening place on more days, and Food chooses between the nearest restaurant and a better-known one further away.
  - Places are grouped by area so that each day's places are close to each other, and each day is ordered to keep walking short, starting from the hotel.
  - Lunch and dinner are the restaurants nearest the places before them that have not been used yet.
  - A day starts at 09:00 and ends by 22:00; the first and last days fit the flight times (FR-007).
  - The exact rules and durations are set in the plan's research and can change without changing the API.
- **FR-006**: Pace MUST set the places in a full day: Relaxed 2, Balanced 3, Packed 4. Meals, events and free time are not counted. Arrival and departure days hold fewer, as time allows.
- **FR-007**: With a flight, the first day MUST start no earlier than 1 hour after landing plus the travel time to the hotel, and the last day MUST end 3 hours before the return flight leaves. The travel time between the airport and the hotel MAY be a fixed estimate per destination, stated in the plan.
- **FR-008**: Events of the destination during the trip (Sprint 1, Ticketmaster) MUST be placed on their date, at their time when known, when Culture or Nightlife is not Low. At most one event per day.
- **FR-009**: The plan's match MUST be the weighted average of three parts, each from 0 to 1, shown as a whole percentage with its breakdown:
  - the hotel's match (spec 007), weighted by the sum of all six preferences;
  - the flight's match, weighted by Budget plus Comfort (left out when there is no flight);
  - the days' fit: for each of Culture, Nature, Nightlife and Food, how much of what the pace allows went to that interest compared with what its preference asked for, weighted by those four preferences.
  
  The exact formula is set in the plan's research. A car, when added, MUST count like the flight.
- **FR-010**: The plan MUST show its total: the flight, the hotel, the car, and events with a known price, in the trip's currency. It MUST say which items have no known price, and how far it is over the trip budget when it is.
- **FR-011**: The traveller MUST be able to swap the hotel or flight for another offer of the same ranked list, and add, swap or remove a car. The days, total and match MUST then be rebuilt by the same rules. No swap may call the paid source again.
- **FR-012**: Building a plan MUST save it to the signed-in traveller's account as a draft, named "{Destination}, {n} days" by default. The traveller MUST be able to list their plans (newest first), open one, rename it (1 to 60 characters), set it to draft or planned, and delete it.
- **FR-013**: A saved plan MUST keep a copy of every offer, place and event it uses, with the time prices were checked, so it opens the same later without calling any source.
- **FR-014**: A traveller MUST only see and change their own plans. Another traveller's plan MUST answer as not found.
- **FR-015**: A traveller MUST be able to keep up to 50 plans.
- **FR-016**: Plans, preferences and names MUST NOT be written to logs (constitution: personal data).
- **FR-017**: When one source fails (places, events, weather, flights, hotels), the plan MUST still be built from the others, with a warning naming what is missing, except when no places at all are available (Edge Cases).
- **FR-018**: The rules that build the days and the match MUST live in the Domain layer with no outside dependency, so the assistant (TG-11) can later change a plan through the same rules.

### Key Entities

- **Preferences**: the six of spec 007, plus Pace (Relaxed, Balanced, Packed).
- **Plan (Itinerary)**: owner, name, status (draft, planned), destination, dates, travellers, departure city, preferences, trip budget, the chosen flight, hotel and optional car, its days, total, match with breakdown, warnings, created and updated times.
- **Day**: number, date, title, weather (optional), walking distance, items.
- **Item**: kind, start time, duration, name, walking distance from the previous item, price (optional), a copy of the place, event or offer it refers to.
- **Plan match**: total from 0 to 1, the three parts with their weights and values, and one short reason in fixed wording.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For Rome and 4 other cities, a 5-day plan is built in under 5 seconds when the hotel and flight searches are already stored, and makes no request to the paid source.
- **SC-002**: In those plans, no place appears twice, every day's walking distance is under 12 km, and no item starts before 09:00 or ends after 22:00, apart from flights.
- **SC-003**: With Culture High and Nature Low, the plan holds at least twice as many sights as parks; with the opposite, at least twice as many parks as sights.
- **SC-004**: Relaxed, Balanced and Packed plans for the same trip hold on average 2, 3 and 4 places per full day (± 0.5).
- **SC-005**: Two identical requests give the same plan 100% of the time.
- **SC-006**: A traveller never sees another traveller's plan (checked with two accounts against the running API).
- **SC-007**: A saved plan opens in under 1 second, the same as when it was built.

## Assumptions

- **Scope is the backend API only.** The itinerary and My plans screens in `chocomousse` are a separate spec, after a Sprint 4 Figma page is approved.
- **Sign-in comes first**, as its own spec: Firebase Authentication with email and password, and Google; the app requires sign-in before any screen; the API refuses requests without a valid Firebase sign-in. The owner turns off the Anonymous provider in the Firebase console. This spec only needs the signed-in traveller's id.
- Plans are stored in Firestore (ADR 0017), under the traveller's account.
- Flights are the Travelpayouts fares of Sprint 2 (recent prices, not live). The return leg's landing time is not known, so only the outbound landing time is used.
- Most sights have no ticket price in the free sources, so the total is mainly flight, hotel, car and priced events. Tours and tickets (Figma 10) come later, if a source is chosen.
- Walking distance is a straight line times 1.3 at 4.5 km/h. No routing service is used (skipped for the MVP).
- "Ask the assistant to change this day" (Figma 08) is TG-11 and comes after this spec; it will use the same rules (FR-018).
- Sharing, printing (TG-14) and booking (TG-13) are later sprints.
