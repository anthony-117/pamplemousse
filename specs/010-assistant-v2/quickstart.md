# Quickstart: Assistant v2

## Run

```bash
dotnet run --project src/Pamplemousse.AppHost
```

With `FIREBASE_PROJECT_ID` and a model key set (`GEMINI_API_KEY`, or `ASSISTANT_PROVIDER=openai` with the
`OPENAI_COMPATIBLE_*` settings). Without Firebase, chats and saved items live in memory until the API restarts.

## Check by hand

Use a Firebase ID token of a confirmed account (`TOKEN`):

```bash
curl -N -H "Authorization: Bearer $TOKEN" -H "Accept: text/event-stream" -H "Content-Type: application/json" \
  -d '{"text":"Plan 5 days in Rome for two from Beirut in April"}' http://localhost:5064/api/v1/assistant/chats
```

1. `event: chat` comes first, then a `step` per tool, then `final`; `GET /plans` lists the new draft.
2. `GET /api/v1/assistant/chats` lists the chat; `GET /api/v1/assistant/chats/{id}` returns both messages with their
   steps and cards.
3. Send "swap the second sight on day 2 for something outdoors" to the chat; `GET /plans/{id}` shows the edit.
4. `PUT /api/v1/saved/place/wd%3AQ10285`, then ask "what did I save in Rome?": the answer's cards are that list.
5. Send two messages at once to one chat: the second answers `409 chat-busy`.

## SC-001 measurement (FR-035)

Run the 10 scripted questions once per provider and record, per message, the input and output tokens and whether
each reply carried its card kind. The table goes in ADR 0021 with the chosen default.
