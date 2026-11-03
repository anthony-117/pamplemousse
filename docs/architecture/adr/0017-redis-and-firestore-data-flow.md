# 0017 — Search data flow: Redis, then Firestore, then the API
Date: 2026-11-03 · Status: Accepted · Settles the database part left open in ADR 0015

## Context
The Booking COM API (ADR 0016) has a hard monthly request limit and takes 2–30 s per search. The same searches repeat, and some data never changes between searches (a city's IDs, a hotel's name and photos). The owner wants the database on Firebase and wants to keep price history.

## Decision
A search goes through three layers, and only reaches the API when the first two can't answer:

```
App → Backend → Redis ──miss──► Firestore ──miss──► Booking COM API
```

1. **Redis:** answers a recent copy of the same search (same place, dates and travellers) in milliseconds.
2. **Firestore:** gives what doesn't change between searches: city IDs and hotel details.
3. **Booking COM API:** only then is it called. The result is saved to Redis and the lasting parts to Firestore.

### Redis (short-lived)
Redis runs in Docker next to the backend on the Oracle server, and through Aspire locally. It replaces the in-memory `IDistributedCache` behind `JsonCache`.

| Key | Holds | Kept for |
|---|---|---|
| `flights:{search}` | Flight results | 6 hours |
| `hotels:{search}` | Hotel and rental results | 12 hours |
| `cars:{search}` | Car rental results | 1 day |
| `attractions:{search}` | Attraction results | 1 day |
| `lock:{search}` | One API call at a time for the same search; others wait for its result | Seconds |
| `quota:{yyyy-MM}` | This month's API requests | The month |

### Firestore (lasting)
Firestore's free plan needs no billing card: 50,000 reads, 20,000 writes and 20,000 deletes a day, 1 GiB stored.

| Collection | Holds | Refreshed |
|---|---|---|
| `cities/{geonameId}` | The city's Booking.com destination ID, flight code, attraction and car location IDs | When missing; checked monthly |
| `hotels/{bookingHotelId}` | Name, address, stars, photos, position, Booking.com link | Weekly |
| `priceSnapshots/{id}` | Price history: one document per API search, with the search, its time and each result's ID and price | Never changed; kept |

Saved plans, users and preferences also go in Firestore, defined by their own specs.

### Price history
Every API search writes one `priceSnapshots` document, so history costs one write per call, not one per hotel. It is used for:
- "price went up or down" on a saved plan;
- showing the last known prices, with their time, when the API fails or the month's requests run out.

### Request limit
- Each API call adds 1 to `quota:{yyyy-MM}`.
- Above 95% of the plan's limit, the backend stops calling the API. It shows the last snapshot from Firestore, labelled with its time, or booking links if there is none.

### Layers
Redis and Firestore are used only in Infrastructure, behind Application interfaces (ADR 0012). Use cases never see them.

## Still open
Background refresh of popular cities (Rome, Paris, Beirut) is not done for now, because it spends requests when nobody searches. It can be added if the Pro plan is approved.

## Consequences
- Repeat searches are instant and free; each search costs at most one API request per time window.
- Shown prices can be up to 6 hours old for flights, 12 for hotels and 1 day for cars and attractions. The app shows when prices were fetched.
- Two more services to run: Redis on the server, Firestore with a service account (`GOOGLE_APPLICATION_CREDENTIALS`, `FIREBASE_PROJECT_ID`). Tests use the Firestore emulator and a Redis container.
- Price history grows by one document per API search. At Pro's full 40,500 requests, about 2 KB each, that is about 80 MB a month, so the free 1 GiB lasts about a year; old snapshots can be thinned out before then.
