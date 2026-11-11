# Data Model: Assistant v2

## Domain

| Type | Fields | Rules |
|---|---|---|
| `Chat` | `Id`, `Owner`, `Title`, `Trip` (`ChatTrip`), `Preferences?`, `Pace?`, `CreatedAt`, `LastMessageAt`, `MessageCount`, `BusyUntil?`, `Terms` | `ExpiresAt` = `LastMessageAt` + 90 days; full at 200 messages; busy while `BusyUntil` > now |
| `ChatMessage` | `Seq` (`0001`…), `Role` (traveller, assistant), `Text`, `At`, `About?`; assistant: `Steps`, `CardsJson`, `Suggestions`, `Warnings`, `PlanChanged?` | text 1–1,000 for the traveller |
| `MessageAbout` | `PlanId`, `Day?`, `ItemId?` | `ItemId` needs `Day` |
| `ToolStep` | `Tool`, `Summary`, `Ok`, `Total?`, `CardsJson` | summary written by the server |
| `ChatTitle` | — | the first message's first 40 characters, cut at a word (R-9); a rename is 1–40 |
| `ChatTerms` | — | distinct normalised words, 2–30 letters, at most 2,000 (R-7) |
| `ChatLimits` | `MaxMessages` 200, `MaxChats` 500, `KeepDays` 90, `History` 20 | settings |
| `SavedItem` | `Owner`, `Kind` (hotel, place, event), `Id`, `DestinationId`, `DestinationName`, `Name`, `ImageUrl?`, `Location`, `SavedAt`, `ItemJson?` | name 1–200; item at most 20 KB; at most 200 per account |
| `PlanEdit` | `Day`, `Action` (replace, add, remove), `ItemId?`, `NewItem?` (`Kind`, `Id`, `Name`) | applied by `PlanBuilder` (R-5) |

`ChatTrip` (spec 003) gains `TripBudget` and the origin's name is never stored (the app keeps it).

## Firestore

```text
travellers/{uid}
├── chats/{chatId}               title, trip, preferences, pace, createdAt, lastMessageAt, messageCount,
│   │                            busyUntil, terms[]
│   └── messages/{seq}           role, text, at, about, steps[], cards[] (JSON strings), suggestions[],
│                                warnings[], planChanged
├── saved/{kind}:{id}            destinationId, destinationName, name, imageUrl, lat, lng, savedAt, item (JSON)
└── plans/{planId}               (spec 008) + edits[]
```

Indexes: a collection-group index on `chats.lastMessageAt` for the sweeper; `chats.terms` (array-contains) with
`lastMessageAt` descending for search.

## Application

| Type | Purpose |
|---|---|
| `IChatStore` | create (with the 500 cap), get, list page, rename, delete, search terms, append message, take and release the lease, delete all (account) |
| `ISavedStore` | put (idempotent, 200 cap), delete, list (kind, city), ids, delete all |
| `ICardJson` | a typed card → the JSON the app receives (Api implements it) |
| `ReplyRun` | one answer: the channel of steps, the session's cards and warnings, `planChanged` |
| `ReplyEvent` | `Started`, `Step`, `Final`, `Failed`: what the Api writes as SSE |

## Account deletion (spec 009)

`DeleteAccount` deletes, in order: chats with their messages, saved items, plans, the profile, the sign-in.
