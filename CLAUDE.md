<!-- SPECKIT START -->
For additional context about technologies to be used, project structure,
shell commands, and other important information, read the current plan:
`specs/002-local-manual-pr-review/plan.md`

**Project constitution**: `.specify/memory/constitution.md` (v1.0.0). Seven non-negotiable
principles, each enforced by a test. It governs the agent package and its tests (both now live in
the sibling `CodeReviewAgent` repository) and `specs/` here — **not** `src/eShop.Api/` or `demo/`,
which are the deliberately-flawed demo target the agent reviews. A change that requires weakening
one of the enforcing tests is a constitutional amendment, not a fix.

**Where the code lives**: this repo (`CodeReviewSample`) is the *sample code under review*. The
review agent itself is in `../CodeReviewAgent` (`review_agent/`, 15 modules); the checklists are
five .xlsx/.docx documents in `../CodeReviewChecklist`. Feature 001 is implemented; feature 002 is
a delta on it.

Supporting design artifacts for the current feature:

- `specs/002-local-manual-pr-review/spec.md` — 5 clarifications + 1 planning amendment (FR-017a)
- `specs/002-local-manual-pr-review/research.md` — 14 decisions verified against the real repos
- `specs/002-local-manual-pr-review/data-model.md` — entities, document states, routing, config
- `specs/002-local-manual-pr-review/quickstart.md` — first dry run, troubleshooting
- `specs/002-local-manual-pr-review/contracts/` — CLI contract, checklist extraction, LLM backend
  interface, response schema v2
- Prior feature: `specs/001-ai-code-review-agent/` (spec, research, data model, contracts)

**Known blocker before any 002 work**: `tests/` in this repo imports `review_agent`, which is not
here — `pytest --collect-only` fails with 17 collection errors, so no constitutional principle is
currently enforced. Task A1 moves the suite into `CodeReviewAgent`. Nothing else starts first.

**Repo safety note**: verified 2026-09-15 — `.env` is **not** tracked and `.gitignore` covers it
(`.env`, `*.pem`, `*.key`, `*.pfx`). The earlier warning about a tracked `.env` and an eShop
`origin` is stale: `origin` is now `CodeReviewSample`. The working copy still holds live PATs and
an LLM key, so keep them out of commits, command lines, and logs.
<!-- SPECKIT END -->
