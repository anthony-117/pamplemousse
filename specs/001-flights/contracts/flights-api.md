# API Contract: Search flights

```http
GET /api/v1/destinations/{id}/flights
```

`{id}` is the destination's TravelGenie id.

## Query parameters

| Name | Required | Default | Rule |
|---|---|---|---|
| `origin` | **Yes** | | TravelGenie id of the departure city, from city search |
| `from` | No | Today + 30 days | Same date rules as the overview |
| `to` | No | `from` + 4 days | Ignored when `tripType=oneWay` |
| `tripType` | No | `roundTrip` | `roundTrip` or `oneWay` |
| `directOnly` | No | `false` | |
| `currency` | No | `USD` | 3 letters |
| `limit` | No | 20 | 1 to 50 |

## 200 OK

Also returned when the source is down or not configured: then `items` is empty and `warnings` says why.

```json
{
  "origin": { "id": "2988507", "name": "Paris", "flightCity": "Paris", "code": "PAR", "isNearby": false, "distanceKm": 2 },
  "destination": { "id": "3169070", "name": "Rome", "flightCity": "Rome", "code": "ROM", "isNearby": false, "distanceKm": 1 },
  "dates": { "from": "2027-04-10", "to": "2027-04-14" },
  "tripType": "roundTrip",
  "directOnly": false,
  "currency": "USD",
  "items": [
    {
      "id": "tp:3f9a1c",
      "airline": "ITA Airways",
      "flightNumber": "AZ319",
      "originAirport": "ORY",
      "destinationAirport": "FCO",
      "departureAt": "2027-04-10T09:25:00+02:00",
      "returnAt": "2027-04-14T18:10:00+02:00",
      "stops": 0,
      "returnStops": 0,
      "durationMinutes": 130,
      "price": { "amount": 142.00, "currency": "USD" },
      "bookingUrl": "https://www.aviasales.com/...&marker=12345",
      "dateMatch": "exact",
      "source": "Travelpayouts"
    }
  ],
  "priceNote": "Recent prices found by other travellers. The live price is on the booking site.",
  "message": null,
  "searchUrl": null,
  "warnings": []
}
```

Rules:
- `items` is ordered cheapest first and holds one level only, the first that has fares: `exact`, then `nearby` (within 3 days), then `sameMonth`, then, for round trips, `anyReturn` (departs in the requested month, returns on any date). Every item has a `dateMatch`.
- When a level other than `exact` is used, `message` says so.
- When no level has fares, `items` is empty, and `message` and `searchUrl` are set.
- When a city has no airport of its own, its `isNearby` is `true`, and `flightCity` and `distanceKm` say what was used.
- When a city has no flight city within range, `items` is empty, and `message` says so.

The example's values are illustrative. Field meanings come from the real API (research R-7); the marker's place in the link is still to be confirmed.

## Errors (Problem Details)

| Status | When | `errors` field |
|---|---|---|
| 400 | Missing `origin` | `Origin` |
| 400 | Origin and destination are the same flight city | `origin` |
| 400 | Invalid dates | `to` |
| 400 | Bad `tripType`, `currency` or `limit` value, or a currency the source does not know | That field |
| 404 | Unknown `{id}` or `origin` | |

## Warnings

| Situation | Warning |
|---|---|
| Token missing | `Travelpayouts is not configured. Set TRAVELPAYOUTS_API_TOKEN in the environment or .env file.` |
| Source failed or timed out | `Flights is unavailable right now.` |

Existing endpoints do not change.
