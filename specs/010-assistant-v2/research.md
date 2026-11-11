# Research: Assistant v2

## R-1 The stream

- **Decision**: Server-Sent Events from ASP.NET Core 10's `TypedResults.ServerSentEvents`, one `POST` per message.
  Events: `chat`, `step`, `final`, `error`; a `: ping` comment every 10 s. Refusals (400, 404, 409, 429) are normal
  Problem Details answers, sent before the stream starts.
- **Rationale**: one-way, plain HTTP through Caddy, and the app already reads it (`dart:io` on devices, a
  `js_interop` fetch reader on the web, its research R-1). Built into .NET 10, so no package.
- **Alternatives considered**: WebSockets (two-way, which nothing needs, and harder behind the proxy); polling a
  message status (slower steps, more reads).

## R-2 Steps as each tool finishes

- **Decision**: every tool is an `AIFunction` wrapped by `ReplyRun`. When a tool returns, the wrapper builds the
  step (tool name, a server-written summary from the result, ok, total, cards) and writes it to the run's channel.
  `FunctionInvokingChatClient` still runs the loop (at most 6 rounds, ADR 0018).
- **Rationale**: steps then come from what ran, never from the model's text (FR-002, SC-006), and the loop stays the
  library's.
- **Alternatives considered**: `GetStreamingResponseAsync` and reading tool-call updates: the updates show calls,
  not their results, and differ between providers.

## R-3 The answer outlives the request

- **Decision**: the answer runs on a background task with the app's stopping token, not the request's. The request
  reads the channel; if it goes away, the task still saves the answer. A busy lease on the chat (`busyUntil`, now +
  70 s, taken in a Firestore transaction) refuses a second send (`409 chat-busy`) and shows `busy: true` to another
  device; saving the answer clears it, and an expired lease is free.
- **Rationale**: the spec's "the stream breaks" and "two messages at once" edge cases, with no queue.
- **Alternatives considered**: a job queue (another service to run and pay for); cancelling with the request (loses
  answers when a phone locks).

## R-4 Cards as the list endpoints write them

- **Decision**: Application builds typed cards (kind, id, position, found at, city, the item as the use case returned
  it). `ICardJson` (Application abstraction, implemented in Api with the existing list mappings) turns each into the
  JSON the app receives. The stream and the store both use that JSON.
- **Rationale**: "the item as its list endpoint returns it" (FR-004) by construction, and old chats show what was
  found then. Dependencies still point inwards.
- **Alternatives considered**: new card view models (two shapes for one item, which would drift); storing typed
  cards in Infrastructure (needs the Api's mappings there).

## R-5 Day edits are builder inputs

- **Decision**: a plan keeps a list of edits (`replace`, `add`, `remove`, each with a day and the items). `PlanBuilder`
  applies them while it builds: a replacement takes the replaced item's slot, an addition takes the day's next free
  slot (none: `422 day-full`), a removal leaves free time. A rebuild or a swap builds again with the same edits, so
  they last; an edit whose day no longer exists (shorter dates) is dropped with a note.
- **Rationale**: times, walks, totals and the match stay the builder's (FR-013, ADR 0002); the same input gives the
  same plan (spec 008 SC-005).
- **Alternatives considered**: patching the built day (a second set of timing rules, lost on the next rebuild).

## R-6 Where a chat lives

- **Decision**: the chat document holds its header; each message is its own document under it, keyed by a 4-digit
  sequence (`0001`…). A message holds its steps and cards inline.
- **Rationale**: a Firestore document is at most 1 MiB; 200 messages with cards would pass it, one message never
  does. The sequence orders messages and is the id the app shows.
- **Alternatives considered**: all messages in the chat document (hits the size limit); random ids (need a separate
  order field).

## R-7 Search

- **Decision**: each chat keeps `terms`, its distinct normalised words (lower case, accents removed, 2–30 letters,
  at most 2,000) from the title and messages. Search takes the query's longest word, asks Firestore `array-contains`
  for it, then keeps the chats that contain every word of the query. Plans are matched by name in memory (at most 50).
- **Rationale**: one indexed query per search, and Firestore has no text search.
- **Alternatives considered**: reading every chat (up to 500 reads a search); a search service (cost, another
  dependency); word-start matching (one stored term per prefix).

## R-8 Expiry after 90 days

- **Decision**: a hosted `ChatSweeper` runs once a day. Each new chat also writes `chatOwners/{uid}`; the sweep reads
  that list and, per traveller, queries the chats with `lastMessageAt` older than 90 days and deletes each with its
  messages. (A collection-group query needs a manual index; listing `travellers` needs admin metadata calls, which the
  emulator refuses.) Reads treat an expired chat as missing.
  Starting chat 501 deletes the chat with the oldest last message (FR-022a).
- **Rationale**: Firestore TTL policies delete a document but not its subcollection, and need console setup; the
  sweeper is code, tested on the emulator.

## R-9 The title

- **Decision**: the first 40 characters of the first message, cut at the last whole word, without a trailing
  punctuation mark ("Plan 5 days in Rome for two in April" → "Plan 5 days in Rome for two in April").
- **Rationale**: no extra model call on the tightest free limits; the spec's fallback already.

## R-10 Prompt injection and scope

- **Decision**: tool results go to the model as data only (JSON in tool messages, never in the system message). Every
  tool takes the signed-in traveller from the run, never from the model. No tool deletes a plan, chat, saved item or
  account (FR-014). A plan tool reads the latest saved plan each time.
- **Rationale**: the spec's prompt-injection edge case; the worst a hostile event text can cause is a change the
  traveller can undo.

## R-11 The model provider

- **Decision**: `ASSISTANT_PROVIDER` = `gemini` (default until measured) or `openai`. Gemini as today (Google.GenAI);
  `openai` through `Microsoft.Extensions.AI.OpenAI` with `OPENAI_COMPATIBLE_BASE_URL`, `OPENAI_COMPATIBLE_API_KEY`
  and `OPENAI_COMPATIBLE_MODEL` (Groq: `https://api.groq.com/openai/v1`, `openai/gpt-oss-20b`). Both behind the same
  tool loop and an error translator that turns 429 and 503 into the busy warning.
- **Rationale**: FR-034. 21 tool schemas and 20 messages are close to Groq's 8,000 tokens a minute, so FR-035's run
  decides the default; tool descriptions are kept to one line.

## R-12 Limits per account

- **Decision**: the rate limiter's partition is the token's `sub` (after authentication); only the three send routes
  are limited: 10 a minute and 100 a day per account, 15 a minute in all (settings, FR-031).
- **Rationale**: every route is signed in now, so the IP rule of spec 003 no longer fits a shared network.
