# API Contract: Chats and the answer stream

Every route needs the sign-in of spec 009 (FR-033). Another traveller's chat, or one past its 90 days, answers `404`.
Refusals are Problem Details with a `code` where the app acts on it. This is what `chocomousse` spec 008 parses.

## Send

- `POST /api/v1/assistant/chats`: the first message of a new chat.
- `POST /api/v1/assistant/chats/{chatId}/messages`: the next message of a chat.

Request (`Accept: text/event-stream`):

```json
{
  "text": "Plan 5 days in Rome for two, from Beirut, under $1,200",
  "trip": {
    "destinationId": "3169070", "from": "2027-04-10", "to": "2027-04-14", "adults": 2,
    "originId": "276781", "passport": "LB", "currency": "USD", "tripBudget": 1200
  },
  "preferences": { "budget": 90, "culture": 90, "food": 50, "nature": 20, "nightlife": 20, "comfort": 50 },
  "pace": "balanced",
  "about": { "planId": "6f1c…", "day": 2, "itemId": "wd:Q10285" }
}
```

- `text` 1–1,000 characters after trimming. Every other field is optional; a missing `preferences` or `pace` means
  the profile's.
- `about` says the message is about a plan, a day of it or an item of that day (the panel's swap icon and "Ask to
  change this day…").

Refusals, before any event:

| Status | `code` | When |
|---|---|---|
| `400` | — | a field is wrong; `errors` names it |
| `404` | — | no such chat for this traveller |
| `409` | `chat-busy` | another send in this chat is still answering |
| `409` | `chat-full` | 200 messages; `detail`: "This chat is full. Start a new chat to carry on." |
| `429` | — | the account's limit; `Retry-After` in seconds |

The stream (`200`, `text/event-stream`), in order:

```text
event: chat
data: {"chatId":"c9","title":"Plan 5 days in Rome for two, from","messageId":"0001"}

event: step
data: {"tool":"search_flights","summary":"Searched flights Beirut → Rome · 23 fares","ok":true,"total":23,"cards":[…]}

: ping

event: step
data: {"tool":"search_hotels","summary":"Hotels unavailable right now","ok":false,"total":null,"cards":[]}

event: final
data: {"messageId":"0002","text":"Here is a 5-day plan…","cards":[…],"suggestions":["Add a car"],"warnings":[],
       "planChanged":"6f1c…","title":"Plan 5 days in Rome for two, from","at":"2026-11-11T10:02:11Z"}
```

- `chat` comes first: the chat id (new or the same), its title and the traveller's message id as stored.
- One `step` per tool, as it finishes. `summary` is written by the server; `total` is how many were found (for "See
  all N"), or null; `cards` are what that tool produced, best first, at most 6 of a kind.
- `final` carries the whole answer. `cards` are all the reply's cards, grouped by kind in the order plan, flight,
  hotel, car, place, event, facts. `suggestions` are 0–3, each under 60 characters. `planChanged` is the id of a plan
  this answer built or changed, else absent.
- `error` (`{"warning": "…"}`) ends a stream that cannot finish (the store failed). A missing key, a busy model or a
  timeout is not an error: `final` comes with empty `text` and the warning.
- A `: ping` comment every 10 s while the answer runs.

## Cards

Every card has the same envelope:

```json
{
  "kind": "hotel",
  "id": "bk:12345",
  "lat": 41.9, "lng": 12.49, "approximate": false,
  "foundAt": "2026-11-11T10:01:58Z",
  "destinationId": "3169070", "destinationName": "Rome",
  "item": { … }
}
```

| `kind` | `item` | Notes |
|---|---|---|
| `plan` | the `GET /plans` item, with its additions ([plan-edits-api.md](plan-edits-api.md)) | `id` is the plan id |
| `flight` | the flights list item of spec 001, plus `match` (spec 007) | position: the arrival airport |
| `hotel` | the hotels list item of spec 002, plus `match` | |
| `car` | the cars list item of spec 004, plus `match` | position: the pick-up place |
| `place` | the places list item | sights, restaurants, nature, nightlife |
| `event` | the events list item, plus `location` when the venue's is known | `approximate` without one |
| `facts` | `destination` (`id`, `name`, `countryCode`), `from`, `to`, `weather` (`kind`: forecast or climate, `summary`, `maxC`, `minC`, `rainChance`), `visa`, `currency` (`from`, `to`, `rate`, `date`), `safety`, `holidays` | each part null when its source had nothing |

- `match` is null when none was computed, never 0.
- `approximate: true` when the position is the city centre, not the item's own.

## Recents

`GET /api/v1/assistant/chats?limit=30&before={cursor}` → latest message first:

```json
{
  "items": [
    { "id": "c9", "title": "Rome in April", "trip": { … }, "createdAt": "…", "lastMessageAt": "…", "messageCount": 4 }
  ],
  "next": "opaque-cursor-or-null",
  "keepDays": 90
}
```

`limit` 1–100.

## Open a chat

`GET /api/v1/assistant/chats/{chatId}`:

```json
{
  "id": "c9", "title": "Rome in April", "trip": { … }, "preferences": { … }, "pace": "balanced",
  "createdAt": "…", "lastMessageAt": "…", "expiresAt": "…", "busy": false,
  "messages": [
    { "id": "0001", "role": "traveller", "text": "…", "about": null, "at": "…" },
    { "id": "0002", "role": "assistant", "text": "…", "steps": [ … ], "cards": [ … ], "suggestions": [ … ],
      "warnings": [ … ], "planChanged": "6f1c…", "at": "…" }
  ]
}
```

Messages oldest first, all of them (the model only sees the last 20). `busy` is true while another device's answer
runs.

## Rename, delete

- `PATCH /api/v1/assistant/chats/{chatId}` with `{ "title": "Rome with Maya" }` (1–40 characters) → the Recents item.
- `DELETE /api/v1/assistant/chats/{chatId}` → `204`. Plans built in it stay.

## Search

`GET /api/v1/assistant/chats/search?q=jazz` (2–100 characters) →
`{ "chats": [ Recents items ], "plans": [ GET /plans items ] }`, latest first. Whole words, case and accents ignored;
every word of `q` must appear.

## Spec 003

`POST /api/v1/assistant/messages` keeps working unchanged (FR-007). The app no longer calls it; its removal is a later
change.
