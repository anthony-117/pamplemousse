# Implementation Plan: Assistant v2 — every feature through the assistant

**Branch**: `010-assistant-v2` | **Date**: 2026-11-11 | **Spec**: [spec.md](spec.md)

**Status**: Approved by the owner on 2026-11-11, with the spec changes and the new package below

## Summary

The assistant becomes the way into every feature. A message goes to a chat stored per account; the answer streams
over Server-Sent Events: one `step` event as each tool finishes, then one `final` event with the text, the cards,
up to three next steps and the warnings. The tools grow from six read-only searches to 21: ranked searches (with the
new `search_cars`), the itinerary's own use cases (build, rebuild, swap, day edits), preferences and saved items.
Every tool runs an existing or new use case in-process, as ADR 0018 set; the model never writes a price, a time or a
plan change itself.

Three things the app already calls get their routes here: the day edits and rebuild of a plan, the saved items, and
the chat store with its stream. The shapes come from what `chocomousse` spec 008 already parses (it was built
against this spec's contract ahead of it); [contracts/](contracts/) writes them down.

**Depends on** spec 008 (itinerary) and spec 009 (accounts), both merged.

## Technical Context

**Language/Version**: C# on .NET 10

**Primary Dependencies**:
- `Microsoft.Extensions.AI.OpenAI` (new, needs the owner's OK): the OpenAI-compatible provider (Groq) of FR-034,
  through the same `IChatClient`. Nothing else new: the stream uses ASP.NET Core 10's `TypedResults.ServerSentEvents`.

**Storage**:
- Firestore, under the traveller (ADR 0017):
  - `travellers/{uid}/chats/{chatId}`: the chat (title, trip, preferences, pace, times, busy lease, search terms);
  - `travellers/{uid}/chats/{chatId}/messages/{seq}`: one document per message, steps and cards inside;
  - `travellers/{uid}/saved/{kind}:{id}`: one document per saved item.
- Memory when no Firebase project is set, as for plans.
- Plans keep their document; it gains `edits` (R-5).

**Testing**: xUnit.
- Domain: day edits through the builder, chat rules (title, caps, expiry), saved-item rules.
- Application: the reply loop with a scripted `IChatClient` that calls tools (SC-001, SC-002, SC-006, SC-007).
- Infrastructure: chat and saved stores on the Firestore emulator; the sweeper; the OpenAI-compatible client on
  canned JSON.
- Api: every route through `WebApplicationFactory`, the stream read as SSE, another traveller's ids as 404.

**Target Platform**: Linux container on the Oracle server (ADR 0015), behind Caddy

**Project Type**: Web service

**Performance Goals**:
- The first step within 3 s of sending, and no gap over 5 s without a step or the final event (SC-003). A comment
  line (`: ping`) every 10 s keeps proxies and the app's watchdog from closing a quiet stream.
- Opening a chat: two Firestore reads (the chat and one query for its messages).

**Constraints**:
- The free model tiers' token limits (spec Assumptions): tool results to the model at most 6 items with short fields
  (FR-036); the last 20 messages as history, as today.
- No paid request the screens wouldn't make: the same use cases, caches and quotas (FR-011, FR-032).
- Chat text, tool arguments and results never in logs or traces (FR-027).

**Scale/Scope**:
- 4 new route groups (chats and the stream, saved, day edits, rebuild), 15 new tools;
- 3 stores, 1 hosted sweeper;
- 1 Domain addition to the builder (day edits);
- 1 ADR.

## Spec changes found while planning

Approved with this plan and applied to the spec (FR-005, FR-012b, FR-016, FR-022, FR-024, FR-031).

1. **FR-005, flights**: a flight card carries the arrival airport's position, from the bundled flight cities, not the
   arrival city's centre; with no known airport, the city centre and `approximate: true`.
2. **FR-016, removing an item**: the removed item's slot becomes free time and stays so through later rebuilds; the
   builder does not refill it. (The app already says "its time becomes free time".)
3. **FR-022, search**: search matches whole words (as the spec says), not word starts: "jaz" finds nothing,
   "jazz" finds the chat. Firestore has no text search; whole words keep it one indexed query (R-7).
4. **FR-024, title**: the title is the first 40 characters of the first message, cut at a word, not written by the
   model. One fewer model call per new chat, on the tightest limits (R-9). The spec's own fallback is this rule.
5. **FR-031, which routes are limited**: only sending (`POST /assistant/chats`, `POST /assistant/chats/{id}/messages`
   and spec 003's `POST /assistant/messages`); listing, opening and searching chats are not.
6. **Cards** carry the item's city (`destinationId`, `destinationName`) in the envelope, so Saved files an item under
   the right city. The app already reads it when present.
7. **`GET /plans` items** gain `destinationId`, `adults`, `tripBudget`, `pace`, `lat` and `lng`, and the plan gains
   `edits`, as the app's plan cards and Trip pane read them (contract additions only).

## Constitution Check

| Principle | How this plan meets it | Result |
|---|---|---|
| I. Layered Architecture | Chat, message, saved item and day edit in Domain; the reply loop, tools and handlers in Application; stores, the sweeper and the model clients in Infrastructure; SSE, routes and card JSON in Api. Cards are written as JSON by the Api's existing list mappings through an Application interface (R-4) | Pass |
| II. Every Feature Is Tested | Each tool with a scripted model; SC-001, SC-002, SC-006 and SC-007 as tests; stores on the emulator; the stream read as SSE; owner scoping on every route | Pass |
| III. Zero Lint Findings | Normal build gate | Pass |
| IV. Partial Failure | A failed tool is a failed step and the answer goes on; no key, a busy model or a timeout ends with a warning in `final`, never a 5xx (spec Edge Cases) | Pass |
| V. Low Cost First | Same use cases, caches and quotas as the screens; trimmed tool results; the title without a model call; one Firestore read per open message | Pass |
| VI. Stable API Contract | New routes only; spec 003's route keeps working until the app no longer calls it (FR-007); additions to the plan and its summary | Pass |
| VII. Simplicity | One new package, and only for FR-034; a background answer behind a channel instead of a queue; a daily sweeper instead of TTL policies | Pass, with the package to approve |
| Configuration and Security | Provider settings in `Env.cs`; chat text never logged (FR-027); tools act only on the signed-in traveller's data; no tool deletes anything (FR-014); tool results stay data for the model (R-10) | Pass |

## Project Structure

### Documentation (this feature)

```text
specs/010-assistant-v2/
├── spec.md · plan.md · research.md · data-model.md · quickstart.md
├── contracts/ chat-api.md · plan-edits-api.md · saved-api.md
└── tasks.md             # next step, after approval
docs/architecture/adr/0021-assistant-v2.md   # new; ADR 0018 marked partly superseded
```

### Source Code

```text
src/
├── Pamplemousse.Api/
│   ├── Controllers/ChatsController.cs           # list, get, rename, delete, search; the two streamed sends
│   ├── Controllers/SavedController.cs           # PUT, DELETE, GET, GET ids
│   ├── Controllers/PlansController.cs           # + day alternatives, day items, rebuild
│   ├── Streaming/ChatStream.cs                  # SseItem events, pings, refusals before the first byte
│   ├── Mappings/CardJson.cs                     # ICardJson: cards as the list endpoints write their items
│   ├── RateLimiting/AssistantRateLimits.cs      # per account (sub), sends only
│   └── Env.cs                                   # ASSISTANT_PROVIDER, OPENAI_COMPATIBLE_*
├── Pamplemousse.Application/
│   ├── Abstractions/ IChatStore · ISavedStore · ICardJson
│   ├── Assistant/Reply/ ReplyHandler · ReplyRun (channel of steps) · StepSummaries · Suggestions
│   ├── Assistant/Tools/ Search · Plan · DayEdit · Preferences · Saved tool sets
│   ├── Assistant/Chats/ list, get, rename, delete, search handlers
│   ├── Itinerary/ DayAlternatives · ChangeDayItem · RebuildPlan handlers
│   └── Saved/ save, remove, list, ids handlers
├── Pamplemousse.Domain/
│   ├── Assistant/ Chat · ChatMessage · ToolStep · ChatTitle · ChatLimits
│   ├── Itinerary/ PlanEdit · DayEdits (applied by PlanBuilder)
│   └── Saved/ SavedItem · SavedKind
└── Pamplemousse.Infrastructure/
    ├── Storage/ FirestoreChats · InMemoryChats · FirestoreSaved · InMemorySaved
    ├── Assistant/ ChatSweeper (hosted, daily) · OpenAiCompatibleErrorTranslator
    └── DependencyInjection.cs                   # provider choice (FR-034)

tests/  one folder per area in each test project, as for spec 008
```

**Structure Decision**: the reply runs as a background task that writes steps into a channel and saves the answer
when it ends; the request only reads the channel. So a closed app does not lose the answer (spec Edge Cases), and the
loop is tested without HTTP. Cards are stored as the JSON the app receives, so an old chat shows exactly what was
found, when it was found.

## How one message is answered

1. Check the request (`text` 1–1,000, the trip, preferences, pace, `about`), naming the field (`400`).
2. The account's limits (`429` with `Retry-After`).
3. Load or create the chat; refuse `404` (not this traveller's, or expired), `409 chat-full` (200 messages),
   `409 chat-busy` (a lease from another send is still running).
4. Take the busy lease (a Firestore transaction), save the traveller's message, send `event: chat`.
5. Start the answer in the background: system instructions with the trip and `about`, the last 20 messages, the 21
   tools. Each tool, when it finishes, writes a `step` (server summary, ok, total, cards) to the channel.
6. The request streams each step as it arrives, with a ping every 10 s.
7. At the end (answer, warning or 50 s): next steps, `planChanged`, save the assistant message, release the lease,
   send `event: final`. If the request has gone, the answer is still saved.

## Delivery order

1. Domain and stores: chat, messages, saved items, plan edits (with the emulator tests).
2. Plan routes the app already calls: day alternatives, day items, rebuild, summary additions.
3. Saved routes.
4. Chat routes and the stream with the current six tools, then the new tools in groups (search, plan, day edits,
   preferences, saved).
5. Per-account limits, the provider setting, the sweeper, account deletion.
6. SC-001 measured on Gemini and Groq; the default provider and the numbers go in ADR 0021 (FR-035).
7. chocomousse T096 against this branch.
