# Data Model: Accounts

## Domain (`Pamplemousse.Domain/Travellers/`)

### `TravellerId`

A value object around the Firebase user id (`sub`): non-empty, at most 128 characters. Its `ToString()` gives
`traveller` and never the id, so it can't reach a log by mistake (FR-016).

### `TravelProfile`

| Field | Type | Rule |
|---|---|---|
| `Traveller` | `TravellerId` | set once |
| `DisplayName` | string | 1–60 characters, trimmed |
| `Email` | string | from the sign-in; never changed through the API |
| `Passport` | string | 2-letter country code, upper case, known to the reference data |
| `HomeCurrency` | string | 3-letter code, upper case, used by a known country |
| `HomeCityId` | string? | a known destination id, or null |
| `Preferences` | `Weights` | spec 007 |
| `CreatedAt`, `UpdatedAt` | DateTimeOffset | |

- `TravelProfile.New(traveller, name, email, now)` gives the defaults of research R-9.
- `profile.Update(...)` checks every field and throws `DomainException` naming the first bad one (`displayName`,
  `passport`, `homeCurrency`, `homeCityId`, `budget`, …).
- The reference data checks (known country, currency, city) are done by the handler with `IReferenceData`, which
  passes the results to `Update`, so Domain keeps no dependency.

## Application

```text
Abstractions/Accounts.cs
  ITravelProfileStore   GetAsync(TravellerId) · SaveAsync(TravelProfile) · DeleteAsync(TravellerId)
  IAccountDeleter       DeleteAsync(TravellerId)       // the Firebase account
  IDeletedTravellers    AddAsync(TravellerId) · ContainsAsync(TravellerId)   // R-5

Travellers/Queries/GetProfile/GetProfileHandler        // creates the profile on first call (FR-010)
Travellers/Commands/UpdateProfile/UpdateProfileHandler
Travellers/Commands/DeleteAccount/DeleteAccountHandler // plans → profile → account; then the deleted list
```

The signed-in traveller reaches a handler as a `SignedInTraveller(TravellerId Id, string Email, string? Name)`
argument, built by the controller from the token's claims (FR-008). Handlers never read HTTP.

Plans (spec 008) add `IPlanStore.DeleteAllAsync(TravellerId)`, which `DeleteAccountHandler` calls first. Until
then the handler deletes the profile and the account only.

## Firestore

```text
travellers/{uid}
  displayName   string
  email         string
  passport      string
  homeCurrency  string
  homeCityId    string | null
  preferences   map { budget, culture, food, nature, nightlife, comfort }   // ints
  createdAt     timestamp
  updatedAt     timestamp

travellers/{uid}/plans/{planId}   // spec 008
```

The document id is the Firebase user id. Field names are written out in `FirestoreDocuments`, as for hotels.

## Cache

| Key | Value | Time |
|---|---|---|
| `deleted-traveller:{sha256(uid)}` | `"1"` | 1 hour |

The user id is hashed in the key so a Redis listing shows no ids.
