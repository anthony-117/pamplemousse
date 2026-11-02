# Agile Process

- **Sprints:** 2 weeks. Each sprint ends with a working vertical slice (API → Flutter screen), not a finished layer.
- **Backlog:** GitHub Projects (linked to both repos) or Notion. User stories: "As Fares, I want … so that …".
- **Design gate:** no frontend code for a screen starts until its Figma design is approved (logged in OneDrive: `04-design/figma/figma-links.md`).
- **Definition of done:** deployed, tested, visible in the app, matching the approved design. Not just "code written". Every feature ships with:
  - **Unit tests** for its logic (backend: domain, each provider with canned JSON, service with fakes; app: models, repository, providers).
  - **Integration tests** (backend: every endpoint through HTTP; app: widget tests per screen and a flow test per new user journey).
  - Failure paths: source down, missing key, bad input, loading / error / empty states.
  - **Zero lint findings** and formatting clean (backend: .NET analyzers + Roslynator + Meziantou; app: very_good_analysis with `--fatal-infos`).
  - **Every test passes** — CI fails on a single failing test (no coverage percentage target). See [ADR 0009](../architecture/adr/0009-linting-and-testing.md).
- **Branches and PRs:** no direct commits to `main`; every change goes through a pull request that CI must pass.
- **Pre-push hook:** in both repos `git push` is blocked locally unless formatting, lint and every test pass (install once per clone: `sh scripts/install-hooks.sh`).
- **Sprint end:** short demo + review + retrospective, written in `sprints/sprint-NN.md`. Record a 1-minute demo clip into OneDrive: `06-assets/video/`.
- **Meetings with the supervisor:** OneDrive: `00-management/meetings/YYYY-MM-DD.md`.

## Sprint plan
| Sprint | Goal (demo at the end) |
|---|---|
| 0 | Repos, .NET and Flutter skeletons, Docker, CI/CD, "hello world" deployed, backlog set up |
| 1 | Pick Rome → country info, weather, currency, holidays, visa, advisories, attractions, restaurants, events |
| 2 | Flights and hotels from Travelpayouts (ADR 0010), cars as links, plus Redis caching |
| 3 | **Weighted search:** sliders for budget, culture, food… → ranked results |
| 4 | **Itinerary builder:** full "Rome Complete: 96%" plan |
| 5 | **Chatbot with tools:** "swap Day 3 for a food tour" actually changes the plan |
| 6 | Own MCP server exposing the tools; external MCPs wired through it |
| 7 | **RAG** over saved trips and preferences (personalised suggestions) |
| 8 | Booking flow (affiliate links / sandbox), save and print plans, polish |
| 9 | **Telegram bot** with the same features as the app, connected directly to the .NET backend ([ADR 0007](../architecture/adr/0007-telegram-bot.md)) |
