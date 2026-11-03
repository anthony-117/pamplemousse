# API Contract: Hotels

## Search hotels

```http
GET /api/v1/destinations/{id}/hotels
```

`{id}` is the destination's TravelGenie id.

### Query parameters

| Name | Required | Default | Rule |
|---|---|---|---|
| `from` | No | Today + 30 days | Check-in; same date rules as flights |
| `to` | No | `from` + 4 days | Check-out; after `from`, at most 30 nights |
| `adults` | No | 2 | 1 to 8 |
| `currency` | No | `USD` | 3 letters |
| `sort` | No | `recommended` | `recommended`, `price`, `review`, `stars`, `distance` |
| `minStars` | No | | 1 to 5 |
| `minReview` | No | | 0 to 10 |
| `minPrice`, `maxPrice` | No | | Total for the stay, ≥ 0, `minPrice` ≤ `maxPrice` |
| `stayType` | No | | Repeatable, e.g. `Hotels`, `Apartments` (needs details) |
| `freeCancellation` | No | `false` | Needs details |
| `page` | No | 1 | ≥ 1, 20 per page |

### 200 OK

Also returned when the source is down, has no key, or the monthly limit is nearly used: then the last saved prices
are returned with their time, or `items` is empty, and `warnings` says why.

```json
{
  "destination": { "id": "3169070", "name": "Rome", "sourceName": "Rome" },
  "dates": { "from": "2027-04-10", "to": "2027-04-14", "nights": 4 },
  "adults": 2,
  "currency": "USD",
  "sort": "recommended",
  "page": 1,
  "totalItems": 20,
  "totalPages": 12,
  "pricesFetchedAt": "2027-03-01T09:12:00Z",
  "isSavedPrice": false,
  "items": [
    {
      "id": "bk:558347",
      "name": "Hotel Example Roma",
      "photoUrl": "https://cf.bstatic.com/xdata/images/hotel/square500/....jpg",
      "stars": 4,
      "reviewScore": 8.6,
      "reviewCount": 1520,
      "location": { "lat": 41.9, "lng": 12.49 },
      "distanceKm": 1.2,
      "nearCity": false,
      "stayType": null,
      "freeCancellation": null,
      "price": { "total": 812.40, "perNight": 203.10, "currency": "USD" },
      "source": "Booking.com"
    }
  ],
  "filtersCheckedOn": null,
  "message": null,
  "searchUrl": null,
  "warnings": []
}
```

Rules:
- `price.total` includes taxes and fees; it is for a room that fits `adults`.
- Empty fields (`stars`, `reviewScore`, `stayType`, `freeCancellation`) are `null`, never `0` or `false`.
- `filtersCheckedOn`: when `stayType` or `freeCancellation` is used, the number of hotels whose details were available to check.
- `totalItems` counts the matching hotels on the pages fetched so far; `totalPages` is how many pages the source has for the search (at least `page`). Offer a next page while `page` < `totalPages`.
- `isSavedPrice` is `true` when the prices come from a saved snapshot instead of a fresh call.
- When nothing matches, `items` is empty, and `message` and `searchUrl` (the Booking.com search for the city and dates) are set.
- When the source's city differs from the destination, `sourceName` says which was used.

### Errors (Problem Details)

| Status | When | `errors` field |
|---|---|---|
| 400 | Invalid dates | `to` |
| 400 | `adults` outside 1–8 | `adults` |
| 400 | Bad `currency`, `sort`, `minStars`, `minReview`, price range or `page` | That field |
| 404 | Unknown `{id}` | |

### Warnings

| Situation | Warning |
|---|---|
| `RAPIDAPI_KEY` not set | `Hotels are not configured (RAPIDAPI_KEY).` |
| Source down, rate-limited or broken answer | `Hotel prices could not be refreshed; showing prices from {time}.` (or no prices saved) |
| Monthly limit nearly used | `The monthly hotel request limit is nearly used; showing prices from {time}.` |
| Firestore not configured | `Hotel data is not being saved (FIREBASE_PROJECT_ID).` (logged at startup; not shown per request) |

## Open a hotel

```http
GET /api/v1/hotels/{hotelId}?from=&to=&adults=&currency=
```

`{hotelId}` is an item `id` from the search (`bk:558347`). Same rules and defaults for `from`, `to`, `adults` and `currency`.

### 200 OK

```json
{
  "id": "bk:558347",
  "name": "Hotel Example Roma",
  "photoUrls": ["https://cf.bstatic.com/xdata/images/hotel/square500/....jpg"],
  "stars": 4,
  "starsEstimated": false,
  "reviewScore": 8.6,
  "reviewCount": 1520,
  "reviewWord": "Excellent",
  "location": { "lat": 41.9, "lng": 12.49 },
  "address": "Via Example 1, 00184 Rome",
  "district": "Esquilino",
  "distanceToCentreKm": 1.2,
  "stayType": "Hotels",
  "freeCancellation": true,
  "breakfastIncluded": false,
  "checkInFrom": "14:00",
  "checkInUntil": "23:00",
  "checkOutFrom": "07:00",
  "checkOutUntil": "11:00",
  "facilities": ["Free WiFi", "Air conditioning"],
  "languages": ["en-gb", "it"],
  "price": { "total": 812.40, "perNight": 203.10, "currency": "USD" },
  "priceFetchedAt": "2027-03-01T09:12:00Z",
  "bookingUrl": "https://www.booking.com/hotel/it/example.html?checkin=2027-04-10&checkout=2027-04-14&group_adults=2&no_rooms=1&group_children=0&selected_currency=USD",
  "detailsFetchedAt": "2027-03-01T09:15:00Z",
  "source": "Booking.com",
  "warnings": []
}
```

`price` is the last search price, and only when it was for the same dates, adults and currency; otherwise `null`.
`freeCancellation` is for a room that fits the adults, on the dates the details were last fetched.

When details can't be fetched (source down, no key, limit), the stored fields are returned, `bookingUrl` falls
back to the Booking.com search for the city and dates, and `warnings` says why.

### Errors

| Status | When |
|---|---|
| 400 | Invalid dates, `adults` or `currency` (field named) |
| 404 | Hotel never seen in a search and not found at the source |

The example's values are illustrative; field meanings come from the real API (research R-2 to R-5).
