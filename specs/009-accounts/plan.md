# Implementation Plan: Accounts

**Branch**: `009-accounts` | **Date**: 2026-11-06 | **Spec**: [spec.md](spec.md)

**Status**: Draft, waiting for the owner's approval

## Summary

Every API route checks the Firebase ID token the app sends, using ASP.NET Core's JWT bearer handler pointed at
the Firebase project. Signature, project, expiry and a confirmed email are checked locally, with no network call
per request. Three routes are added for the traveller's own profile: `GET`, `PUT` and `DELETE /api/v1/me`. They
are stored in Firestore `travellers/{uid}`. Deleting removes the plans, the profile and the Firebase account,
the last with the Firebase Admin SDK.

This plan covers the API part of the spec. The app part (sign-in screens, the token on every request, Profile)
gets its own plan in `chocomousse`, from the approved Sprint 4 Figma page, once this API is merged.

## Technical Context

**Language/Version**: C# on .NET 10

**Primary Dependencies**, both new, free and needing ADR 0019:
- `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.13, the token check (research R-1);
- `FirebaseAdmin`, only to delete the Firebase account (R-4).

**Storage**:
- Firestore `travellers/{uid}`, with memory when no project is set.
- One cache key per deleted account, for 1 hour (R-5).

**Testing**: xUnit.
- Domain: profile rules.
- Application: handlers with fakes.
- Infrastructure: Firestore profile store against the existing Firestore emulator container.
- Api: `WebApplicationFactory` with a test signing key, and tokens made per test (R-8).

**Target Platform**: Linux container on the Oracle server (ADR 0015)

**Project Type**: Web service

**Performance Goals**: the token check adds under 5 ms a request once Google's keys are loaded. `GET /me` takes
under 300 ms (one Firestore read).

**Constraints**:
- No secret in the app. The API needs no new secret: the project id and the service-account key file are the ones
  Firestore already uses.
- Nothing personal in logs (FR-016).

**Scale/Scope**:
- one Domain module and three handlers;
- one controller, one store, one account deleter;
- authentication set up in `Program.cs`;
- every existing endpoint test sends a token.

## Spec changes found while planning

These need your OK together with this plan.

1. **Edge Cases, "a deleted or disabled account"**:
   - An account deleted through the app is refused at once.
   - An account disabled in the Firebase console is refused within 1 hour at most, when its token expires.
   
   Refusing it sooner needs a network call to Google on every request (R-5).
2. **FR-014, "recent sign-in"**: the API asks for a sign-in from the last 5 minutes before deleting, the same
   rule Firebase uses, since the Admin SDK doesn't enforce it (R-3).

## Constitution Check

| Principle | How this plan meets it | Result |
|---|---|---|
| I. Layered Architecture | Profile rules in Domain (`Travellers/`); handlers in Application get the traveller as an argument; token checking in Api; Firestore and Admin SDK in Infrastructure behind `ITravelProfileStore` and `IAccountDeleter` | Pass |
| II. Every Feature Is Tested | Each refusal (no token, expired, wrong project, wrong key, unconfirmed, deleted, old sign-in, not configured), profile rules, each handler, the store on the emulator; no call to Google in tests | Pass |
| III. Zero Lint Findings | Normal build gate | Pass |
| IV. Partial Failure | No project set: the API starts and answers 503 naming `FIREBASE_PROJECT_ID`. Firestore down: profile routes give 503; other routes keep working | Pass |
| V. Low Cost First | Firebase Authentication free on the Spark plan; no new paid service | Pass |
| VI. Stable API Contract | Breaking for clients: every route needs a token. The app adds it in the same sprint, before deployment (see Delivery). New routes are additions. Problem Details with a `code` | Pass, coordinated |
| VII. Simplicity | The standard ASP.NET Core handler, no custom token code; Admin SDK used for one call; ADR 0019 for the new pattern | Pass |
| Configuration and Security | No new variable; nothing secret in the app; tokens, ids and emails never logged; `TravellerId.ToString()` hides the id | Pass |

## Project Structure

### Documentation (this feature)

```text
specs/009-accounts/
├── spec.md · plan.md · research.md · data-model.md · quickstart.md
├── contracts/accounts-api.md
└── tasks.md             # next step, after approval
docs/architecture/adr/0019-firebase-sign-in.md
```

### Source Code

```text
src/
├── Pamplemousse.Api/
│   ├── Auth/FirebaseAuthentication.cs     # new: JWT bearer setup, confirmed-email and deleted-account checks, fallback policy, 503 when not configured
│   ├── Auth/AuthProblems.cs               # new: Problem Details with `code` for 401 / 403 / 503
│   ├── Auth/SignedInTravellerExtensions.cs# new: claims → SignedInTraveller
│   ├── Controllers/MeController.cs        # new: GET / PUT / DELETE /api/v1/me
│   ├── Contracts/ProfileRequests.cs       # new
│   ├── ViewModels/ProfileViewModels.cs    # new
│   ├── Mappings/ProfileMappings.cs        # new
│   ├── Controllers/SystemController.cs    # ping: [AllowAnonymous]
│   └── Program.cs                         # UseAuthentication / UseAuthorization; Scalar gets the Bearer scheme
├── Pamplemousse.ServiceDefaults/Extensions.cs   # health endpoints: AllowAnonymous
├── Pamplemousse.Application/
│   ├── Abstractions/Accounts.cs           # ITravelProfileStore, IAccountDeleter, IDeletedTravellers
│   └── Travellers/                        # SignedInTraveller; GetProfile, UpdateProfile, DeleteAccount handlers
├── Pamplemousse.Domain/
│   └── Travellers/                        # TravellerId, TravelProfile
└── Pamplemousse.Infrastructure/
    ├── Storage/FirestoreTravelProfiles.cs · InMemoryTravelProfiles.cs
    ├── Storage/FirestoreDocuments.cs      # profile ↔ document
    ├── Accounts/FirebaseAccountDeleter.cs · NotConfiguredAccountDeleter.cs
    ├── Caching/DeletedTravellers.cs       # on ISearchCache
    └── DependencyInjection.cs

tests/
├── Pamplemousse.Domain.Tests/Travellers/        # defaults, each field's rule, names trimmed, ToString hides the id
├── Pamplemousse.Application.Tests/Travellers/   # created on first read, update checks the reference data, delete order and retry, already-gone counts as done
├── Pamplemousse.Infrastructure.Tests/Storage/   # profile round trip and delete on the Firestore emulator
├── Pamplemousse.Api.Tests/Auth/                 # every refusal of the contract; open routes stay open; two travellers never see each other (SC-006)
├── Pamplemousse.Api.Tests/TestTokens.cs         # new: test RSA key and token maker; the factory sends a valid token by default
└── Pamplemousse.Architecture.Tests/             # unchanged rules; Application has no ASP.NET reference
```

**Structure Decision**: the check is the standard ASP.NET Core authentication pipeline with a fallback policy,
so every current and future controller is protected unless marked `[AllowAnonymous]`. Nothing has to remember to
add `[Authorize]`. The traveller is passed to handlers as an argument rather than read from a service, so the
Telegram bot (TG-16) can call the same handlers with its own way of knowing who is asking.

## How a request is checked

1. No `FIREBASE_PROJECT_ID`: every protected route answers 503 `sign-in-not-configured`.
2. JWT bearer: signature with Google's keys, issuer and audience for the project, lifetime with 1 minute of
   skew, `sub` present. Any failure gives 401 `sign-in-required`.
3. `email_verified` is true, otherwise 403 `email-not-confirmed`.
4. The user id is not on the deleted list (R-5), otherwise 401 `sign-in-required`.
5. The controller builds `SignedInTraveller` from `sub`, `email` and `name` and calls the handler.

`DELETE /me` also checks `auth_time` is within 5 minutes (401 `recent-sign-in-required`).

## Delivery order

1. **ADR 0019** "Firebase sign-in for the API": the choice of R-1 and R-4.
2. **Domain**: `TravellerId`, `TravelProfile`, fully tested.
3. **Auth in the API**: handler, policies, Problem Details, open routes, test tokens. Every existing endpoint test
   now sends a token, plus one test per refusal.
4. **Story 3, profile**: store (Firestore and memory), handlers, `GET` and `PUT /me`.
5. **Story 4, delete**: Admin SDK deleter, deleted list, `DELETE /me`.
6. **Checks and docs**:
   - quickstart, with the owner running the signed-in checks;
   - README (the header, the new routes);
   - `.env.example` notes that `FIREBASE_PROJECT_ID` now also turns on sign-in;
   - sprint 4 log.

Each step ends with all tests passing. The whole feature is one pull request.

**Deploying**: merging this into `main` makes every route need a token, so the deployed web app would stop
working until its sign-in is released. The release tag that deploys the API waits until the app's sign-in pull
request is merged too, and both go out together.

## Risks

| Risk | Effect | Response |
|---|---|---|
| The service-account key can't delete Firebase accounts | `DELETE /me` gives 503 | Quickstart check 11 finds it; the owner grants the Firebase Authentication Admin role in the console |
| The app keeps an old token after the email is confirmed | Still sees "Check your email" | The app renews the token on "I've confirmed it" (R-2), tested in the app's plan |
| Clock drift on the server | Valid tokens refused | 1 minute of skew; the server uses NTP |
| The API is deployed before the app's sign-in | The live web app breaks | Released together (Delivery) |
| The assistant's per-IP limits now see only signed-in travellers | None at first | Could switch to per-traveller limits later; not in this spec |

## Complexity Tracking

None. No constitution violations.
