# Sprint 4 review — in progress

Started 6 Nov 2026. Built with Spec Kit: [accounts](../../../specs/009-accounts/) (TG-17), then the
[itinerary](../../../specs/008-itinerary/) (TG-10). The Figma design is approved (page "Sprint 4 — Accounts &
itinerary", 6 Nov 2026). Decisions in [ADR 0019](../../architecture/adr/0019-firebase-sign-in.md).

## Delivered: accounts (backend)
Every route now needs a Firebase sign-in from a confirmed account, checked locally by the JWT bearer handler with
Google's published keys. Ping, health and the API reference stay open. A refusal says only "sign in again" or
"confirm your email", with a `code` the app acts on. Without `FIREBASE_PROJECT_ID`, protected routes answer 503 and
nothing is let through.

`GET`, `PUT` and `DELETE /api/v1/me` keep the travel profile in Firestore `travellers/{uid}`: name, email, passport,
home currency, home city and the six default preferences. Deleting needs a sign-in from the last 5 minutes. It removes
the profile, then the Firebase account (Admin SDK), and refuses the old token for the hour it could still live.

| Story | Result |
|---|---|
| TG-17 Sign in to use the app | ✅ API side: every route checked; ⏳ app screens |
| TG-17 Create an account with email | ✅ API refuses unconfirmed emails; ⏳ app screens |
| TG-17 Keep my travel profile | ✅ API; ⏳ app |
| TG-17 Delete my account | ✅ API; ⏳ app, and a live check with a real account |

## Not done yet
- The app's sign-in, Profile and delete screens (`chocomousse`, from the approved Figma page). The API and the app are
  released together, since the API now refuses requests without a sign-in.
- Live checks that need a real account (quickstart 3–11): run by the owner.
- The itinerary (TG-10), next.
