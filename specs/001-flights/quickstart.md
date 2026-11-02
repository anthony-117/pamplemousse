# Quickstart: Checking flights by hand

Automated tests never call the internet, so these live checks prove the feature against the real source (constitution, principle II). They need `TRAVELPAYOUTS_API_TOKEN` and `TRAVELPAYOUTS_MARKER` in `.env`.

## Run

```bash
dotnet run --project src/Pamplemousse.Api --launch-profile http
```

## Basic request

Paris (`2988507`) to Rome (`3169070`), with dates 1–3 months ahead:

```bash
curl "http://localhost:5064/api/v1/destinations/3169070/flights?origin=2988507&from=2027-02-10&to=2027-02-14"
```

Expected: flights cheapest first, a `priceNote`, and working `bookingUrl` values.

## Live checks against the success criteria

Use these 10 routes, with dates 1–3 months ahead: Paris–Rome, Beirut–Paris, London–New York, Beirut–Dubai, Madrid–Rome, Berlin–Istanbul, Rome–Athens, Beirut–Istanbul, Paris–Tokyo, Dubai–London.

| Criterion | Check | Pass when |
|---|---|---|
| SC-001 | Request each route | At least 8 of 10 return a flight |
| SC-002 | Inspect the results | All have price, link and note; nearby dates are labelled |
| SC-003 | Repeat a request, timing it | Under 1 second |
| SC-004 | Time first requests | Under 5 seconds, or a warning |
| SC-005 | Remove the token and restart | Still 200, with a warning naming the variable |
| SC-006 | Open 10 links | Right route and dates, and marker present |
| SC-007 | Compare the first price with the booking site | Within 25% on at least 7 of 10 |

Record the results in the sprint review.

## Other cases

```bash
curl "http://localhost:5064/api/v1/destinations/3169070/flights"
curl "http://localhost:5064/api/v1/destinations/3169070/flights?origin=3169070"
curl "http://localhost:5064/api/v1/destinations/3169070/flights?origin=2988507&tripType=oneWay"
curl "http://localhost:5064/api/v1/destinations/3169070/flights?origin=2988507&directOnly=true"
```

Expected, in order: 400 naming the origin; 400 for the same city; flights with no return; flights with no stops.
