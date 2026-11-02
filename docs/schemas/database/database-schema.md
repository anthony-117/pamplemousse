# Database Schema (draft v0.1 — 2026-10-26)

Postgres 17 + pgvector (Neon/Supabase in the cloud, Docker via Aspire locally). Managed with EF Core migrations in `Pamplemousse.Infrastructure/Persistence`.

```mermaid
erDiagram
    countries ||--o{ destinations : has
    countries ||--o{ visa_requirements : "destination of"
    countries ||--o| advisories : has
    destinations ||--o{ places : contains
    users ||--o| user_preferences : has
    users ||--o{ trips : plans
    destinations ||--o{ trips : "target of"
    trips ||--o{ itineraries : has
    itineraries ||--o{ itinerary_days : has
    itinerary_days ||--o{ itinerary_items : has
    users ||--o{ chat_sessions : has
    chat_sessions ||--o{ chat_messages : has
    trips ||--o| trip_embeddings : "embedded as"

    countries {
      char2 code PK
      text name
      text capital
      jsonb currencies
      jsonb languages
      text calling_code
      text flag_url
    }
    destinations {
      uuid id PK
      text name
      char2 country_code FK
      float lat
      float lng
      text time_zone
      int population
      text geonames_id
    }
    places {
      uuid id PK
      uuid destination_id FK
      text provider
      text provider_id
      text category
      text name
      float lat
      float lng
      float rating
      int rating_count
      int price_level
      text photo_url
      text description
      timestamptz refreshed_at
    }
    visa_requirements {
      char2 passport PK
      char2 destination PK
      text requirement
      int visa_free_days
      date source_date
    }
    advisories {
      char2 country_code PK
      int level
      text summary
      text source_url
      timestamptz updated_at
    }
    users {
      uuid id PK
      text email
      text display_name
      char2 passport_country
      char3 home_currency
      timestamptz created_at
    }
    user_preferences {
      uuid user_id PK
      jsonb weights
      jsonb interests
    }
    trips {
      uuid id PK
      uuid user_id FK
      uuid destination_id FK
      date start_date
      date end_date
      int travellers
      numeric budget_amount
      char3 budget_currency
      jsonb weights
      text status
      timestamptz created_at
    }
    itineraries {
      uuid id PK
      uuid trip_id FK
      int version
      float match_score
      jsonb score_breakdown
      timestamptz created_at
    }
    itinerary_days {
      uuid id PK
      uuid itinerary_id FK
      int day_number
      date date
    }
    itinerary_items {
      uuid id PK
      uuid day_id FK
      text type
      text ref_provider
      text ref_id
      jsonb snapshot
      time start_time
      time end_time
      numeric cost_amount
      char3 cost_currency
      float score
      int position
    }
    chat_sessions {
      uuid id PK
      uuid user_id FK
      uuid trip_id FK
      timestamptz created_at
    }
    chat_messages {
      uuid id PK
      uuid session_id FK
      text role
      text content
      jsonb tool_calls
      timestamptz created_at
    }
    trip_embeddings {
      uuid trip_id PK
      vector embedding
      text summary
      timestamptz updated_at
    }
```

## Notes
- **Sprint 1 status:** countries, cities and visa rules are bundled files searched in memory ([ADR 0008](../../architecture/adr/0008-sprint-1-config-and-reference-data.md)); these tables are created when saved trips arrive (Sprint 3–4).
- **Seeded reference data:** `countries` (mledoze/countries), `destinations` (GeoNames `cities15000` — free, CC BY, gives free city search without Google), `visa_requirements` (Passport Index), `advisories` (US State Dept, refreshed daily).
- **Cached, not stored:** weather, exchange rates, holidays, events and all price offers go to Redis.
- `places` stores provider data with `refreshed_at`; Google Places terms limit how long some fields may be stored — keep only IDs + refresh, check terms before storing photos/ratings long term.
- `itinerary_items.snapshot` keeps a copy of the chosen offer at save time (prices change).
- `trip_embeddings.embedding`: `vector(768)` — Gemini embeddings truncated to 768 dimensions (configurable).
