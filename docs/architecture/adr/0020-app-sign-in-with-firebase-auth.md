# 0020 — The app signs in with Firebase Authentication
Date: 2026-11-06 · Status: Accepted · Builds on ADR 0019 · App spec `chocomousse/specs/006-accounts-screens`

## Context
The app's constitution (VI) allows Firebase for hosting only, unless an ADR says otherwise. Sign-in is now required
(spec 009), and the API checks Firebase ID tokens (ADR 0019), so the app must get those tokens from Firebase.

## Decision
- The app uses `firebase_core` and `firebase_auth` for email and password and Google sign-in, on the web and Android.
- Google sign-in goes through Firebase alone: `signInWithPopup` on the web, `signInWithProvider` on Android. No
  separate Google sign-in package.
- Firebase is used only to sign in and get ID tokens. All data still comes from the API (`/api/v1`); the app never
  reads or writes Firestore.
- Firebase's app settings (API key, app id, project id, sender id, auth domain) come from `assets/env/app.env` through
  `env.dart`. They are public identifiers, protected by the authorized domains and the Android app's fingerprint;
  the app constitution 1.1.0 allows them.
- Firebase sits behind an `AuthRepository` in the app, so tests use a fake and never reach Google.

## Consequences
- Two new packages in the app, both free.
- The owner keeps the Firebase console settings in step: authorized domains, the Android SHA-1 fingerprints (debug
  and release), and the email templates.
- iOS needs Sign in with Apple when Google sign-in is offered; that waits for a paid Apple account.
