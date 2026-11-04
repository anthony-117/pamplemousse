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

## Known limits
- **Gemini availability**: on 4 Nov `gemini-3.8-flash` (the default) returned 503 "high demand" all afternoon; `gemini-2.5-flash` answered in under 1 s. The model is the `GEMINI_MODEL` setting. See [quickstart](../../../specs/003-assistant/quickstart.md).
- **Shared free tier**: anyone using the web app shares the free daily limit until accounts exist.
- **No saved conversations** until accounts; the app keeps the chat for the session.
- **Local test run**: the 9 Testcontainers tests (Redis, Firestore emulator) fail on a machine whose Docker Hub login has expired (`docker logout` fixes it); CI is unaffected.

## Next
- SC-001/SC-002: 10 scripted questions on one model, recorded in the quickstart.
