# 0005 — AI stack
Date: 2026-10-26 · Status: Accepted

## Decision
- LLM: **Gemini Flash** (Google AI Studio free tier for development), through `Microsoft.Extensions.AI` so it can be swapped by configuration.
- RAG content: **saved user trips and preferences** (personalisation).
- Vector store: **pgvector** in the main Postgres database.
- Embeddings: **Gemini embeddings**.

## Consequences
- RAG has nothing to retrieve until users save trips; it belongs in a late sprint. Wikivoyage can be added later for city knowledge.
- The Gemini free tier may use prompts for training → move to the paid tier before real users.
- Prices and availability are never stored in RAG.
