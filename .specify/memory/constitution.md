# Pamplemousse Constitution

Pamplemousse is the .NET 10 backend of TravelGenie, an AI trip planner and a USEK final-year project
(defence around June 2027). Its clients are the `chocomousse` Flutter app and, from Sprint 9, a Telegram bot.

## Core Principles

### I. Layered Architecture

- Dependencies MUST point inwards: `Api → Application → Domain`, and `Infrastructure → Application`.
- Domain MUST have no package dependencies. Application MUST depend only on Domain and on
  `Microsoft.Extensions.*.Abstractions` packages.
- Each use case MUST be its own handler in Application. Controllers MUST only translate HTTP to a
  handler call and the result to a view model; they MUST NOT contain business rules.
- Business rules MUST live in Domain and be broken by throwing `DomainException` or `NotFoundException`.
- The system MUST stay one deployed service. Splitting it requires a new ADR.

Rationale: the app, the chatbot and the Telegram bot must all run the same use cases. Enforced by
`Pamplemousse.Architecture.Tests`.

### II. Every Feature Is Tested (NON-NEGOTIABLE)

- Every feature MUST ship with unit tests for its logic and integration tests for each endpoint through HTTP.
- Tests MUST cover failure paths: source down, missing key, bad input, not found.
- Every test MUST pass. Coverage percentages MUST NOT be used as a gate.
- Tests MUST NOT call the internet; external APIs are replaced by canned responses or fakes.
- A change to API behaviour MUST be checked against the running API, not only by tests.

Rationale: `main` must always be demonstrable, and external services must not make tests flaky.

### III. Zero Lint Findings

- Any analyzer finding MUST fail the build (`TreatWarningsAsErrors`), with Roslynator and Meziantou enabled.
- Code MUST pass `dotnet format --verify-no-changes`.
- An exception MUST be listed in `.editorconfig` with its reason, never suppressed inline without one.

### IV. Partial Failure, Not Total Failure

- A page built from several sources MUST load when one source fails; that section is null or empty and
  a warning says why.
- A missing API key MUST produce a warning naming the variable, never a crash or a failed startup.
- External providers MUST sit behind an Application interface, so a source can be replaced without
  touching use cases.
- Providers MUST cache responses and stay within each source's free limits and usage rules.

Rationale: free sources fail, rate-limit and change; the demo must survive that.

### V. Low Cost First

- The project MUST run at about $0 during development. A paid service, or one requiring a billing
  card, MUST NOT be added without the owner's explicit approval.
- Google Places MUST NOT be used while on hold (ADR 0008).
- Automated clients MUST NOT disguise themselves to get past a provider's restrictions.

### VI. Stable API Contract

- Routes MUST stay under `/api/v1`. Errors MUST be Problem Details.
- A breaking change to a response MUST be coordinated with `chocomousse` in the same delivery, or go
  under a new version.
- Response shapes MUST be defined by view models and mapped by hand, so a renamed field is a compile
  error rather than a wrong response.

### VII. Simplicity

- Prefer the simplest design that meets the current spec; no code for imagined future needs.
- A new framework or architectural pattern MUST have an ADR. MediatR and AutoMapper are not used
  (ADR 0012).

## Configuration and Security

- All environment variables MUST be read through `Env.cs` using `Env.Get(name, default)`, the single
  configuration file of this repo.
- API keys MUST come from environment variables or the git-ignored `.env`, and new ones MUST be added
  to `.env.example` without values.
- Secrets MUST NOT be committed, logged or sent to clients.
- personal data (passport, trips, chat history) MUST NOT be written to logs.

## Development Workflow

- `main` is protected: every change MUST go through a branch and a pull request, and CI MUST pass.
- The pre-push hook MUST run on every push and MUST NOT be bypassed without the owner's say-so. It
  runs formatting, analyzers, every test and the Docker image build.
- a backlog feature MUST have a spec in `specs/`. The owner MUST approve the spec and the
  plan before implementation starts. Bug fixes and small refactors MAY skip the spec.
- tests MUST be in the same pull request as the code they cover. Writing them first is
  encouraged but not required.
- A user-facing screen MUST have an approved Figma design before frontend work starts.
- Lasting architecture decisions MUST be recorded as ADRs in `docs/architecture/adr/`.
- Documentation about the code MUST live in this repo (`docs/`, `specs/`) and change in the same pull
  request as the code it describes. Non-code material (Figma, presentations, budget and other studies)
  stays in the OneDrive project folder.
- Each sprint's review MUST record what was delivered, including what is not done.

## Governance

- This constitution takes precedence over other practices. When it conflicts with a spec or a plan,
  the constitution wins until it is amended.
- when principles conflict, the order is: V Low Cost, then II Tested, then IV Partial Failure,
  then delivery speed.
- Only the owner approves amendments, through a pull request stating the reason.
- Versions follow semantic versioning: MAJOR removes or redefines a principle, MINOR adds or expands
  one, PATCH fixes wording.
- Every plan MUST pass the Constitution Check. Any justified violation MUST be recorded in the plan's
  Complexity Tracking table.

**Version**: 1.0.0 | **Ratified**: 2026-11-02 | **Last Amended**: 2026-11-02
