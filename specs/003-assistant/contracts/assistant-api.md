# Contract: Assistant API

## `POST /api/v1/assistant/messages`

Answers the last traveller message. Stateless: send the recent conversation every time (at most 20 messages).

### Request

```json
{
  "messages": [
    { "role": "traveller", "text": "Do I need a visa?" },
    { "role": "assistant", "text": "With a Lebanese passport you need a Schengen visa for Italy." },
    { "role": "traveller", "text": "And a cheap hotel near the centre?" }
  ],
  "context": {
    "destinationId": "3169070",
    "from": "2027-04-10",
    "to": "2027-04-15",
    "adults": 2,
    "passport": "LB",
    "currency": "USD",
    "originId": "276781"
  }
}
```

| Field | Rules |
|---|---|
| `messages` | 1–20 items; the last one has role `traveller` |
| `messages[].role` | `traveller` or `assistant` (case-insensitive) |
| `messages[].text` | 1–1,000 characters after trimming |
| `context` | optional; every field optional |
| `context.destinationId` | a GeoNames city id; unknown → 404 |
| `context.from`, `context.to` | same rules as the other endpoints (trip ≤ 30 days, `to` after `from`) |
| `context.adults` | 1–8 |
| `context.passport` | ISO country code; default `LB` |
| `context.currency` | ISO currency; default `USD` |
| `context.originId` | departure city id for flights |

### Response `200`

```json
{
  "text": "Here are three well-rated hotels near the centre for 10–15 Apr: …",
  "cards": [
    {
      "kind": "hotels",
      "destinationId": "3169070",
      "title": "Hotels in Rome",
      "subtitle": "10 – 15 Apr · 2 adults",
      "from": "2027-04-10",
      "to": "2027-04-15",
      "adults": 2,
      "originId": null,
      "category": null
    }
  ],
  "warnings": []
}
```

| Field | Meaning |
|---|---|
| `text` | The assistant's reply; empty when it could not answer (see `warnings`) |
| `cards[].kind` | `overview`, `places`, `flights` or `hotels`; events use `overview` |
| `cards[].category` | `attraction` or `restaurant`, for `places` only |
| `cards[].originId` | departure city, for `flights` only |
| `warnings` | Sources that had no data, a missing key, or the assistant being busy |

Not configured:

```json
{ "text": "", "cards": [], "warnings": ["Assistant is not configured. Set GEMINI_API_KEY in the environment or .env file."] }
```

Busy (Gemini 429, 503 or time-out):

```json
{ "text": "", "cards": [], "warnings": ["The assistant is busy right now. Try again in a minute."] }
```

### Errors

`400` Problem Details naming the field (too many messages, empty or too long text, last message not from the
traveller, bad dates, adults out of range). `404` unknown `context.destinationId`.
