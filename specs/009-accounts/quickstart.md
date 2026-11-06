# Quickstart: Accounts

Run the API (`pamplemousse-api`, port 5064) with `FIREBASE_PROJECT_ID` and `GOOGLE_APPLICATION_CREDENTIALS` set.

The owner runs the checks that need a sign-in, since they use real accounts: two test accounts made in the
Firebase console (one confirmed, one not). A token comes from the Firebase Auth REST sign-in, or from the app once
its sign-in screens exist. Tokens stay in the owner's shell, never in a file or a log. Checks 1 and 12 need no
sign-in. No travel source is called, so this costs nothing.

| # | Check | Expect |
|---|---|---|
| 1 | `GET /api/v1/system/ping` without a token | `200` |
| 2 | `GET /api/v1/countries` without a token (SC-002) | `401 sign-in-required` |
| 3 | Same with a token edited by one character (SC-002) | `401` |
| 4 | Same with the unconfirmed account's token (SC-002) | `403 email-not-confirmed` |
| 5 | Same with the confirmed account's token | `200` |
| 6 | `GET /me` with the confirmed account | profile with the defaults and the account's email |
| 7 | `PUT /me` with passport `LB`, currency `USD`, home city Beirut, budget 90 | `200`; `GET /me` returns them |
| 8 | `PUT /me` with passport `XX` | `400` naming `passport` |
| 9 | `GET /me` with the second account after confirming it (SC-006) | its own profile, not the first one's |
| 10 | Wait an hour, or use a token older than 5 minutes: `DELETE /me` | `401 recent-sign-in-required` |
| 11 | Fresh sign-in, `DELETE /me` (SC-005) | `204`; the Firestore document is gone; the account is gone from the console; the old token gets `401` |
| 12 | API started without `FIREBASE_PROJECT_ID`: `GET /api/v1/countries` | `503 sign-in-not-configured`; ping still `200` |
| 13 | API logs after 1–11 | no email, name, user id or token in them |
