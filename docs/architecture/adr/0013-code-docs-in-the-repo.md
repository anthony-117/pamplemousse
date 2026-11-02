# 0013 — Code documentation lives in the repo
Date: 2026-11-02 · Status: Accepted · Supersedes the "Documents stay in the OneDrive FYP folder" line of [0006](0006-repo-structure.md)

## Context
ADRs, schemas and setup notes lived in OneDrive, apart from the code they describe, so they could not change in the same pull request. Specs (Spec Kit) already belong in the repo.

## Decision
- **GitHub, in `pamplemousse/docs/`:** architecture, ADRs, schemas, agile process, backlog, sprint reviews, setup notes.
- **Why `pamplemousse` for shared docs:** most decisions cover both repos, and the backend owns the API contract. `chocomousse` links here instead of keeping copies. A third repo would add overhead for one developer.
- **OneDrive keeps everything not about code:** Figma links and the design approval log, brand assets, presentations, report, videos, the timeline, meeting notes, and all studies in `01-research/`, including the API exploration and its samples.

## Consequences
- Documentation changes go through the same branch, PR and CI as code.
- Both repos are public, so these documents are public. They MUST NOT contain secrets or personal data.
- The final report draws on two places: OneDrive for studies, this folder for architecture.
