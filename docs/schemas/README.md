# Schemas

| Folder | Contents |
|---|---|
| `domain/` | Domain model / class diagrams: `Trip`, `Itinerary`, `DayPlan`, `Offer` (flight, hotel, car), `Place`, `Event`, `UserPreferences`, `Weights`, `Score` |
| `database/` | ERD, Postgres schema notes, pgvector tables (trip/preference embeddings) |
| `api/` | OpenAPI spec exported from `pamplemousse` (source of truth for the generated Dart client in `chocomousse`) |

The authoritative OpenAPI file and EF Core migrations live in the `pamplemousse` repo; copies or exports here are for the report.
