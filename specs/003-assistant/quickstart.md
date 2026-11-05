# Quickstart: Trip assistant

## Run it

1. In `.env`, set `GEMINI_API_KEY` (Google AI Studio) and, optionally, `GEMINI_MODEL` (default `gemini-2.5-flash`).
2. `dotnet run --project src/Pamplemousse.Api`
3. Ask:

```bash
curl -s -X POST localhost:5064/api/v1/assistant/messages -H 'Content-Type: application/json' -d '{
  "messages": [{ "role": "traveller", "text": "Do I need a visa, and what will the weather be like?" }],
  "context": { "destinationId": "3169070", "from": "2026-11-20", "to": "2026-11-24", "passport": "LB", "currency": "USD" }
}'
```

Without a key the reply is a warning naming `GEMINI_API_KEY`; when Gemini is busy it is "The assistant is busy right now".

## Live checks, 4 Nov 2026

Real key, local API without Aspire.

| Check | Model | Result |
|---|---|---|
| Visa and weather for Rome, 20–24 Nov, LB passport | 3.5-flash-lite | ✅ Called `get_overview`; "visa required", highs 10.2–14.7 °C, lows 2.2–11.2 °C, rain: identical to `/overview`. One "About Rome" card. 38 s (Gemini overloaded) |
| Flights to Lisbon in mid-January, no departure city | 3.5-flash-lite | ❌ then ✅. First run guessed Beirut from the passport. After the rule "never guess the departure city" (2.5-flash): asks where they fly from, 2 s |
| "From Paris" as the next message | 3.5-flash-lite | ✅ Found Paris, searched, "from $118"; flights card with Paris as origin |
| Well-rated hotel under $700, Rome 4–8 Dec, 2 adults | 2.5-flash | ✅ Called `search_hotels`; 5 hotels with scores and totals identical to `/hotels` for the same search. One hotels card. 7.5 s |
| Gemini busy | 3.8-flash | ✅ Real 503 "high demand" and one "Deadline expired" (504) → busy warning, HTTP 200 |

**Availability on 4 Nov**: `gemini-3.8-flash` returned 503 "high demand" all afternoon; `gemini-2.5-flash` answered in under 1 s once its own spike passed; `gemini-3.5-flash-lite` answered but took 11–22 s. The model is a setting (`GEMINI_MODEL`), so it can be switched without a release.

Still to run before the sprint review: the 10 scripted questions of SC-001/SC-002 on one model, and SC-004 timings on a quiet day.
