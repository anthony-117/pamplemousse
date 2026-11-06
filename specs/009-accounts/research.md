# Research: Accounts

Decided on 2026-11-06 from the Firebase and ASP.NET Core documentation. No paid service and no request to any
travel source.

## R-1 · Checking a Firebase sign-in in the API

**Question**: How does the API check that a request carries a valid sign-in for project `travelgenie-1fdd1`?

**Finding**: A Firebase ID token is a standard JWT signed by Google:
- issuer `https://securetoken.google.com/{projectId}`, audience `{projectId}`;
- expires 1 hour after it is issued; the Firebase SDK in the app renews it on its own;
- the signing keys are published through the issuer's OpenID discovery document;
- claims used here: `sub` (the user id), `email`, `email_verified`, `name`, `auth_time`.

ASP.NET Core's JWT bearer handler (`Microsoft.AspNetCore.Authentication.JwtBearer`, Microsoft, free) checks all
of this with `Authority = https://securetoken.google.com/{projectId}`. It downloads Google's keys once, keeps them,
and refreshes them when an unknown key id appears. Each request is then checked locally, with no network call.

**Decision**: JWT bearer with the Firebase authority. Checks: signature, issuer, audience, lifetime (1 minute of
clock skew), and a non-empty `sub`. The Firebase Admin SDK is not used for checking, since it would add nothing
here and its revocation check costs a network call per request.

**If the keys can't be downloaded** (first start offline), the handler fails the check, so the request gets 401.
It is never let through (spec Edge Cases).

## R-2 · Confirmed email

**Finding**: `email_verified` is `true` for Google accounts, and for email accounts once the link was opened. The
app must renew its token after the link is opened (`user.reload()` then `getIdToken(true)`), or the old token still
says `false`.

**Decision**: An authorization requirement reads `email_verified`. When it is missing or false, the answer is 403
with `code: "email-not-confirmed"`. The app's "I've confirmed it" button renews the token, then retries.

## R-3 · A recent sign-in before deleting

**Finding**: Firebase's own client `delete()` needs a sign-in from the last 5 minutes. The API deletes the
account with the Admin SDK, which has no such rule, so the API checks it itself.

**Decision**: `DELETE /me` needs `auth_time` within the last 5 minutes. Otherwise it answers 401 with
`code: "recent-sign-in-required"`, and the app asks the traveller to sign in again (Google or password), then
retries.

## R-4 · Deleting the Firebase account

**Options**:

| Option | For | Against |
|---|---|---|
| A. The app calls `user.delete()` after the API deletes the data | No new package in the API | If the app is closed between the two steps, the sign-in stays with no data. A second client (the Telegram bot) would repeat the logic |
| B. The API deletes the data, then the account, with the Firebase Admin SDK | One request does everything; the order is in one place | One new package (`FirebaseAdmin`, Google, free) |

**Decision**: B. The Admin SDK uses the same service account as Firestore (`GOOGLE_APPLICATION_CREDENTIALS`).
The `firebase-adminsdk` service account has the Firebase Authentication Admin role by default. This is checked
live in the quickstart; if the key file belongs to another account, the owner grants the role in the console.

**Order**: delete the plans, then the profile, then the Firebase account. A failure stops there and answers 503,
so a retry finishes the job. Deleting what is already gone counts as done.

## R-5 · A deleted or disabled account that still holds a token

**Finding**: A token stays valid until it expires, up to 1 hour, even after the account is deleted or disabled.
The only way to refuse it sooner is the Admin SDK's revocation check, which is a network call per request.

**Decision**:
- **Deleted through the API**: its user id is added to a "deleted" list in the search cache (Redis, or memory) for
  1 hour. The list is checked on every request, and a match is refused with 401.
- **Disabled in the Firebase console**: refused when its token expires, within 1 hour at most. Disabling an
  account is rare and done by the owner.

This is a spec change (Edge Cases) and needs your OK with this plan.

## R-6 · When `FIREBASE_PROJECT_ID` is not set

**Decision**: The API starts as usual (constitution IV). Every endpoint that needs a sign-in answers 503 with
`code: "sign-in-not-configured"` and a message naming the variable. It never lets a request through. Health, ping
and, in Development, the API reference page stay open.

Firestore already uses the same variable (ADR 0017), so one setting turns both on.

## R-7 · Where the profile lives

**Decision**: Firestore `travellers/{uid}`, one document per traveller. Spec 008's plans go in
`travellers/{uid}/plans/{planId}`, so deleting a traveller is one sub-collection and one document. With no
Firestore project, profiles live in memory, like the hotel data.

The app never reads Firestore directly; everything goes through the API. So no Firestore security rules change.

## R-8 · Testing without the internet

**Decision**: The tests host the API with the JWT handler pointed at a test issuer and an RSA key made at test
start. A small helper signs tokens with any claims (expired, wrong audience, wrong issuer, wrong key, no
`email_verified`, an old `auth_time`). The Admin SDK sits behind `IAccountDeleter` and is replaced by a fake.
No test reaches Google.

## R-9 · Values in the profile

| Field | Rule | Source of truth |
|---|---|---|
| Display name | 1 to 60 characters after trimming | first value from the token's `name`, else the part of the email before `@` |
| Passport | a country code in the reference data | `countries.json` (Sprint 1) |
| Home currency | a currency of a country in the reference data | the same file |
| Home city | optional; a destination id in the reference data | `cities.tsv` (Sprint 1) |
| Preferences | six whole numbers, 0 to 100 | `Weights` of spec 007 |

Defaults on first sign-in: passport `LB`, currency `USD`, no home city, every preference 50 (the app's Medium).
