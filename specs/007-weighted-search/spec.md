# Feature Specification: Weighted search — rank a trip's options by what matters to the traveller

**Feature Branch**: `007-weighted-search`

**Created**: 2026-11-05

**Status**: Approved by the owner on 2026-11-05. Design: Figma v0.2 screens 06 "Plan setup & preferences" and 07 "Ranked results" (approved 2026-10-27); a "Sprint 3 — Weighted search" page fits them to real data before the plan.

**Input**: User description: "TG-09: As Fares, I want to weight budget, culture, food and style and get ranked options."

## Clarifications

### Session 2026-11-05

- Q: What is ranked? → A: The flights, hotels and rental cars **inside one destination**, each kind in its own list (Figma 07: tabs Flights, Hotels, Cars). Destinations are not ranked against each other. Putting one flight, one hotel and one car together into a trip is the itinerary (TG-10, Sprint 4).
- Q: Which preferences? → A: The six on Figma 06: Budget, Culture & history, Food, Nature, Nightlife, Comfort. The domain model's seventh, Pace, is about how full each day is, so it waits for the itinerary (TG-10).
- Q: What scale? → A: Each preference is a number from 0 to 100, as in the domain model and the `weights` column planned for saved trips. The app's Low, Medium and High send 20, 50 and 90. 0 means "doesn't matter". Leaving a preference out means 50.
- Q: Which preference applies to which kind? → A: Budget and Comfort apply to all three. Culture, Food, Nature and Nightlife are about where you stay, so they apply to hotels only, measured by what is within walking distance of each hotel. A flight's or car's match uses only Budget and Comfort.
- Q: Food quality isn't known (ADR 0008: OpenTripMap has no ratings or prices for restaurants). → A: Food is measured by how many restaurants and cafés are near the hotel, from the free OpenTripMap data already used. No paid source (Google Places and others need the owner's approval and are not needed for this).
- Q: Who ranks? → A: Deterministic C# code (ADR 0002). The same offers and preferences always give the same order, scores and reasons. The LLM never ranks or writes the reasons.
- Q: Where are the preferences kept? → A: Sent with each request; nothing is stored on the server. Saving them with a trip comes with accounts and saved trips.
- Q: Does ranking cost extra requests to the paid hotel and car source? → A: No. Ranking runs on the same stored search results as the existing lists. Ranking a search that was never run runs it once, exactly as opening the list would.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Rank hotels by my preferences (Priority: P1)

Fares is planning 5 days in Rome. Budget and culture matter a lot to them, food somewhat, nightlife
little. They set those and see Rome's hotels best match first, each with a % match and a short
reason such as "Cheap, near 9 sights".

**Why this priority**: Hotels are where all six preferences meet, and they are the biggest part of
most trip budgets. This alone delivers the backlog story.

**Independent Test**: Request Rome's hotels for valid dates with preferences through the API, and
confirm each hotel has a match from 0 to 100%, a breakdown and a reason, and that changing the
preferences changes the order as the breakdown explains.

**Acceptance Scenarios**:

1. **Given** a destination, dates and preferences, **When** Fares asks for hotels ranked by match, **Then** the hotels come best match first, each with its match as a whole percentage.
2. **Given** a ranked hotel, **When** Fares reads it, **Then** they see how it scored on each preference that counted (0–100% each), and one short reason built from its strongest points.
3. **Given** Culture is High and everything else Low, **When** Fares compares two hotels of similar price and reviews, **Then** the one with more sights within walking distance ranks higher.
4. **Given** Budget is High and everything else Low, **Then** the order is close to cheapest first.
5. **Given** every preference is 0, **When** Fares asks, **Then** the hotels come in the source's recommended order and no match is shown, with a note that no preference was set.
6. **Given** a total budget for the trip, **When** a hotel's price for the stay is more than that whole budget, **Then** it is marked "over your budget" and ranked after every hotel that is not.
7. **Given** the same search was answered before, **When** Fares changes only the preferences or the total budget, **Then** the new order comes back without any call to the paid source.

---

### User Story 2 - Rank flights and rental cars too (Priority: P2)

Fares also wants the flight and the car that suit them best. Budget and Comfort rank those lists
the same way, with a match and a reason such as "Direct, short" or "Cheapest option".

**Why this priority**: It completes Figma 07's three tabs. It depends on the scoring of Story 1.

**Independent Test**: Request flights and cars for Rome with preferences, and confirm both are
ranked by match with a breakdown and reason, and that a High Comfort lifts direct flights and
larger, better-rated cars.

**Acceptance Scenarios**:

1. **Given** a departure city, a destination, dates and preferences, **When** Fares asks for flights ranked by match, **Then** they come best match first, scored on price (Budget) and stops, duration and how close the dates are to the ones asked (Comfort).
2. **Given** a destination, dates and preferences, **When** Fares asks for cars ranked by match, **Then** they come best match first, scored on price (Budget) and size class, company rating, automatic gearbox and free cancellation (Comfort).
3. **Given** a flight or car, **When** Fares reads its breakdown, **Then** only Budget and Comfort appear, and Culture, Food, Nature and Nightlife are not counted for it.
4. **Given** Budget and Comfort are both 0 but other preferences are set, **When** Fares asks for flights or cars by match, **Then** they come in the usual order with a note that none of the set preferences apply to this kind.

---

### User Story 3 - Filter, sort and page as before (Priority: P3)

Ranking is one more way to order each list. Every existing filter, page and the Booking.com links
still work.

**Why this priority**: Keeps everything the lists already do. It depends on Stories 1 and 2.

**Independent Test**: Ask for hotels ranked by match and filtered to 4 stars and up, then page 2, and
confirm every hotel matches the filter, the order continues across pages, and no call to the paid
source was made.

**Acceptance Scenarios**:

1. **Given** filters and ranking by match, **When** Fares asks, **Then** only matching offers are ranked, and the match is computed against those offers.
2. **Given** a ranked list, **When** Fares asks for the next page, **Then** it continues the same order.
3. **Given** any other sort (price, rating, recommended), **When** preferences are sent too, **Then** each offer still carries its match, but the order is the sort asked for.

---

### Edge Cases

- **A field a preference needs is missing** (a hotel with no reviews, a flight with no duration, a car with no rating): that part of the score counts as average (50%) for that offer, so it is neither rewarded nor punished, and the breakdown says it is unknown.
- **All offers share the same value** (same price, all direct): that part counts as average (50%) for all of them, so it doesn't change the order or inflate the match.
- **One offer only**: the parts compared within the list (price, nearby counts) count as average, as above; the parts measured on a fixed scale (review score, stars, stops, company rating) still count.
- **Equal matches**: ordered by the source's recommended order, so the result is stable.
- **No places data near a hotel** (OpenTripMap down or no key): Culture, Food, Nature and Nightlife count as average for every hotel, and the response carries a warning that they could not be measured. The ranking still works on Budget and Comfort.
- **A preference outside 0–100, or an unknown preference name**: rejected, naming the field.
- **A total budget of zero or less, or in a bad format**: rejected, naming the field. The total budget is in the request's currency.
- **No offers** (source down, nothing found, filters match nothing): the same empty outcome as each list today.
- **Prices in the saved results come from an older search** (source down): they are ranked as they are, with the time they were fetched, as today.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The hotel, flight and car lists MUST accept a new sort, "match", and six optional preferences: budget, culture, food, nature, nightlife, comfort, each a whole number from 0 to 100. A missing preference MUST count as 50.
- **FR-002**: The lists MUST accept an optional total budget for the trip, in the request's currency.
- **FR-003**: Each preference MUST be measured on a scale from 0 to 1 per offer as follows:
  - **Budget** (all kinds): the total price for the stay, flight or rental. Cheapest in the list is 1, most expensive is 0, linear in between.
  - **Comfort, hotels**: the review score out of 10, and the star rating.
  - **Comfort, flights**: fewer stops (both legs), a shorter journey, and dates closer to the ones asked (exact, then within 3 days, then same month).
  - **Comfort, cars**: a larger size class, the company's rating, an automatic gearbox and free cancellation.
  - **Culture** (hotels): how many of the destination's top attractions are within 1.5 km.
  - **Food** (hotels): how many restaurants and cafés are within 1 km.
  - **Nature** (hotels): how many parks, gardens and natural sites are within 2 km.
  - **Nightlife** (hotels): how many bars, pubs and night clubs are within 1 km.
  
  Price and the nearby counts MUST be compared within the list: the best in the list gets 1 and the worst 0 (a hotel with no places nearby always gets 0). Review score, stars, stops, journey time and company rating MUST be measured on a fixed scale (e.g. review score out of 10), so one offer's measure doesn't depend on the others.
- **FR-004**: An offer's match MUST be the weighted average of the preferences that apply to its kind: each measure is multiplied by its preference, the results are added, and the total is divided by the sum of those preferences. It MUST be shown as a whole percentage from 0 to 100.
- **FR-005**: Each offer MUST carry its match, a breakdown (each preference that counted: its weight and the offer's measure, as a percentage) and one short reason in plain English, built from its two strongest contributions. The wording MUST come from a fixed list in code, not from an LLM.
- **FR-006**: With sort "match", offers MUST be ordered by match (highest first), then by the source's recommended order. Offers over the total budget MUST come after all others.
- **FR-007**: Ranking MUST run on the stored results of the search, like the existing sorts and filters. Changing preferences or the total budget MUST NOT call the paid hotel and car source.
- **FR-008**: The places near each hotel MUST come from data that is free and stored per destination (OpenTripMap, ADR 0008), fetched at most once a day per destination and kind, and MUST NOT need a request per hotel.
- **FR-009**: The same offers, preferences and budget MUST always give the same order, matches, breakdowns and reasons.
- **FR-010**: When all preferences that apply to a kind are 0, the list MUST come in its usual order with no match, and a note saying so.
- **FR-011**: If the places data cannot be fetched, hotels MUST still be ranked, with the four place-based preferences counted as average and a warning.
- **FR-012**: Invalid preferences or total budget MUST return a clear error naming the field.
- **FR-013**: Preferences MUST NOT be stored or logged with anything that identifies a person.
- **FR-014**: The scoring rules MUST live in the Domain layer with no outside dependency (ADR 0012), so the assistant and the itinerary (TG-10) can reuse them.

### Key Entities

- **Preferences (Weights)**: six numbers from 0 to 100: budget, culture, food, nature, nightlife, comfort. Pace (domain model) comes with TG-10.
- **Match (Score)**: for one offer: the total from 0 to 1 (shown as %), the breakdown per preference, the reason, and whether it is over the total budget.
- **Nearby counts**: for one hotel: the number of top attractions, restaurants, nature sites and nightlife places within each distance.
- **Places by kind** (per destination): the destination's restaurants, nature sites and nightlife places, with their positions, stored for a day.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For Rome and 4 other cities, with Budget High and the rest Low, at least 8 of the top 10 hotels are among the 10 cheapest.
- **SC-002**: For the same cities, with Culture High and the rest Low, the top 5 hotels have more sights within 1.5 km on average than the bottom 5.
- **SC-003**: With Comfort High and Budget Low, direct flights rank above flights with stops whenever both exist.
- **SC-004**: Re-ranking a list already searched (new preferences or budget) returns in under 1 second and makes no call to the paid source.
- **SC-005**: Two identical requests return the same order, matches and reasons, 100% of the time.
- **SC-006**: Every ranked offer has a match from 0 to 100, a breakdown with only the preferences that apply to its kind, and a reason.

## Assumptions

- **Scope is the backend API only.** The plan setup and ranked results screens in `chocomousse` are a separate spec, with the Figma page approved first.
- The lists already exist: flights (`/flights`, Travelpayouts, free), hotels and cars (`/hotels`, `/cars`, Booking COM on RapidAPI, 530 requests a month shared). A hotel search keeps 20 hotels by default (`Hotels:PagesPerSearch`), so a hotel list ranks about 20 hotels.
- Flights need a departure city, as today. The ranked flights use the one the traveller already chose.
- "Top attractions" are the destination's attraction list already shown in the app (Wikidata, then OpenTripMap). Restaurants, nature sites and nightlife places come from OpenTripMap with the free key already configured. Which OpenTripMap kinds count for each is set in the plan's research.
- The distances (1 km, 1.5 km, 2 km) are walking distances in a straight line. They are a first choice to be checked against real cities during the build, and can change without changing the API.
- No user account is needed, as with the rest of the current API.
- Pace, putting offers together into one trip, and the trip's overall match ("Rome Complete: 96%") are TG-10.
