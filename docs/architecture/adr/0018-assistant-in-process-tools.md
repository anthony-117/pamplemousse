# 0018 — Assistant: Gemini through Google.GenAI, tools in the API, no stored chat
Date: 2026-11-04 · Status: Partly superseded by ADR 0021 (stored chats, streaming, tools that change plans) · Builds on ADR 0002 and ADR 0005

## Context
The assistant (TG-11) is started before the itinerary (Sprint 4), the MCP server (Sprint 6) and accounts. ADR 0005 chose
Gemini Flash through `Microsoft.Extensions.AI`; ADR 0002 said the chatbot's tools are our own use cases, exposed on our MCP
server. Neither says how Gemini is reached, where the tools run before the MCP server exists, or where a conversation lives.

## Decision
- **Client**: Google's official `Google.GenAI` SDK, used only through `IChatClient` (`client.AsIChatClient(model)`). The model is the `GEMINI_MODEL` setting (default `gemini-2.5-flash`).
- **Tools**: the assistant's tools are `AIFunction`s in Application that call the existing query handlers directly, in the same process. `FunctionInvokingChatClient` runs the loop, at most 6 calls per reply. When the MCP server arrives (Sprint 6), it exposes the same handlers; the in-process tools stay for the app.
- **Read-only**: every tool is a search. Plan-changing tools come with the itinerary.
- **Cards** come from the tools that ran, not from the model's text, so the links the app shows are always real searches.
- **No stored conversation**: the app sends the last 20 messages each time; the server neither stores nor logs them. Saved conversations wait for accounts.
- **No streaming** for now; the app shows a typing state.

## Consequences
- The assistant works today with no database and no new service; swapping Gemini for another model is a registration change.
- Every reply re-sends the conversation, so long chats cost more tokens; capped at 20 messages.
- Anyone using the public web app shares the free tier's daily limit until accounts and per-user limits exist.
- The free tier may use prompts for training (ADR 0005); the app tells users not to share personal details.
