# 0015 — Oracle hosting, released from factory_mousse
Date: 2026-11-02 · Status: Accepted · Replaces the backend and data parts of ADR 0004

## Context
ADR 0004 planned Cloud Run, Neon or Supabase, and Upstash. The owner wants the backend in containers on an Oracle Always Free server, with releases managed from one place.

Oracle's Always Free ARM allowance was checked on 2 Nov 2026: 2 OCPUs and 12 GB (not the 4 and 24 many guides still quote). Idle servers can be reclaimed, and creating one can fail for lack of capacity.

## Decision
- The backend runs as a Docker container on one Oracle Always Free ARM server. Caddy in front provides HTTPS.
- The web app stays on Firebase Hosting.
- The `factory_mousse` repo holds the infrastructure and the version of each app. The infrastructure is written in Terraform's language and run with OpenTofu, because HashiCorp's downloads and registry answered "Content not available in your region" from the owner's connection on 2 Nov 2026. Changing a version there through a pull request deploys it.
- App repos publish versions: a tag such as `v1.2.0` runs the tests and publishes an ARM64 image to GitHub's container registry (`pamplemousse`) or a web bundle on the GitHub release (`chocomousse`). `factory_mousse` never rebuilds them.
- Docker Compose runs the containers. Kubernetes and ArgoCD are a possible later upgrade.

## Still open
The owner plans for the database, sessions and file storage to come from Firebase. Firebase's Terraform guide says Auth through Terraform and Cloud Storage need the Blaze plan, which requires a billing card, and trips and bookings are relational data, which Firestore doesn't model directly. That needs its own decision.

## Consequences
- One server means no redundancy and a few seconds of downtime per backend deploy.
- Two cloud accounts to manage. A billing card needs the owner's approval (constitution, principle V).
- The API must stay backward compatible, because the backend and the app deploy separately.
- A separate RAG service would need a constitution amendment (principle I) and must fit the same server.
