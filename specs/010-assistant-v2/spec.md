# Feature Specification: Assistant v2 — every feature through the assistant

**Feature Branch**: `010-assistant-v2`

**Created**: 2026-11-10

**Status**: Approved by the owner on 2026-11-10

**Input**: User description: "Assistant v2 (backend for chocomousse spec 008, assistant-first shell, design v1.0
approved 2026-11-10). Every feature reachable through the assistant. (1) Structured card payloads in the reply: plan,
flight, hotel, car, place, event, travel facts, with match % where ranked and lat/lng on every card. (2) Ranked search
tools for flights, hotels and cars (spec 007 weights), including a new search_cars. (3) Plan tools: build, list, get,
update (rename, status), swap hotel/flight/car, remove car (spec 008 routes). (4) A NEW day-edit route plus tools:
alternatives for a day item; replace, add or remove an item, with times rebuilt. (5) Preferences tools: get and
update the six preferences and pace. (6) A saved-items store, routes and tool for hotels, places and events only. (7)
Tool steps streamed live as each tool finishes (the answer text itself is not streamed), and 0-3 next-step
suggestions. (8) A server chat store per account: create, list, get, rename, delete, search; the reply works from a
chat id. Chats are deleted 90 days after their last message and chat text is never logged; reverses spec 003 FR-010
and ADR 0018. (9) Per-account assistant rate limits, and the hotels quota under many chats."

## Context

On 2026-11-10 the owner approved design v1.0: TravelGenie is an AI assistant first, modelled on the Claude desktop
app, and **every feature must be reachable through the assistant**: flights, hotels, cars, places, events, travel info,
preferences, building and changing plans, and saved items. The app side is chocomousse spec 008
(`specs/008-assistant-first-shell`, approved 2026-11-10, side-panel frames approved the same day); its "Backend
dependencies" table is the list this spec answers. Under the constitution's two-repo rule this spec is approved before
that spec's plan.

Today (spec 003, ADR 0018) the assistant is a single stateless endpoint: the app sends the last 20 messages, the
server answers in one piece with text and link cards, it has six read-only tools, nothing is ranked, it cannot search
cars or touch a plan, and it stores nothing. Since then the ranked lists (spec 007), the itinerary (spec 008) and
accounts (spec 009) have been built, but only the screens use them.

## Clarifications

### Session 2026-11-10

- Q: Does the assistant change a plan or the preferences at once, or confirm first? → A: At once when the request is clear; it asks only when the request is unclear (FR-015).
- Q: Per-account limits? → A: 10 messages per minute and 100 per day per account, 15 per minute for all accounts together; settings (FR-031).
- Q: A per-account cap on uncached hotel searches? → A: No; the shared cache and monthly quota only (FR-032).
- Q: How are the changes to ADR 0018 recorded? → A: A new ADR 0021, written with the plan; ADR 0018 marked partly superseded (FR-030).
- Q: The approved Trip pane has "Rebuild plan"; is there a route for it? → A: Add a rebuild route and a `rebuild_plan` tool that rebuild a plan in place (FR-012a).
- Q: Can the assistant save items itself? → A: Yes: `save_item` and `list_saved`; removing stays an app action (FR-020).
- Q: Caps on chat storage? → A: 200 messages per chat and 500 chats per account, both settings (FR-022a).
- Q: Which model? The owner asked to switch to Groq's free `openai/gpt-oss-20b`. → A: The provider becomes a setting (Gemini or OpenAI-compatible such as Groq); the default is chosen after measuring tokens per message on both, recorded in ADR 0021 (FR-034, FR-035).
- Q: Should a rebuild keep the hotel and flight the traveller swapped in, and the day edits, or choose the best again? (The live check, 2026-11-11: a rebuild undid a hotel swap and lost a day edit.) → A: Keep the traveller's choices and edits; take the best only for what they left as built or what is no longer offered, with a note (FR-012a, research R-5).

### Owner decisions already taken (2026-11-10, chocomousse spec 008)

- Tool steps are sent live, one as each tool finishes; the answer text is not streamed word by word.
- Chats are kept on the server per account and deleted 90 days after their last message; chat text is never logged.
- Saved covers hotels, places and events only; flights and cars are kept in plans.
- Changing a plan by chat covers swapping the hotel, flight or car, removing the car, and editing a day's items
  (replace, add or remove a place, restaurant or event).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Plan a trip in one message (Priority: P1)

Fares writes "Plan 5 days in Rome for two, from Beirut, under $1,200" in a new chat. While the assistant works, the
app receives each step as it finishes ("Searched flights Beirut → Rome · 23 fares", "Searched hotels in Rome · 48
found, ranked for you", "Built your plan · 91 % match"). Then the answer arrives: a few sentences, a plan card, the
best flight and the best-matching hotels as cards with their match, and up to three next steps ("Add a car", "Make day
3 more relaxed"). The plan is saved as a draft in Fares's plans, exactly as `POST /plans` would have built it.

**Why this priority**: This is the product of design v1.0: the assistant does what the v0.2 screens made the traveller do.

**Independent Test**: With a scripted chat model that calls `build_plan`, send the message through the API; check the
steps arrive one by one before the answer, the plan exists in `GET /plans`, and the reply's plan, flight and hotel
cards carry the same ids, prices and match as the plan and the ranked lists.

**Acceptance Scenarios**:

1. **Given** a signed-in traveller and a new chat, **When** they ask for a plan with a city, dates, adults and a budget, **Then** the assistant builds it with their saved preferences and pace (or the ones given in the message or the chat's trip), and the plan is saved as a draft.
2. **Given** the assistant is working, **When** each tool finishes, **Then** the app receives a step with the tool, a one-line summary, whether it worked, and the cards it produced, before the next step starts being sent.
3. **Given** the answer is complete, **Then** the app receives the text, the cards grouped by kind, 0–3 next-step suggestions and any warnings, in one final event.
4. **Given** a plan card, **Then** it carries the plan's id and the summary that `GET /plans` returns for it (name, status, dates, total, currency, match, photo).
5. **Given** a value the plan needs is missing (no dates, no city), **Then** the assistant asks for it instead of guessing, and no plan is built.

---

### User Story 2 - Ask for any one thing and get real cards (Priority: P1)

Fares asks "Cheap flights from Beirut in May", "Hotels near the centre", "Rent a car in Lisbon", "Top sights in Paris",
"What's on this weekend" or "Visa and weather for Istanbul". Each answer carries the items themselves, not links: the
app draws a flight row, hotel and place photo cards, a car row, event cards or travel facts from the reply alone.

**Why this priority**: The owner's rule: every feature must be reachable through the assistant.

**Independent Test**: For each of the seven card kinds, a scripted model calls the matching tool; check the reply's
cards hold the same items, fields, order and match as the list endpoint returns for the same search and preferences.

**Acceptance Scenarios**:

1. **Given** a flights, hotels or cars question, **Then** the search is ranked with the traveller's preferences and trip budget (spec 007), and each card carries its match (percent, reason, parts).
2. **Given** a cars question ("Rent a car in Lisbon"), **Then** the assistant searches cars for the trip's dates and the city centre (or the airport or place named) and answers with car cards.
3. **Given** a places question, **Then** the assistant can list sights, restaurants, nature or nightlife, and each card carries the place as the places list returns it.
4. **Given** an events question, **Then** each card carries the event: name, date and time, venue, price when known, link, photo.
5. **Given** a travel-info question, **Then** the reply carries one travel-facts card with weather, visa, currency, safety and holidays as the overview returns them, each part empty when its source had nothing.
6. **Given** any card for a hotel, place, event, car pick-up or plan, **Then** it carries a latitude and longitude; when an item has no position of its own, it carries the city's centre and says so.
7. **Given** a ranked search, **Then** the cards hold at most 6 items of a kind, best first, with the total found, so the app can offer "See all N" with the same search.
8. **Given** an item with no match computed, **Then** its match is empty, never 0.

---

### User Story 3 - Change the plan by chatting (Priority: P2)

From the plan Fares asks "pick a cheaper hotel", "add a car", "drop the car", "swap the Palatine for something
outdoors", "add a gelato stop on day 2" or "rename it Rome with Maya". The assistant makes the change through the same
rules as the itinerary screens, and the reply carries the updated plan card.

**Why this priority**: Building is P1; changing is what makes the plan the traveller's own (TG-11).

**Independent Test**: For each change (swap hotel, flight, car; remove car; replace, add, remove a day item; rename;
mark as planned), a scripted model calls the tool; check `GET /plans/{id}` afterwards equals what the matching route
gives for the same change, and the reply carries the new plan card.

**Acceptance Scenarios**:

1. **Given** a plan, **When** the traveller asks for another hotel, flight or car, **Then** the assistant offers the plan's kept alternatives (ranked) and swaps in the one chosen; the days, total and match are rebuilt.
2. **Given** a plan with a car, **When** they ask to remove it, **Then** the car is removed and the total rebuilt.
3. **Given** a day item, **When** they ask for alternatives, **Then** the assistant answers with up to 6 places or events that fit that slot (same kind or the kind asked for, near the day's other items, ranked by the traveller's preferences, not already in the plan) as cards.
4. **Given** a day, **When** they ask to replace, add or remove an item, **Then** the day is changed and its times, walking distances and the plan's total and match are rebuilt by the itinerary's rules, never written by the model.
5. **Given** a rename or "mark as planned", **Then** the plan is updated as `PATCH /plans/{id}` does.
6. **Given** a change that is not possible (an offer no longer among the alternatives, a day that does not exist, a full day), **Then** the plan is unchanged and the assistant says why.
7. **Given** the app's own day-edit controls (the swap icon and "Ask to change this day…"), **Then** the same day edits are also available as routes, so the panel and the assistant change a plan the same way.

---

### User Story 4 - Come back to a chat on any device (Priority: P2)

Fares planned Rome on their laptop. The next day on their phone, Recents lists "Rome in April for two"; opening it
returns the whole conversation with its steps and cards, and they carry on. Searching "jazz" finds it.

**Why this priority**: The v1.0 sidebar is built around past chats, and the owner chose to keep them on the server.

**Independent Test**: Through the API, create a chat, send two messages, list chats, get the chat, search a word from
it, rename it, delete it; check another account never sees it, and a chat whose last message is 90 days old is gone.

**Acceptance Scenarios**:

1. **Given** a signed-in traveller, **When** they send the first message of a new chat, **Then** the chat is created for their account and gets a short title from the first exchange.
2. **Given** past chats, **Then** the list returns them latest first, with title, last message time and the trip.
3. **Given** a chat, **When** it is opened, **Then** its messages come back as they were sent: text, steps, cards, suggestions and warnings.
4. **Given** a new message in an existing chat, **Then** the app sends only the chat id, the message and the current trip and preferences; the server answers with that chat's history.
5. **Given** a search word, **Then** chats whose title or messages contain it come back, latest first.
6. **Given** rename or delete, **Then** the chat is renamed, or deleted with all its messages and no longer listed or found.
7. **Given** a chat with no new message for 90 days, **Then** it is deleted with its messages; plans built in it stay.
8. **Given** another traveller's chat id, **Then** the API answers as if it did not exist.

---

### User Story 5 - Preferences by chat (Priority: P2)

Fares says "I care more about food than museums" or "make my trips more relaxed". The assistant reads their saved
preferences, changes the ones asked, and says what changed.

**Why this priority**: Preferences drive every ranking; the owner listed them among the features the assistant must reach.

**Independent Test**: A scripted model calls `update_preferences`; check `GET /me` returns the new values and the
other fields are unchanged.

**Acceptance Scenarios**:

1. **Given** a question about their preferences, **Then** the assistant answers from the saved six preferences and pace.
2. **Given** a request to change them, **Then** only the named preferences or the pace change, within 0–100 and the three paces, and the reply says the old and new values.
3. **Given** a change of preferences, **Then** existing plans are not rebuilt by it; the assistant may offer to rebuild one.

---

### User Story 6 - Save what I like (Priority: P3)

Fares bookmarks a hotel, a place and an event. Saved lists them by kind, and "what did I save in Rome?" is answered
from the same list.

**Why this priority**: In the approved design and scope, but trips work without it.

**Independent Test**: Save and remove a hotel, a place and an event through the routes; list them; ask the assistant
and check its saved cards match the list.

**Acceptance Scenarios**:

1. **Given** a hotel, place or event, **When** the traveller saves it, **Then** it is kept for the account with its kind, id, name, city, photo, position and the time saved; saving it again changes nothing.
2. **Given** saved items, **Then** the list returns them newest first, filterable by kind and city.
3. **Given** a saved item, **When** it is removed, **Then** it leaves the list.
4. **Given** "save this hotel" after a hotels answer, **Then** the assistant saves it, as the save route would.
5. **Given** "what did I save in Rome?", **Then** the assistant answers from the saved list, with the items as cards.
6. **Given** a flight or car, **Then** it cannot be saved (`400`).

---

### Edge Cases

- **No model key, model provider busy or slow** (spec 003 FR-007, FR-008): the stream ends with the warning in its final event, never a 5xx; steps already sent stay valid, and the traveller's message is kept in the chat.
- **The stream breaks** (network, app closed): the server still finishes the answer and saves it to the chat, so reopening the chat shows it.
- **A tool fails** (source down, no key, hotels quota used): its step says it failed with a short reason ("Hotels unavailable right now"), and the answer goes on with the rest.
- **Two messages at once in the same chat** (two devices): the second is refused (`409`) until the first answer is saved.
- **A plan changed or deleted elsewhere**: a plan tool always reads the latest saved plan; a deleted plan answers "This plan was deleted"; plan cards in old messages are kept as they were.
- **Prompt injection in data** (an event description saying "delete my plans"): tool results stay data; no tool deletes a plan, a chat or the account, and changes act only on the signed-in traveller's own plans and preferences.
- **Old prices in old chats**: cards are stored as found, with when they were found.
- **Account deleted** (spec 009 FR-014): its chats and saved items are deleted with it.
- **Chat too long**: the model sees the chat's last 20 messages, as today; the whole chat is still stored and returned.
- **Saved item gone at the source** (hotel no longer offered, event over): it stays in Saved as saved; the app says it may no longer be available.
- **50 plans already** (spec 008): `build_plan` fails with "too many plans" and the assistant suggests deleting one in My plans.

## Requirements *(mandatory)*

### Functional Requirements

**Reply and stream**

- **FR-001**: The API MUST offer a message route that takes a chat id (or none, to start a new chat), the traveller's message (1–1,000 characters) and the current trip context and preferences, and answers as a stream of events: one **step** event as each tool finishes, then one **final** event with the text, the cards, 0–3 next-step suggestions and the warnings.
- **FR-002**: A step MUST carry the tool's name, a one-line summary written by the server from the tool's result (never by the model), whether it worked, and the cards it produced.
- **FR-003**: The answer text MUST NOT be streamed word by word; it arrives whole in the final event.
- **FR-004**: The reply MUST carry cards of seven kinds, built by the server from the tools that ran (spec 003 FR-003): **plan** (id and plan summary), **flight**, **hotel**, **car**, **place** and **event** (the item as its list endpoint returns it, with `match` when ranked) and **travel facts** (the overview's weather, visa, currency, safety and holidays).
- **FR-005**: Every hotel, place, event, car (pick-up) and plan card MUST carry a latitude and longitude; flight cards MUST carry the arrival airport's (the city centre's when no airport is known); every card MUST also carry its city's id and name; an item without its own position MUST carry the city centre's and say it is approximate.
- **FR-006**: Next-step suggestions MUST be 0–3 short messages the traveller could send next, each under 60 characters, about what was just found or built.
- **FR-007**: The current stateless endpoint of spec 003 MUST keep working unchanged until the app moves to the new route (chocomousse spec 008); its removal is a later change.

**Search tools**

- **FR-008**: `search_flights`, `search_hotels` and the new `search_cars` MUST rank by the traveller's preferences and trip budget (spec 007) by default, and MAY sort another way when the traveller asks (cheapest, closest…), still with the match shown.
- **FR-009**: `search_cars` MUST search the car list of spec 004 for the trip's dates, at the city centre by default or at an airport or place the traveller names.
- **FR-010**: `list_places` MUST accept sights, restaurants, nature and nightlife (spec 008's categories).
- **FR-011**: Search tools MUST go through the same use cases, caches and quotas as the screens (spec 003 edge cases).

**Plan tools**

- **FR-012**: The assistant MUST have plan tools that call the itinerary's own use cases: `build_plan`, `list_plans`, `get_plan`, `update_plan` (rename, status), `list_plan_alternatives` (hotels, flights, cars), `swap_plan_offer` (hotel, flight, car) and `remove_plan_car`. Results MUST equal the matching spec 008 routes.
- **FR-012b**: The plan list items and plan cards MUST carry the plan's city id, adults, trip budget, pace and the city centre's position, and a plan MUST list the traveller's day edits.
- **FR-012a**: The API MUST offer a route that rebuilds an existing plan in place with new dates, adults, departure city, trip budget, preferences or pace (the Trip pane's "Rebuild plan"), also used as the `rebuild_plan` tool. The plan keeps its id, name, status and day edits, and the traveller's choices: a hotel or flight they swapped in stays while the new offers still include it (for new dates, the same flight number), and a car stays while the dates are the same (its prices depend on them). Offers the traveller left as built, and a choice that is no longer offered, are chosen again as `POST /plans` chooses them, with a note for a lost choice. The days, total and match are built again. A rebuild that fails leaves the plan unchanged.
- **FR-013**: Days, times, walking distances, totals and matches MUST be built by the itinerary's deterministic rules (ADR 0002), never by the model.
- **FR-014**: The assistant MUST NOT delete plans, chats, saved items or the account; those stay app actions.
- **FR-015**: The assistant MUST make a change to a plan, the preferences or the saved items as soon as the traveller clearly asks for it, without a confirmation step, and the reply MUST say what changed. When the request is unclear (which plan, which day, which hotel), it MUST ask first and change nothing.

**Day edits**

- **FR-016**: The API MUST offer day-edit routes for a plan, also used as tools: list alternatives for a day item (`suggest_day_alternatives`), and replace, add or remove a day item (`change_day_item`). A change MUST rebuild that day's order, times and walking distances and the plan's total and match, and return the plan. A removed item's time becomes free time and stays free through later rebuilds.
- **FR-017**: Alternatives MUST come from the places and events already known for the city (spec 008's kept lists, and events for that date), near the day's other items, ranked by the plan's preferences, excluding items already in the plan; at most 6.
- **FR-018**: An added item MUST fit in the day's pace; when the day is full the change MUST be refused with a reason, or MUST replace the item the traveller names.

**Preferences**

- **FR-019**: `get_preferences` and `update_preferences` MUST read and change the six preferences and pace of the traveller's profile (spec 009, spec 008), changing only the values named.

**Saved items**

- **FR-020**: The API MUST store saved items per account (hotels, places and events only) with routes to save, remove and list them (by kind and city, newest first), and the assistant MUST have `save_item` and `list_saved` tools (removing stays an app action, FR-014). Saving the same item twice MUST keep one.
- **FR-021**: A saved item MUST keep what the app needs to show it without another search: kind, id, name, city, photo, position, and the time saved.

**Chats**

- **FR-022**: The API MUST store chats per account, with routes to list (latest first), get, rename, delete and search them; a chat is created by its first message. Search matches whole words, not word starts.
- **FR-022a**: A chat MUST hold at most 200 messages; past that the route answers `409` with "This chat is full. Start a new chat to carry on." An account MUST hold at most 500 chats; starting one more deletes the chat with the oldest last message. Both are settings.
- **FR-022b**: The chat search route MUST also return the traveller's plans whose name contains the word, in a second group (chocomousse spec 008 FR-025).
- **FR-023**: A chat MUST keep its title, trip context, created and last-message times, and its messages: role, text, steps, cards, suggestions, warnings and time.
- **FR-024**: A chat MUST get a short title (at most 40 characters): the first message's start, cut at a word; a rename replaces it.
- **FR-025**: The model MUST see the chat's last 20 messages; the server MUST read them from the stored chat, not from the app.
- **FR-026**: A chat MUST be deleted with its messages 90 days after its last message; plans built in it stay.
- **FR-027**: Chat text, message text, tool arguments and tool results MUST NOT be written to logs or traces (constitution: personal data). Logs MAY hold ids, tool names, counts and timings.
- **FR-028**: Search MUST match words in a chat's title or messages, case-insensitive, for the signed-in traveller only.
- **FR-029**: Deleting the account (spec 009 FR-014) MUST delete its chats and saved items.
- **FR-030**: This spec replaces spec 003 FR-010 ("the server MUST NOT store conversation text") and the "no stored conversation", "no streaming" and "read-only" points of ADR 0018. A new ADR 0021 ("Assistant v2: stored chats, streamed steps, plan tools") records this and marks ADR 0018 as partly superseded; it is written with the plan.

**Limits**

- **FR-031**: Sending assistant messages (the two send routes and spec 003's route; not listing, opening or searching chats) MUST be limited per account, not per IP: 10 per minute and 100 per day per account, and 15 per minute for all accounts together. Past a limit the route answers `429` with `Retry-After`, as spec 003 FR-015. The limits stay settings (`Assistant:RateLimit`). The model provider's own limit (`429`) still ends the answer with the busy warning.
- **FR-032**: Hotel searches from the assistant MUST share the hotels screen's cache and monthly quota (ADR 0017), with no per-account cap. When the quota is used up, the hotels step MUST fail with "Hotels unavailable right now" and the rest of the answer still comes.

**Model**

- **FR-034**: The chat model's provider MUST be a setting: Google Gemini (today) or an OpenAI-compatible provider such as Groq (`openai/gpt-oss-20b`), each with its own key, model name and base address, reached through the same chat abstraction (ADR 0005, ADR 0018). Changing the provider MUST NOT change the routes, the stream, the cards or the tools.
- **FR-035**: The default provider MUST be chosen after a measured run of the scripted questions of SC-001 on each provider, recording tokens per message and answer quality; the result goes in ADR 0021. A missing key for the chosen provider MUST produce the not-configured warning naming that key (spec 003 FR-007).
- **FR-036**: Tool results sent to the model MUST stay small (at most 6 items of a kind, short fields), since the free providers cap tokens per minute and per day; cards sent to the app are not trimmed by this.

**Contract**

- **FR-033**: Every new route MUST need the sign-in of spec 009 and act only on the signed-in traveller's data; another traveller's ids answer `404`.

### Key Entities

- **Chat**: belongs to one account; title, trip context, created and last-message times; its messages. Deleted 90 days after the last message.
- **Message**: role (traveller or assistant), text, time; for the assistant: steps, cards, suggestions, warnings.
- **Step**: tool name, server-written summary, worked or failed, the cards it produced.
- **Card**: kind (plan, flight, hotel, car, place, event, travel facts), the item as its list endpoint returns it (with match when ranked), latitude and longitude, and when it was found.
- **Saved item**: account, kind (hotel, place, event), item id, name, city, photo, position, time saved.
- **Day edit**: a plan, a day number, an action (replace, add, remove), the item it is about and the new item.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Each of the 8 features (plan a trip, flights, hotels, cars, places, events, travel info, preferences) plus saved items can be reached with one message, and its reply carries its own card kind (9 of 9 in a scripted test).
- **SC-002**: For 10 scripted change requests (swaps, car removal, day replace, add and remove, rename, status), the plan afterwards equals what the matching route gives for the same change (10 of 10).
- **SC-003**: When the assistant uses a tool, the first step reaches the app within 3 seconds of sending (excluding uncached hotel searches), and no more than 5 seconds pass without a step or the final event.
- **SC-004**: A chat sent from one device is listed and readable from another device of the same account at once after its answer is saved.
- **SC-005**: No chat text appears in the logs of a full scripted run (checked by searching the logs for the messages' words).
- **SC-006**: No reply carries a price, match, visa rule or plan change that a tool did not produce in that reply (spec 003 SC-002).
- **SC-007**: Without a key for the chosen provider, or with the provider returning 503 or 429, 100 % of messages end with a warning and no 5xx.

## Assumptions

- The traveller is signed in (spec 009); chats, saved items, plans and preferences belong to the account.
- Chats and saved items are stored with the plans and profiles (Firestore, ADR 0017); the store and the stream's
  transport are plan decisions.
- English is the main language; the reply follows the language of the question, as today.
- The model is reached through `Microsoft.Extensions.AI` (ADR 0005, ADR 0018), Gemini or an OpenAI-compatible
  provider (FR-034); at most 6 tool rounds and 50 s per answer stay. Groq's published limits for `openai/gpt-oss-20b`
  (checked 2026-11-10 on its rate-limits page, per organization: 30 requests and 8,000 tokens per minute, 1,000
  requests and 200,000 tokens per day) are tight for ~20 tools and a 20-message history, hence FR-035 and FR-036.
- The app's notice (spec 003 FR-014) names the provider in use; storing chats does not change what the provider receives.
- The MCP server (Sprint 6) later exposes the same tools; this spec only adds them in-process (ADR 0018).
- Chat titles are written by the model from the first exchange; if it gives none, the first words of the message are used.
