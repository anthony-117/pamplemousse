# Tasks: Assistant v2

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md),
[contracts/](contracts/). Plan approved by the owner on 2026-11-11; specs 008 and 009 merged.

Each phase ends with every test passing (`dotnet test`) and no analyzer finding.

**Done on 2026-11-11: T001–T027.** The Firestore store tests (chats, saved items) run on the emulator in CI; they need
Docker. Where the code differs from the names below: the Domain message is `ChatEntry` (with the existing `Speaker` as
its role, since `ChatMessage` and `ChatRole` are Microsoft.Extensions.AI's), and flight cards sit at the arrival city's
centre with `approximate: true`, since the bundled flight cities have no airport positions (FR-005's fallback). Left: T028 (the owner's live run) and
T029 (chocomousse T096).

## Phase 1 · Plan edits (FR-012a, FR-012b, FR-013, FR-016 to FR-018)

- [x] T001 Domain `Itinerary/PlanEdit.cs`: `PlanEdit` (day, action, item, new item), validation naming the field.
- [x] T002 `PlanBuilder` applies edits while building (R-5): a replacement takes the slot, an addition the next free
  slot (`DayFullException`), a removal leaves free time; an edit for a day that no longer exists is dropped with a
  note. `Plan` keeps `Edits`.
- [x] T003 Domain `Itinerary/DayAlternatives.cs`: up to 6 places or events for a day, by kind, near the day, ranked by
  the plan's weights, not in the plan.
- [x] T004 Application: `GetDayAlternatives`, `ChangeDayItem` (replace, add, remove), `RebuildPlan` (unchanged on
  failure); `PlanSummary` gains destination id, adults, trip budget, pace, position.
- [x] T005 Store: edits saved and read back (Firestore and memory).
- [x] T006 Api: the three day routes and rebuild; summary and plan view model additions; `422 day-full`.
- [x] T007 Tests: every edit rule, edits kept through swap and rebuild, alternatives, handlers, routes, another
  traveller's plan.

## Phase 2 · Saved items (FR-020, FR-021, FR-029)

- [x] T008 Domain `Saved/SavedItem.cs`: kinds, name 1–200, item at most 20 KB, 200 per account.
- [x] T009 `ISavedStore`; `FirestoreSavedStore`, `InMemorySavedStore`; registered.
- [x] T010 Application handlers: save (idempotent), remove, list (kind, city), ids.
- [x] T011 Api `SavedController`; `409 too-many-saved` with `max`.
- [x] T012 Tests: domain rules, handlers, routes, owner scoping.

## Phase 3 · Chats (FR-022 to FR-026, FR-028)

- [x] T013 Domain `Assistant/Chat.cs`, `ChatMessage`, `ToolStep`, `ChatTitle` (R-9), `ChatTerms` (R-7), `ChatLimits`.
- [x] T014 `IChatStore`; `FirestoreChatStore` (R-6), `InMemoryChatStore`: create with the 500 cap, page, get, rename,
  delete, search terms, append, lease.
- [x] T015 Application: list, get, rename, delete, search (chats and plans).
- [x] T016 Api `ChatsController` read routes.
- [x] T017 Tests.

## Phase 4 · The answer stream and tools (FR-001 to FR-015, FR-019, FR-025, FR-027, FR-036)

- [x] T018 Application `Assistant/Reply/`: `ReplyHandler` (refusals, lease, background run, channel, save, release),
  `ReplyRun` (steps, cards, warnings, plan changed), next-step suggestions, `ICardJson`.
- [x] T019 Tools: the six of spec 003 ranked and as cards, `search_cars`, plan tools, day-edit tools, preferences,
  saved items; each step's server summary.
- [x] T020 Api: `CardJson` with the list mappings; the two send routes as Server-Sent Events with pings.
- [x] T021 Tests with a scripted chat model: SC-001 (9 card kinds), SC-002 (10 changes), SC-006, SC-007, a closed
  stream still saves, `chat-busy`, `chat-full`, no chat text in logs (SC-005).
  *Done so far*: plan and hotel cards (hotel cards equal the list's items), save from a card, history, `chat-busy`, a
  closed stream still saves, busy and unset model end with a warning, refusals, Recents, rename, search, delete.
  Then `AssistantCoverageTests`: every card kind plus saved items (SC-001), ten changes by chat equal to the same
  changes by route (SC-002), `chat-full`, and no chat text, arguments or answers in the logs (SC-005).

## Phase 5 · Limits, provider, expiry, deletion (FR-026, FR-029, FR-031, FR-034, FR-035)

- [x] T022 Rate limits per account on the send routes only.
- [x] T023 `ASSISTANT_PROVIDER` and `OPENAI_COMPATIBLE_*` in `Env.cs`; the OpenAI-compatible client with its error
  translation; README and `.env.example`.
- [x] T024 `ChatSweeper` (daily), expired chats read as missing.
- [x] T025 Account deletion removes chats and saved items.
- [x] T026 Tests.

## Phase 6 · Check

- [x] T027 Docs: README endpoints, sprint log ([sprint 5](../../docs/project/sprints/sprint-05.md)).
- [ ] T028 The owner's live run of SC-001 on Gemini and Groq; the numbers and the default in ADR 0021 (FR-035).
- [ ] T029 chocomousse T096 against this branch.
