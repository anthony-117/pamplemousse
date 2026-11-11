# API Contract: Saved items

Hotels, places and events only (FR-020). Every route needs the sign-in of spec 009 and acts on the signed-in
traveller's items only.

## Save

`PUT /api/v1/saved/{kind}/{id}` (`kind`: `hotel`, `place` or `event`; `id` URL-encoded, e.g. `wd%3AQ10285`)

```json
{
  "destinationId": "3169070",
  "name": "Colosseum",
  "imageUrl": "https://upload.wikimedia.org/…",
  "lat": 41.89, "lng": 12.49,
  "item": { … the card's item … }
}
```

- `name` 1–200 characters; `destinationId` a known city (its name is filled in by the server).
- `item` is optional and at most 20 KB packed; it is what the Saved pane opens in Details.
- `204`. Saving it again changes nothing (it keeps its first `savedAt`).
- `400` naming the field, also for `flight` or `car` as `kind`.
- `409` `code: "too-many-saved"` with `max` at 200 items.

## Remove

`DELETE /api/v1/saved/{kind}/{id}` → `204`, also when it wasn't saved.

## List

`GET /api/v1/saved?kind=&destinationId=` → newest first:

```json
{
  "items": [
    { "kind": "place", "id": "wd:Q10285", "destinationId": "3169070", "destinationName": "Rome",
      "name": "Colosseum", "imageUrl": "…", "lat": 41.89, "lng": 12.49,
      "savedAt": "2026-11-11T10:00:00Z", "item": { … } }
  ],
  "count": 12,
  "max": 200
}
```

## Ids

`GET /api/v1/saved/ids` → `{ "hotel": ["bk:1"], "place": ["wd:Q10285"], "event": [] }`, so every card can show its
bookmark.
