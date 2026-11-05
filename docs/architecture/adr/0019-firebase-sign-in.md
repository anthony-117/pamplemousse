# 0019 — Sign-in: Firebase ID tokens checked by the JWT bearer handler
Date: 2026-11-06 · Status: Accepted · Builds on ADR 0017 · Spec 009 (TG-17)

## Context
The owner decided that signing in is required before using TravelGenie (spec 009), with Firebase Authentication
(email and password, and Google) on project `travelgenie-1fdd1`. The API must refuse every request without a valid
sign-in, know the traveller by their Firebase user id, and delete an account with everything stored for it.

## Decision
- **Checking**: ASP.NET Core's JWT bearer handler (`Microsoft.AspNetCore.Authentication.JwtBearer`) with the
  authority `https://securetoken.google.com/{FIREBASE_PROJECT_ID}`. It checks signature, issuer, audience and expiry
  locally with Google's published keys, which it downloads once and refreshes on its own. No network call per
  request.
- **Everything is protected by default**: a fallback policy needs a signed-in traveller with a confirmed email.
  Open routes are marked `[AllowAnonymous]`: ping, health and, in Development, the API reference.
- **Refusals** are Problem Details with a `code` the app acts on: `sign-in-required`, `email-not-confirmed`,
  `recent-sign-in-required` or `sign-in-not-configured`.
- **Deleting an account**: the Firebase Admin SDK (`FirebaseAdmin`), used for that one call, with the service
  account Firestore already uses. The API asks for a sign-in from the last 5 minutes first.
- **Deleted accounts** are put on a list in the search cache for an hour, the longest a token lives, so their token
  is refused at once. Accounts disabled in the console are refused when their token expires.
- **No project set**: the API starts, and every protected route answers 503 naming `FIREBASE_PROJECT_ID`.
- Handlers get the traveller as an argument (`SignedInTraveller`), never from HTTP, so other clients (the Telegram
  bot, TG-16) can call them with their own way of knowing who is asking.

## Consequences
- Every client must send `Authorization: Bearer <token>`; the app and the API are released together.
- No secret is added: the project id is public and the key file is the Firestore one.
- A disabled account keeps working for up to an hour. Checking revocation on every request would cost a call to
  Google each time.
- The assistant's rate limits stay per IP address for now; per-traveller limits can follow.
