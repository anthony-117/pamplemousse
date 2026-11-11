# 0021 — Assistant v2: stored chats, streamed steps, plan tools
Date: 2026-11-11 · Status: Accepted 2026-11-11 with the spec 010 plan; the default provider follows FR-035 · Partly supersedes ADR 0018 · Spec `specs/010-assistant-v2`

## Context
Design v1.0 (approved 2026-11-10) makes the assistant the way into every feature. ADR 0018 kept the assistant
stateless, read-only and unstreamed until accounts and the itinerary existed; both are merged now, and the app
(chocomousse spec 008) shows tool steps live, reopens past chats on any device and changes plans from the chat.

## Decision
- **Stored chats**: chats and their messages are kept per account in Firestore (`travellers/{uid}/chats`), deleted
  90 days after the last message by a daily sweeper; the server reads the last 20 messages itself. This reverses
  ADR 0018's "no stored conversation" and spec 003 FR-010. Chat text is still never logged.
- **Streamed steps**: each message is answered as Server-Sent Events (`chat`, `step`, `final`); a step is written by
  the server when a tool finishes. The answer text is not streamed. The answer runs on a background task, so a closed
  app does not lose it. This reverses "no streaming".
- **Tools that change things**: plan, day-edit, preference and saved-item tools call the same use cases as the
  routes. Days, times, totals and matches stay the itinerary's deterministic rules (ADR 0002). No tool deletes
  anything. This reverses "read-only".
- **Provider as a setting**: Gemini (Google.GenAI) or an OpenAI-compatible provider such as Groq
  (Microsoft.Extensions.AI.OpenAI), behind the same `IChatClient` and tool loop.
- Unchanged from ADR 0018: tools in-process (the MCP server later exposes the same use cases), at most 6 tool rounds
  and 50 s per answer, cards built from tool results, never from the model's text.

## Default provider (FR-035)
To be filled in after the measured SC-001 run on both providers: tokens per message, replies with the right card
kind, and the chosen default.

## Consequences
- Firestore holds personal conversation text: covered by account deletion (spec 009) and the 90-day sweeper.
- The tool list grows to 21, which costs tokens on every call; tool descriptions stay one line and tool results stay
  small (6 items of a kind).
- One new package for the OpenAI-compatible provider.
