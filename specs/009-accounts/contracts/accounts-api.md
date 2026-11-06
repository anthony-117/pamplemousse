# API Contract: Accounts

## Every request

Every route under `/api/v1` now needs a header:

```http
Authorization: Bearer <Firebase ID token>
```

Open without it: `GET /api/v1/system/ping`, `/alive`, `/health` (Development), and in Development `/openapi/*`
and `/scalar`.

This is a breaking change for every client. The app adds the header in the same sprint (constitution VI); there is
no other client yet.

### Refusals (Problem Details)

| Status | `code` | When | App does |
|---|---|---|---|
| 401 | `sign-in-required` | no token, expired, forged, another project, or a deleted account | renews the token once and retries; if that fails, signs out to the welcome screen |
| 403 | `email-not-confirmed` | `email_verified` is not true | shows "Check your email" |
| 401 | `recent-sign-in-required` | `DELETE /me` with a sign-in older than 5 minutes | asks the traveller to sign in again, then retries |
| 503 | `sign-in-not-configured` | `FIREBASE_PROJECT_ID` is not set | shows the error |

```json
{
  "title": "Sign in again.",
  "status": 401,
  "code": "sign-in-required"
}
```

The title never says which check failed (FR-007). 401 answers also carry `WWW-Authenticate: Bearer`.

## `GET /api/v1/me`

The signed-in traveller's profile. Created on the first call from the sign-in's name and email and the defaults
(FR-010).

```json
{
  "displayName": "Fares Haddad",
  "email": "fares@example.com",
  "passport": "LB",
  "homeCurrency": "USD",
  "homeCity": { "id": "276781", "name": "Beirut", "countryCode": "LB" },
  "preferences": { "budget": 90, "culture": 90, "food": 50, "nature": 50, "nightlife": 20, "comfort": 50 },
  "createdAt": "2026-11-10T09:12:00Z",
  "updatedAt": "2026-11-10T09:15:30Z"
}
```

`homeCity` is `null` when not set.

## `PUT /api/v1/me`

Replaces the editable fields. Every field is required, so the app sends the whole form.

```json
{
  "displayName": "Fares Haddad",
  "passport": "LB",
  "homeCurrency": "USD",
  "homeCityId": "276781",
  "preferences": { "budget": 90, "culture": 90, "food": 50, "nature": 50, "nightlife": 20, "comfort": 50 }
}
```

- `200` with the profile, as `GET`.
- `400` naming the field: `displayName` empty or over 60, unknown `passport`, `homeCurrency` or `homeCityId`, a
  preference outside 0–100 or missing.
- `email` in the body is ignored.

## `DELETE /api/v1/me`

Deletes the traveller's plans, profile and Firebase account (FR-014).

- `204` when done, including when part of it was already gone.
- `401 recent-sign-in-required` when the sign-in is older than 5 minutes.
- `503` when Firestore or Firebase can't be reached; nothing is reported as deleted, and a retry finishes the job.

After `204`, the same token is refused with `401 sign-in-required`.
