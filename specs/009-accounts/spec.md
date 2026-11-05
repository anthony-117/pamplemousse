# Feature Specification: Accounts — sign in before using the app, with a saved travel profile

**Feature Branch**: `009-accounts`

**Created**: 2026-11-06

**Status**: Approved by the owner on 2026-11-06. Design: Figma v0.2 screen 01 "Welcome & sign in" and web W5, screen 12 "Profile & preferences" and web W9 (approved 2026-10-27). A Sprint 4 Figma page with the missing states comes after this spec is approved.

**Input**: User description: "TG-17: As Fares, I want to sign in with email or Google so my plans and travel profile are mine on every device." The owner decided on 2026-11-06 that signing in is required before using the app, with no anonymous use.

## Clarifications

### Session 2026-11-06

- Q: Which sign-in methods? → A: Firebase Authentication (project already set up by the owner) with **email and password**, and **Google**. No anonymous sign-in: the owner turns that provider off in the Firebase console.
- Q: Is signing in required? → A: Yes. Every screen of the app and every API request needs a signed-in traveller, apart from the welcome and sign-in screens and the API's health check.
- Q: Which platforms? → A: **Web and Android** in this sprint. iOS waits: the App Store requires Sign in with Apple when Google sign-in is offered, which needs a paid Apple developer account (owner's approval, constitution V).
- Q: Must an email account be confirmed? → A: Yes. A new email and password account must open the confirmation link Firebase sends before it can use the app. Google accounts are already confirmed.
- Q: What does the account keep? → A: The **full travel profile** (Figma 12): name, email, passport country, home currency, home city (where flights leave from) and default preferences (the six of spec 007). Plans are added to the account by the itinerary (spec 008).
- Q: Can a traveller delete their account? → A: Yes, from the app. It removes the sign-in and everything stored for that traveller. Google Play requires this for apps with accounts.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Sign in to use the app (Priority: P1)

Fares opens TravelGenie for the first time and sees the welcome screen. They choose "Continue with Google",
pick their account and land on Discover. Next time they open the app, they are still signed in.

**Why this priority**: Nothing else in the app works without it, and the itinerary (spec 008) needs to know
whose plans are whose.

**Independent Test**: Open the app signed out and confirm only the welcome and sign-in screens are reachable;
sign in with Google and with a confirmed email account and reach Discover; call the API without a sign-in and
confirm it is refused.

**Acceptance Scenarios**:

1. **Given** Fares is signed out, **When** they open the app or any link inside it, **Then** they see the welcome screen, and after signing in they land where the link pointed (or Discover).
2. **Given** the welcome screen, **When** Fares chooses Continue with Google and picks an account, **Then** they are signed in.
3. **Given** the welcome screen, **When** Fares chooses I already have an account and enters a confirmed email and the right password, **Then** they are signed in.
4. **Given** a wrong password or an unknown email, **Then** Fares sees "Email or password is wrong", without saying which.
5. **Given** Fares signed in before, **When** they open the app again, **Then** they are still signed in, without seeing the welcome screen.
6. **Given** Fares is signed in, **When** they sign out from Profile, **Then** they return to the welcome screen and no screen with their data can be reached with Back.
7. **Given** any API request without a valid sign-in (none, expired, from another Firebase project, or forged), **Then** it is refused, and the app returns to the welcome screen when its sign-in can't be renewed.

---

### User Story 2 - Create an account with email (Priority: P1)

Maya has no Google account she wants to use. She signs up with her name, email and a password, opens the
confirmation link in her inbox, and the app lets her in.

**Why this priority**: Not everyone signs in with Google. Same priority as Story 1, as both are ways in.

**Independent Test**: Sign up with a new email, confirm the app holds at "Check your email" until the link is
opened, then confirm access; reset the password of that account.

**Acceptance Scenarios**:

1. **Given** the welcome screen, **When** Maya signs up with a name, an email and a password of at least 8 characters, **Then** her account is created and Firebase sends a confirmation email.
2. **Given** an account not yet confirmed, **When** Maya is in the app, **Then** she sees only "Check your email", with Resend the email, I've confirmed it, and Sign out.
3. **Given** she opened the link, **When** she chooses I've confirmed it (or reopens the app), **Then** she is let in.
4. **Given** an account not yet confirmed, **When** a request reaches the API with its sign-in, **Then** it is refused with a message that the email isn't confirmed.
5. **Given** an email that already has an account, **When** someone signs up with it, **Then** they are told to sign in instead, and no second account is made.
6. **Given** a forgotten password, **When** Maya chooses Forgot password and enters her email, **Then** Firebase sends a reset link, and the app says so whether or not the email has an account.

---

### User Story 3 - Keep my travel profile (Priority: P2)

Fares sets their passport (Lebanon), home currency (USD), home city (Beirut) and default preferences (Budget
High, Culture High) once in Profile. On another device, after signing in, every new trip starts with them.

**Why this priority**: It saves retyping on every device and every trip, but the app works without it.

**Independent Test**: Set the profile on one device, sign in on another and confirm the trip settings start from
it; confirm another traveller's profile is never returned.

**Acceptance Scenarios**:

1. **Given** a first sign-in, **Then** the profile is created with the name and email of the sign-in and the app's current defaults (passport Lebanon, currency USD, no home city, every preference Medium).
2. **Given** Profile, **When** Fares changes the passport, home currency, home city or default preferences, **Then** they are saved to the account.
3. **Given** a saved profile, **When** Fares signs in on any device, **Then** new trips start with that passport, currency, departure city and preferences.
4. **Given** a trip, **When** Fares changes the passport, currency, departure city or preferences on it, **Then** the trip changes but the profile doesn't, until they change it in Profile.
5. **Given** Profile, **When** Fares changes their display name, **Then** it is saved; the email can't be changed in this feature.

---

### User Story 4 - Delete my account (Priority: P2)

Maya no longer wants TravelGenie. In Profile she chooses Delete my account, confirms, and everything about her
is gone.

**Why this priority**: Required by Google Play and by privacy expectations, but used rarely.

**Independent Test**: Create an account with a profile (and, once spec 008 is in, plans), delete it, and confirm
the sign-in no longer works and nothing stored for it remains.

**Acceptance Scenarios**:

1. **Given** Profile, **When** Maya chooses Delete my account, **Then** she is told what will be removed and asked to confirm.
2. **Given** she confirms, **Then** her sign-in, profile and plans are removed, and she returns to the welcome screen.
3. **Given** she signed in long ago, **When** she deletes, **Then** she is asked to sign in again first (Firebase requires a recent sign-in).
4. **Given** a deleted account, **When** the same email signs up again, **Then** it is a new, empty account.

---

### Edge Cases

- **No connection or Firebase unreachable** while signing in: a message says sign-in isn't possible right now, with Try again. A traveller already signed in keeps using the app while their sign-in is valid.
- **The Google window is closed** or blocked: Fares stays on the welcome screen with no error shown.
- **The same email for Google and a password account**: one account per email (Firebase's default). Signing in with the other method follows Firebase's linking, and the profile stays the same.
- **The API's sign-in check can't reach Google's keys** (first start offline): requests are refused with a temporary error, never let through.
- **A deleted or disabled account** that still holds a sign-in: refused by the API; the app signs out.
- **The profile can't be loaded**: the app uses its defaults for the trip and shows a warning; Profile shows the error with Try again.
- **Bad profile values** (unknown passport or currency code, unknown home city, a preference outside 0–100, an empty or overlong name): rejected, naming the field.
- **Too many attempts**: Firebase's own limits apply; the app shows its message.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The app MUST show only the welcome, sign-in, sign-up, forgot password and check your email screens until a traveller is signed in with a confirmed account.
- **FR-002**: Sign-in MUST use Firebase Authentication with email and password, and Google, on web and Android.
- **FR-003**: Sign-up with email MUST ask for a name (1 to 60 characters), an email and a password of at least 8 characters, and MUST send Firebase's confirmation email.
- **FR-004**: An email account MUST be confirmed before the app or the API can be used.
- **FR-005**: Forgot password MUST send Firebase's reset email, and the app MUST give the same answer whether or not the email has an account.
- **FR-006**: The app MUST stay signed in across restarts until the traveller signs out, the sign-in can't be renewed, or the account is deleted.
- **FR-007**: Every API request MUST carry the traveller's Firebase sign-in, except the health check and ping. The API MUST check that it is valid for this Firebase project, not expired, not forged, and from a confirmed email, and MUST refuse it otherwise with a Problem Details error that doesn't say which check failed beyond "sign in again" or "confirm your email".
- **FR-008**: The API MUST know the traveller only by their Firebase user id, and pass it to the use cases that need it (profile, plans).
- **FR-009**: The account MUST keep a travel profile: display name, email (from the sign-in), passport country, home currency, home city (optional), and default preferences (the six of spec 007, each 0–100). Pace joins the defaults when spec 008 adds it.
- **FR-010**: On first sign-in, the profile MUST be created from the sign-in's name and email and the app's defaults.
- **FR-011**: The traveller MUST be able to read and change their profile (not the email). Changes MUST be checked as in Edge Cases.
- **FR-012**: New trips in the app MUST start from the profile; changing a trip MUST NOT change the profile.
- **FR-013**: A traveller MUST only read and change their own profile.
- **FR-014**: Delete my account MUST ask for confirmation, MUST ask for a recent sign-in when Firebase requires it, and MUST remove the Firebase account, the profile and every plan of that traveller.
- **FR-015**: Sign out MUST remove the sign-in from the device and clear the traveller's data from the app's memory.
- **FR-016**: Emails, names, user ids, sign-in tokens and profile values MUST NOT be written to logs (constitution: personal data and secrets).
- **FR-017**: The Firebase project and the key files MUST come from configuration (`Env.cs`, `env.dart`), never from the code, and nothing secret MUST be in the app.

### Key Entities

- **Traveller (Account)**: the Firebase user id, email, whether it is confirmed, sign-in method(s).
- **Travel profile**: display name, email, passport country, home currency, home city, default preferences, created and updated times. One per traveller.
- **Plans**: spec 008, owned by a traveller.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Signed out, no app screen other than the sign-in screens can be reached, by tapping or by typing a web address (checked on web and Android).
- **SC-002**: Every API endpoint except the health check and ping refuses a request with no sign-in, an expired one, one from another project, and one from an unconfirmed email (checked against the running API).
- **SC-003**: A returning traveller reaches Discover without signing in again, in under 3 seconds on web.
- **SC-004**: A profile set on one device is used on another within one sign-in.
- **SC-005**: After deleting an account, nothing stored for it remains in Firestore and its sign-in no longer works.
- **SC-006**: A traveller never reads or changes another traveller's profile (checked with two accounts against the running API).

## Assumptions

- **Scope covers the API and the app.** The API part (checking sign-ins, profile, deleting a traveller's data) is planned and built in `pamplemousse`; the screens in `chocomousse` after the Sprint 4 Figma page is approved.
- Firebase project `travelgenie-1fdd1` has email/password and Google sign-in on (done by the owner). The owner also turns off Anonymous, adds the Android app's SHA-1 fingerprint and the web domains in the Firebase console, and sets the confirmation and reset email templates. These are console settings and the owner's to change.
- Firebase Authentication is free at this size (Spark plan). No paid service is added.
- Profiles are stored in Firestore (ADR 0017), keyed by the Firebase user id.
- The Telegram bot (TG-15, TG-16) will need its own way to identify a traveller; that is decided with TG-16.
- The assistant's conversations stay unstored (spec 003); they only need the sign-in to be allowed.
- Changing the email, linking extra sign-in methods from Profile, and Sign in with Apple are out of scope.
