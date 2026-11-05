# Quickstart: Car rentals

## Run it

1. `.env` needs `RAPIDAPI_KEY` (the same key as hotels).
2. `dotnet run --project src/Pamplemousse.AppHost` (Redis and Firestore as for hotels).
3. Ask:

```bash
curl -s "localhost:5064/api/v1/destinations/3169070/car-places"
curl -s "localhost:5064/api/v1/destinations/3169070/cars?from=2026-12-04&to=2026-12-08&sort=price&gearbox=automatic"
```

The first car search takes 20–30 s; the same search again, or with other sorts and filters, answers at once.

## Live checks (about 15 requests of the monthly 530)

| Check | Requests | Result |
|---|---|---|
| SC-001: Rome, Paris, Beirut and 7 more cities at the centre, 1–3 months ahead, ≥ 20 cars | 10 | |
| SC-001: the main airport of 3 of them, ≥ 20 cars | 3 + 3 look-ups | |
| SC-003: 10 cars against Booking.com, within 5% | 0 (reuses the above) | |
| SC-007: 10 links open the right place, dates and age, airports included (`ftsType=A`) | 0 | |
| SC-004/005: repeat and re-filter under 1 s; first search under 40 s | 0 | |
| Source down (wrong key): saved result with its time, or an empty list with a warning | 0 | |
