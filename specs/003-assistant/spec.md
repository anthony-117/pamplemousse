# Feature Specification: Trip assistant (chat)

**Feature Branch**: `003-assistant`

**Created**: 2026-11-04

**Status**: Approved by the owner on 2026-11-04

**Input**: User description: "Work now on the assistant sprint. I am using Gemini and I already added its API key."

## Context

The assistant is Sprint 5 in the plan (TG-11, "change my plan by chatting"), started early. Sprint 3 (weighted search) and
Sprint 4 (itinerary builder) are not built, so there is **no plan to change yet**. This feature delivers the assistant
itself: a chat that answers trip questions using TravelGenie's own data (overview, places, events, flights, hotels).
Plan-editing tools (`swap_day` …, ADR 0002) are added when the itinerary exists. The approved Figma screens are mobile
09 and web W6 (design v0.2, approved 2026-10-27).

## Clarifications

### Session 2026-11-04

- Q: Which model? → A: Gemini Flash through `Microsoft.Extensions.AI` (ADR 0005), Google AI Studio free tier, with the key the owner already has. The model name is a setting, default `gemini-3.8-flash` (checked on 4 Nov 2026: answers on the free tier; the `gemini-flash-latest` alias returned "high demand").
- Q: Where does the assistant get its facts? → A: Only from TravelGenie's own use cases, called as tools: city search, overview (weather, currency, visa, safety, holidays), places, events, flights and hotels. Prices, visa rules and advisories are never made up by the model.
- Q: Where is the conversation kept? → A: In the app, for the current session only. There are no accounts yet, so the server stores nothing; each message sends the recent conversation. Saved conversations (the list in W6) come with accounts.
- Q: Can it change a plan? → A: Not in this feature: there is no plan yet (Sprint 4).
- Q: MCP server? → A: Not yet (Sprint 6). The tools are called inside the API; the same use cases are exposed over MCP later.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Ask about a destination (Priority: P1)

Fares is looking at Rome for 10–15 April. They open the assistant and ask "Do I need a visa, and what's the weather
like?". The assistant answers in plain words, using TravelGenie's own overview for their passport and dates, and
says where a section has no data.

**Why this priority**: This is the core of the assistant: questions answered from real data, not from the model's memory.

**Independent Test**: Send a question with a destination and dates to the API and check that the reply uses the overview data
(the visa rule and the forecast match what `/overview` returns for the same trip).

**Acceptance Scenarios**:

1. **Given** a trip context (city, dates, passport, currency), **When** Fares asks about visa, weather, currency, safety or holidays, **Then** the reply answers from the overview for that trip.
2. **Given** no trip context, **When** Fares names a city ("Is Lisbon safe in May?"), **Then** the assistant finds the city itself and answers for it.
3. **Given** a city name that matches several cities, **When** the assistant can't tell which one is meant, **Then** it asks which one instead of guessing.
4. **Given** a question the data cannot answer (e.g. opening hours), **When** Fares asks it, **Then** the assistant says TravelGenie doesn't have that information rather than inventing it.

---

### User Story 2 - Ask for flights, hotels, places and events (Priority: P1)

Fares asks "Find me a cheap hotel near the centre" or "What's on that weekend?". The assistant uses the same
searches as the app's screens and gives a short summary, with a card that opens the matching screen with the same
city and dates.

**Why this priority**: Flights and hotels are the trip budget; the assistant has to reach them to be useful, and the
cards lead into screens that already exist.

**Independent Test**: Ask for hotels in Rome for given dates and check the reply mentions hotels that the hotels search returns for
the same trip, and that the response contains a hotels card with that city and those dates.

**Acceptance Scenarios**:

1. **Given** a trip context, **When** Fares asks for hotels, flights (with a departure city), attractions, restaurants or events, **Then** the assistant runs the matching search and summarises the top results with their prices or details.
2. **Given** the assistant used a search, **When** the reply arrives, **Then** it carries one card per search used, naming what it opens (e.g. "Hotels in Rome · 10–15 Apr"), and the card opens that screen with the same city and dates.
3. **Given** a flights question without a departure city, **When** Fares asks it, **Then** the assistant asks where they fly from.
4. **Given** a search whose source is down or has no key, **When** the assistant uses it, **Then** it says that part is unavailable and still answers the rest.

---

### User Story 3 - Chat on phone and web (Priority: P2)

Fares opens the Assistant tab on a phone or on the web. They see the conversation, suggestion chips to start
from, and an input to type in. When they open the assistant from a city, the header shows that city and dates and the
questions are about that trip.

**Why this priority**: The screen is how the feature is used; the API alone is not visible to travellers.

**Independent Test**: Run the app against a fake assistant and check the screen at 390 px and desktop width: empty state with
suggestions, sending a message, the typing state, the reply with a card, and an error with Try again.

**Acceptance Scenarios**:

1. **Given** an empty conversation, **When** Fares opens the Assistant, **Then** they see a greeting and suggestion chips; tapping one sends it.
2. **Given** Fares sent a message, **When** the reply is on its way, **Then** their message shows at once and a typing indicator shows until the reply arrives.
3. **Given** a reply failed, **When** it fails, **Then** the conversation stays, the failed message is marked, and Try again resends it.
4. **Given** Fares is on a city's overview, **When** they open the assistant from there, **Then** the header shows the city and dates and the conversation uses that trip.
5. **Given** Fares leaves the Assistant tab and comes back in the same session, **When** they return, **Then** the conversation is still there. "New chat" clears it.

---

### Edge Cases

- **No Gemini key**: the API still starts; the assistant replies with a warning naming the missing variable, and the app shows it in the conversation.
- **Gemini busy or over its free limit** (503 or 429): the reply is a warning ("The assistant is busy, try again in a minute"), not a server error. No automatic retry.
- **Gemini slow**: each call to Gemini has 30 s and a whole answer 50 s, tool calls included; past either, the same busy warning.
- **Too long**: a message over 1,000 characters is refused (400). Only the last 20 messages of the conversation are sent.
- **Endless tool use**: at most 6 rounds of tool calls per reply; past that the model must answer with what it has.
- **Model asks for an unknown tool or bad arguments** (e.g. an unknown city id): the tool returns an error message to the model, which then explains or asks; the request does not fail.
- **Hotels quota**: hotel searches from the assistant go through the same search, cache and monthly quota as the hotels screen (ADR 0017), so a chat cannot spend more than the screen could.
- **Off-topic questions**: the assistant answers briefly and steers back to travel; it does not refuse ordinary small talk.
- **Prompt injection in data** (e.g. an event description saying "ignore your instructions"): tool results are data to the model; the instructions say so, and the assistant cannot do anything beyond read-only searches.
- **Language**: the assistant replies in the language of the question.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The API MUST offer one endpoint that takes the recent conversation (up to 20 messages, each a role and text) and an optional trip context (destination id, dates, adults, passport, currency, departure city), and returns the assistant's reply.
- **FR-002**: The reply MUST contain the assistant's text, zero or more cards, and warnings. Each card MUST name a screen (overview, places, events, flights, hotels), its city id, its title and the trip values it uses, so the app can open it.
- **FR-003**: Cards MUST be built by the server from the searches the assistant actually ran, never written by the model.
- **FR-004**: The assistant MUST be able to use these read-only tools, each calling the existing use case: find a city; get a city's overview for a passport, currency and dates; list attractions or restaurants; list events for dates; search flights from a departure city; search hotels for dates and adults.
- **FR-005**: The assistant MUST NOT state prices, visa rules, safety levels or availability that did not come from a tool in the same reply.
- **FR-006**: When the trip context is given, the assistant MUST use it as the default for tool calls; values in the message ("for 3 adults") override it for that question.
- **FR-007**: A missing Gemini key MUST produce a warning naming `GEMINI_API_KEY`, never a crash or a failed startup.
- **FR-008**: Gemini errors, rate limits and time-outs MUST produce a warning in a normal reply, never a 5xx.
- **FR-009**: The model name MUST be a setting (`GEMINI_MODEL`), so it can change without a code change.
- **FR-010**: The server MUST NOT store or log conversation text (constitution: personal data, chat history).
- **FR-011**: The app MUST show the Assistant tab as in Figma 09 (phone) and W6 (web): header, messages (user right, assistant left), cards in the ActionCard style, suggestion chips, input with send. The web conversation list in W6 is left out until accounts exist.
- **FR-012**: The app MUST keep the conversation for the session, offer "New chat", and offer the assistant from a city's overview with that trip as context.
- **FR-013**: The app MUST show a typing state while waiting and, on failure, keep the conversation and offer Try again (chocomousse principle V).
- **FR-014**: The app MUST show a short notice that messages are processed by Google Gemini and should not contain personal details (ADR 0005: the free tier may use prompts for training).

### Key Entities

- **Chat message**: role (traveller or assistant) and text. Lives only in the app.
- **Trip context**: optional destination id, dates, adults, passport, currency, departure city; the same values the screens already use.
- **Assistant reply**: text, cards, warnings.
- **Card**: a link into an existing screen: kind (overview, places, events, flights, hotels), destination id, title, subtitle, and the trip values to open it with.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For 10 scripted questions about Rome, Paris and Beirut (visa, weather, currency, safety, attractions, restaurants, events, flights, hotels), at least 9 replies use the matching tool and agree with what the screen shows for the same trip.
- **SC-002**: In those 10 replies, no price, visa rule or advisory level appears that the tools did not return.
- **SC-003**: Every search the assistant runs comes back as a card that opens the right screen with the same city and dates (10 of 10).
- **SC-004**: A reply that needs no hotel search arrives in under 8 s, and one with a cached hotel search in under 12 s, on the free tier.
- **SC-005**: Without a Gemini key, or with Gemini returning 503/429, 100% of requests return a reply with a warning and no 5xx.
- **SC-006**: The Assistant screen works at 390 px and at desktop width, with its empty, typing, reply-with-card and error states.

## Assumptions

- No accounts yet, so no saved conversations and no profile defaults; passport and currency come from the app's current choices (defaults LB and USD).
- English is the main language; other languages work as far as Gemini handles them.
- No streaming: the reply arrives whole, behind a typing indicator. Streaming can come later if replies feel slow.
- The Gemini free tier's limits are enough for development and the demo; anyone with the web app can use them up, which is accepted until accounts exist.
- The free tier may use prompts for training (ADR 0005); the notice in FR-014 covers this until a paid tier is chosen.
- Plan editing, saved conversations, the MCP server and RAG are later sprints (4, 5, 6, 7).
