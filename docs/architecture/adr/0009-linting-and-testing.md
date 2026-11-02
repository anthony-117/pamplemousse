# 0009 — Strict linting and tests for every feature
Date: 2026-10-27 · Status: Accepted

## Context
The user wants Clippy-style linting (as in Rust) in both repos to keep the code clean, and every feature to ship with its own unit and integration tests.

## Decision
### Linting
| | Backend (pamplemousse) | App (chocomousse) |
|---|---|---|
| Linter | .NET analyzers (`latest-recommended`) + **Roslynator** + **Meziantou.Analyzer**, `.editorconfig` style & naming rules enforced in the build | **very_good_analysis** + strict casts / inference / raw types |
| Fail on | Any warning (`TreatWarningsAsErrors`) | Any finding (`flutter analyze --fatal-infos`) |
| Formatting | `dotnet format --verify-no-changes` | `dart format --set-exit-if-changed` (width 120) |
| Exceptions | Listed with a reason at the bottom of `.editorconfig` (one type per file, ConfigureAwait, test naming) | Listed with a reason in `analysis_options.yaml` (80-column lint, public API docs, Dart 3.13 `new(...)` constructor shorthand) |

The linters already found and fixed real issues: regexes without a timeout (ReDoS risk), implicit string comparisons, an over-long endpoint method, an internal-package import and an accidental `riverpod: any` dependency.

### Tests
| Kind | Backend | App |
|---|---|---|
| Unit | Domain; each provider against canned JSON via `StubHttp` (no internet); cache; reference data; service with fakes; date rules; `Env` | Models; repository against a fake Dio adapter; providers; error messages; `Env` |
| Integration | Every endpoint through HTTP (`WebApplicationFactory`), with fake free-data providers and no keys | Widget tests per screen/widget; flow tests through the real router and shell |
| Gate | Every test must pass | Every test must pass |

Status on 27 Oct 2026: backend **101 tests**, app **39 tests**, all passing; both repos 0 lint findings.

**Amended 27 Oct 2026:** the 85% line-coverage gate was removed at the user's request — the rule is that every test passes, not a percentage. (The coverage action also did not merge the three backend test projects' reports, so its 80% figure was misleading.)

A PR template in each repo lists the required tests and checks.

**Pre-push hook (added 27 Oct 2026):** each repo versions `.githooks/pre-push`, installed with `sh scripts/install-hooks.sh` (`core.hooksPath`). It runs the same checks as CI — formatting, lint, every test — and blocks the push if any fails. Verified by pushing with a deliberately failing test (blocked, exit 1) and with all tests green (pushed). Amended the same day: the hook now runs EVERY CI step, including the Docker image build (backend; Docker Desktop must be running, otherwise the hook stops with a clear message) and `flutter build web --release` (app). Reason: a CI-only Docker build failed because `.editorconfig` was not copied into the image, which the hook could not catch.

## Consequences
- CI is the gate: a PR with a lint finding, unformatted code or any failing test cannot be merged.
- Device-level integration tests (`integration_test` on a phone or emulator, against a real backend) are not set up yet; flow tests cover the same journeys in the test runner. Revisit when Android is set up.
