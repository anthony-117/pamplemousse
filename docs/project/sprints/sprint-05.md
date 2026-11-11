# Sprint 5 review — in progress

Started 4 Nov 2026, ahead of the planned 9–22 Feb 2027 window and before Sprints 3 (weighted search) and 4 (itinerary).
Built with Spec Kit: [assistant API](../../../specs/003-assistant/) and the app's `specs/003-assistant-screen`. Decisions in
[ADR 0018](../../architecture/adr/0018-assistant-in-process-tools.md).

## Delivered: assistant (backend)
`POST /api/v1/assistant/messages`: Gemini through `Microsoft.Extensions.AI` with six read-only tools over the existing use cases
(city search, overview, places, events, flights, hotels). Each search the assistant runs comes back as a card that opens the
matching screen. No key, Gemini busy (429/503), or slower than 30 s per call / 50 s per answer → a warning, never a 5xx.
Nothing about the conversation is stored or logged.

| Story | Result |
|---|---|
| TG-11 Ask about a destination | ✅ Answers from the overview; checked identical to `/overview` (visa, weather) |
| TG-11 Ask for hotels, flights, places, events | ✅ Hotels checked identical to `/hotels`; flights from a departure city the assistant asks for |
| TG-11 Change my plan by chatting | ❌ Not possible yet: no itinerary (Sprint 4) |

## Delivered: assistant screen (app)
`chocomousse` `003-assistant-screen`, from Figma 09 and the chat column of W6 (approved with v0.2): header with the trip,
bubbles, ActionCards that open hotels / flights / places / overview with the card's dates and adults, suggestion chips,
input, the Gemini notice, typing / failed / busy states with Try again, New chat, and "Ask the assistant" on each city's
overview. The W6 conversation list waits for accounts.

Checked live on 4 Nov at 375 px and 1440 px against the local API on `gemini-2.5-flash`: Rome visa question → answer and
city-guide card; "3 cheapest hotels" → prices identical to `/hotels`, card opened Hotels for 4–8 Dec, 2 adults; Gemini 503
→ busy message, Try again → "Restaurants in Paris" with its card.

Tests: app 138 (26 new: data, controller, screen, flow). Backend: Domain 92, Application 78, Infrastructure 127 (9 need Docker Hub access, see below), Api 73, Architecture 5.

## Delivered: assistant v2 (backend, spec 010, 11 Nov)
Design v1.0 makes the assistant the way into every feature. Built on `010-assistant-v2` from the
[spec](../../../specs/010-assistant-v2/spec.md) and [plan](../../../specs/010-assistant-v2/plan.md) the owner approved on
11 Nov; [ADR 0021](../../architecture/adr/0021-assistant-v2.md) partly supersedes ADR 0018.

| Story | Result |
|---|---|
| TG-11 Plan a trip in one message | ✅ `build_plan` saves a draft; steps stream as each tool finishes, then the answer with cards and next steps |
| TG-11 Ask for any one thing | ✅ Ranked flights, hotels and the new `search_cars`; places, events, travel facts; hotel and car cards equal the list items |
| TG-11 Change my plan by chatting | ✅ Swaps, car, day replace/add/remove, rename, status and rebuild; 10 changes by chat equal the same changes by route |
| TG-11 Come back to a chat | ✅ Chats stored per account, Recents, rename, delete, whole-word search; deleted 90 days after the last message |
| TG-11 Preferences and saved items | ✅ `get/update_preferences`; saved hotels, places and events, by route and by tool |

Also: sending is limited per account (10 a minute, 100 a day, 15 a minute in all), the model provider is a setting
(Gemini or an OpenAI-compatible one such as Groq), and deleting an account deletes its chats and saved items.

Found and fixed while testing: alternatives offered an item just removed; a removed event couldn't be replaced by an
added one; a validation problem lost its `errors` when the request asked for `text/event-stream`; the last messages of a
chat were read with an order Firestore can't serve without an index; the sweep listed travellers with an admin-only call
(it now reads a `chatOwners` list).

Tests: Domain 277, Application 158, Api 188, Infrastructure (in memory) as before, plus Firestore emulator contracts for
chats and saved items in CI.

**Incident, 11 Nov**: before the test setup kept the new chat and saved stores in memory, local test runs wrote 19
documents for test accounts (`travellers/uid-…`) to the real Firestore project through a developer `.env`. They were
deleted the same day with the owner's OK; a test now fails if any store in the test API is not the in-memory one.

## Known limits
- **Gemini availability**: on 4 Nov `gemini-3.8-flash` returned 503 "high demand" all afternoon; `gemini-2.5-flash` answered in under 1 s, so 2.5-flash is the default. The model is the `GEMINI_MODEL` setting. See [quickstart](../../../specs/003-assistant/quickstart.md).
- **Shared free tier**: anyone using the web app shares the free daily limit until accounts exist. Since 5 Nov the API limits the assistant to 5 messages per minute and 40 per day per IP, and 10 per minute overall (FR-015); the counts reset when the API restarts.
- **No saved conversations** until accounts; the app keeps the chat for the session. (Spec 010 stores them, above.)
- **Local test run**: the 9 Testcontainers tests (Redis, Firestore emulator) fail on a machine whose Docker Hub login has expired (`docker logout` fixes it); CI is unaffected.

## Next
- Spec 010 FR-035: the scripted questions on Gemini and on Groq, tokens per message and replies with the right card;
  the default provider and the numbers go in ADR 0021.
- chocomousse spec 008 T096: the app against `010-assistant-v2`, at 390 and 1440 px, light and dark.
