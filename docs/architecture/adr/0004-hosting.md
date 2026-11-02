# 0004 — Hosting
Date: 2026-10-26 · Status: Accepted

## Decision
- Backend: Docker container on **Google Cloud Run** (alternative: Azure Container Apps with student credit).
- Flutter web: **Firebase Hosting**; Android test builds via Firebase App Distribution; app stores later.
- Postgres + pgvector: **Neon** or **Supabase**. Redis: **Upstash**.
- CI/CD: GitHub Actions.

## Consequences
About $0 at MVP traffic plus LLM tokens. Cold starts on Cloud Run unless one instance is kept warm.

See hosting study (OneDrive: `01-research/03-hosting-study.md`).
