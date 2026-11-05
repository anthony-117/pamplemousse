# API Contract: Cars

## Pick-up places

```http
GET /api/v1/destinations/{id}/car-places
```

### 200 OK

```json
{
  "places": [
    { "id": "centre", "kind": "centre", "name": "City centre", "iata": null, "location": { "lat": 41.8919, "lng": 12.5113 } },
    { "id": "FCO", "kind": "airport", "name": "Fiumicino Airport", "iata": "FCO", "location": { "lat": 41.7927, "lng": 12.2523 } },
    { "id": "CIA", "kind": "airport", "name": "Rome Ciampino Airport", "iata": "CIA", "location": { "lat": 41.7993, "lng": 12.591 } }
  ],
  "warnings": []
}
```

The centre is always first. When the airport look-up fails or has no key, only the centre is returned and
`warnings` says why. `404` for an unknown destination.

## Search cars

```http
GET /api/v1/destinations/{id}/cars
```

### Query parameters

| Name | Required | Default | Rule |
|---|---|---|---|
| `from` | No | Today + 30 days | Pick-up day, 10:00; same date rules as hotels |
| `to` | No | `from` + 4 days | Return day, 10:00 |
| `place` | No | `centre` | `centre` or an `id` from car-places |
| `driverAge` | No | 30 | 18 to 99 |
| `currency` | No | `USD` | 3 letters |
| `sort` | No | `recommended` | `recommended`, `price`, `rating` |
| `size` | No | | Repeatable, e.g. `Compact` |
| `gearbox` | No | | `manual` or `automatic` |
| `minSeats`, `minBags` | No | | 1 to 9 |
| `company` | No | | Repeatable, e.g. `Avis` |
| `pickUpType` | No | | Repeatable: `terminal`, `shuttle`, `rentalCentre`, `trainStation`, `downtown`, `other` |
| `maxPrice` | No | | Total for the rental, > 0 |
| `freeCancellation` | No | `false` | |
| `page` | No | 1 | ≥ 1, 20 per page |

### 200 OK

Also returned when the source is down, slow, has no key, or the monthly limit is nearly used: then the last saved
result is returned with its time, or `items` is empty, and `warnings` says why.

```json
{
  "destination": { "id": "3169070", "name": "Rome" },
  "place": { "id": "centre", "kind": "centre", "name": "City centre", "iata": null },
  "dates": { "from": "2026-12-04", "to": "2026-12-08", "days": 4, "pickUpTime": "10:00", "returnTime": "10:00" },
  "driverAge": 30,
  "currency": "USD",
  "sort": "recommended",
  "page": 1,
  "totalItems": 420,
  "totalPages": 21,
  "pricesFetchedAt": "2026-11-05T09:40:00Z",
  "isSavedPrice": false,
  "mayHaveMore": true,
  "items": [
    {
      "id": "bk:858416088",
      "model": "Fiat 600",
      "orSimilar": "or similar large car",
      "size": "Intermediate",
      "photoUrl": "https://cdn.rcstatic.com/images/car_images/new_images/fiat/600_lrg.png",
      "gearbox": "automatic",
      "seats": 5,
      "doors": 4,
      "bags": 3,
      "airCon": true,
      "mileage": "Unlimited km",
      "unlimitedMileage": true,
      "fuelPolicy": "Like for like",
      "company": { "name": "Budget", "logoUrl": "https://cdn.rcstatic.com/sp/images/suppliers/62_logo_200.png", "rating": 7.0, "reviewCount": 372 },
      "pickUp": { "name": "Rome - via Sardegna", "type": "downtown", "address": "38 A, Via Sardegna, Rome Center, Roma, Italy, 00187", "location": { "lat": 41.9095, "lng": 12.491223 }, "distanceKm": 0.9 },
      "price": { "total": 76.76, "perDay": 19.19, "currency": "USD", "payNow": true },
      "freeCancellation": true,
      "deposit": { "amount": 0, "currency": "EUR" },
      "damageExcess": { "amount": 3294, "currency": "EUR" },
      "sponsored": true,
      "bookingUrl": "https://cars.booking.com/search-results?…&coordinates=41.89…%2C12.48…&driversAge=30&puDay=4&puMonth=12&puYear=2026&puHour=10&puMinute=0&doDay=8&…",
      "source": "Booking.com"
    }
  ],
  "filterOptions": {
    "sizes": [{ "value": "Compact", "count": 120 }],
    "companies": [{ "value": "Avis", "count": 61 }],
    "pickUpTypes": [{ "value": "downtown", "count": 230 }]
  },
  "driverNote": null,
  "message": null,
  "searchUrl": null,
  "warnings": []
}
```

Rules:
- `price.total` is the normal Booking.com price for the whole rental, not a member discount.
- Empty fields (`photoUrl`, `bags`, `company.rating`, `deposit`, …) are `null`, never `0`.
- `deposit` and `damageExcess` keep the source's currency, which may differ from `currency`.
- `bookingUrl` opens Booking.com's car search for the same place, dates, times and driver age, not the car itself.
- `totalItems` counts the cars that match the filters; `filterOptions` counts the whole result, before filters.
- `mayHaveMore` is `true` when the source returned its maximum of 500 cars.
- `driverNote` is set for a driver under 25 or over 70: "The rental company may charge a young driver fee." /
  "… a senior driver fee."
- When nothing matches, `items` is empty, and `message` and `searchUrl` are set.

### Errors (Problem Details)

| Status | When | `errors` field |
|---|---|---|
| 400 | Bad or reversed dates, more than 30 days apart, in the past | `from` / `to` |
| 400 | Driver age outside 18–99 | `driverAge` |
| 400 | `place` not `centre` or one of the city's airports | `place` |
| 400 | Bad `currency`, `sort`, `gearbox`, `pickUpType`, `minSeats`, `minBags`, `maxPrice`, `page` | that field |
| 404 | Unknown destination | |
