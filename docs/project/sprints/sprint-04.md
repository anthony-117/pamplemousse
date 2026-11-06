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

## Delivered: accounts (app)
`chocomousse` spec 006 builds the approved screens: Welcome (A1), Sign in (A2, and W5 on wide screens), Create your
account (A3), Check your email (A4), Reset your password (A5), Profile (P1), Edit profile (P2) and Delete your account
(P3). A router guard shows Welcome while signed out and returns to the address afterwards, never to another site.
Every API request carries the Firebase ID token; a refused token is renewed once, then the traveller is signed out.
New trips start from the travel profile (passport, currency, home city, preferences). Without the Firebase settings the
app still runs and Welcome says which one is missing. 250 app tests pass, 42 of them new.

| Story | Result |
|---|---|
| TG-17 Sign in to use the app | ✅ API and app (Google and email) |
| TG-17 Create an account with email | ✅ API and app; confirmation email from Firebase |
| TG-17 Keep my travel profile | ✅ API and app; seeds new trips |
| TG-17 Delete my account | ✅ API and app (signs in again when needed); ⏳ live check with a real account |

## Delivered: itinerary (backend)
`pamplemousse` spec 008 builds a day-by-day plan with `POST /api/v1/plans`: the best match of the ranked hotel and
flight lists (spec 007), then days of sights, parks, meals, nightlife, events and free time, timed around the flight,
grouped by area and with walking distances. Pace (Relaxed, Balanced, Packed) sets the places a day holds and is saved
in the travel profile. The plan shows its total in the trip's currency and its match, with the parts behind it. The
rules live in Domain (`PlanBuilder`), deterministic, with no outside dependency (ADR 0002 note).

Plans are saved to `travellers/{uid}/plans` with a copy of every offer they use, so opening one calls no source. The
traveller can list, rename, mark planned and delete them (50 at most), and swap the hotel or flight for another of the
top 20 kept with the plan, or add and remove a car; a swap calls no paid source. A failing source becomes a warning;
no places at all is a 422. Two new free OpenTripMap lists feed the parks and nightlife. 747 backend tests pass.

| Story | Result |
|---|---|
| TG-10 Build a day-by-day plan | ✅ API; ⏳ live checks with a real account |
| TG-10 Swap the hotel, flight or car | ✅ API |
| TG-10 My plans | ✅ API |

## Not done yet
- Firebase console setup by the owner: Anonymous off, authorised domains, the Android app and its SHA-1, email
  templates; then the app's `app.env`. The API and the app are released together, since the API now refuses requests
  without a sign-in.
- Live checks that need a real account (spec 009 quickstart 3–11, spec 008 quickstart 1–14): run by the owner.
- The itinerary screens in `chocomousse`: a separate spec, after a Sprint 4 Figma page is approved.
