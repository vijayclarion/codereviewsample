# Implementation Plan: AI Code Review Agent for On-Prem TFS Pull Requests

**Branch**: `001-ai-code-review-agent` | **Date**: 2026-08-26 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/001-ai-code-review-agent/spec.md`

## Summary

Build a stateless Python CLI that reviews one TFS / Azure DevOps Server pull request against its
linked user story and the team's shared markdown checklists, then posts a summary discussion plus
anchored inline comments back onto that pull request. It runs manually with a PAT (preview mode) or
as a branch-policy validation build using the build's own OAuth token.

Technical approach: a single-process pipeline — `config → fetch (story, PR, changes, checklists) →
select reviewable content → build prompt(s) → call the provider → validate the JSON contract →
reconcile against feedback already posted → post or print`. Two boundaries stay strictly isolated so
each is replaceable alone: `tfs_client.py` is the only module that speaks TFS REST, and
`llm_adapter.py` is the only module that knows a provider's wire format. No database, no local state
— everything the agent needs to know about its own past feedback it reads back from the pull
request's existing threads.

**Two repository facts materially affect this plan** (verified, not assumed — see
[Preconditions](#preconditions-verified-against-this-repo)): the eShop remote is empty apart from a
placeholder `.env`, so the "ready-made demo" the brief refers to does not exist and must be built to
satisfy SC-001; and `.env` is tracked by git, so the real credentials now in the working copy would
be published to the remote on the next commit of that file.

## Preconditions (verified against this repo)

| Fact | Evidence | Consequence for this plan |
|---|---|---|
| `origin` is the eShop repo | `git remote -v` → `https://devops.najm.sa/tfs/Najm/Najm-Architecture-Unit/_git/eShop` | This working tree **is** the eShop repo; the agent lands here under `review_agent/`, as the brief prescribes. |
| The remote contains only an empty `.env` | `git ls-tree -r origin/master` → 1 entry | The brief's "ready-made demo" (sample .NET API, `demo/user-story-101.md`, `demo/pr-description.md`, `azure-pipelines.yml`) **does not exist**. SC-001 cannot be verified without it, so building it is in scope — see workstream E. |
| `.env` is tracked by git | `git ls-files --error-unmatch .env` → tracked | Non-negotiable violation of FR-036. The committed blob is empty, so **no secret is in history yet**, but the working copy now holds a live PAT and LLM key that the next commit of that file would publish. Remediated first — workstream A, task A1. |
| Python 3.13.14 available locally | `python --version` | Satisfies the 3.11+ floor for development. The **self-hosted build agent's** Python is unverified — see Risks. |

> **Do not run `/speckit-git-commit` (or any `git commit -a`) until task A1 is done.** The optional
> auto-commit hooks offered by the Spec Kit workflow would stage and push the tracked `.env`.

## Technical Context

**Language/Version**: Python 3.11+ (3.13.14 local). No language features beyond 3.11 are used, so the
agent runs unchanged on 3.11, 3.12, and 3.13.
**Primary Dependencies**: `requests` (all HTTP — TFS and every LLM provider), `python-dotenv` (`.env`
loading), `truststore` (binds `requests` to the OS trust store so the private CA on
`devops.najm.sa` validates — see research R7). Provider SDKs deliberately **not** used. Dev-only:
`pytest`, `responses`.
**Storage**: None. The agent is stateless; prior-feedback state is recovered by reading the pull
request's existing comment threads (research R5).
**Testing**: `pytest`, with `responses` faking the TFS and provider HTTP surfaces. Recorded-fixture
integration tests for the section 9 demo scenario; no live network in the test suite.
**Target Platform**: Self-hosted TFS build agent (Windows, per the existing pool) and developer
workstations (Windows 11). Nothing platform-specific — `pathlib` and POSIX-style repo paths only.
**Project Type**: Single-project command-line tool, vendored inside the repo it reviews.
**Performance Goals**: End-to-end within 5 minutes of the PR opening (SC-002), including build-agent
queue time — budget: ≤30 s for all TFS calls, ≤3 min for provider calls across all chunks, ≤20 s to
post.
**Constraints**: No secret in code, logs, console, prompt, or posted feedback (FR-006). TLS
verification never disabled (FR-034). Bounded review volume (FR-014e). Non-blocking by default
(FR-027). Exactly one summary discussion per PR (FR-030).
**Scale/Scope**: One pull request per run. Default bound 40 files / 3,000 changed lines; single
chunk up to ~1,200 diff lines, then split on file boundaries. **As built: 15 modules, ~3,300 lines**
(the pre-implementation estimate was 11 modules / ~1,300 lines; the five clarifications added four
modules, and the error taxonomy and renderers are larger than estimated).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

`.specify/memory/constitution.md` is **ratified at v1.0.0** (2026-08-26). The seven gates this plan
originally imposed on itself are now project principles; this section checks the design against
them rather than against promises it made to itself.

**Gate result: PASS.** Every principle is satisfied by the design and enforced by a test.

| Principle | This design | Enforced by |
|---|---|---|
| I. Configuration Is External | Every value in `contracts/cli-contract.md` resolves through `config.py`; no other module reads the environment | `tests/contract/test_import_boundaries.py` |
| II. Secrets Never Surface | Redaction registry + single `emit` path (research R8); auth headers built per request, never formatted into a message | `tests/contract/test_no_secrets_leak.py` |
| III. Transport Security Is Not Optional | `truststore` injected before the first session; `verify=` never passed (research R7) | `tests/contract/test_tls_never_disabled.py` |
| IV. Replaceable Boundaries | TFS REST only in `tfs_client.py`; provider format only in `llm_adapter.py`, which returns raw text | `tests/contract/test_import_boundaries.py` |
| V. Partial Work Is Not A Defect | `not_addressed` routes to the summary only and is excluded from the blocking set (routing table in `data-model.md`) | `tests/unit/test_routing_not_addressed.py` |
| VI. The Human Decides The Merge | Exit 0 for completed and degraded runs; exit 1 only with `FAIL_ON_CONTRADICTION=true`; no vote, resolve, or delete path exists | `tests/contract/test_preview_makes_no_writes.py`, `tests/integration/test_us3_posting.py` |
| VII. Silence Must Never Read As Success | Excluded and unreviewed files named with reasons; degraded/setup split (FR-029); one strict retry then degrade | `tests/integration/test_bound_transparency.py`, `tests/contract/test_excluded_files_never_sent.py` |

Two constitutional notes specific to this feature:

- The constitution's **Scope And Non-Scope** section explicitly excludes `src/eShop.Api/` and
  `demo/`. That demo code omits the stock-limit check deliberately — it is the fixture the
  acceptance test reviews, and a correctness principle applied to it would destroy the scenario.
- Principle VI is the one a future change is most likely to erode, because "just fail the build on
  a contradiction" is a tempting default. Amending it requires repository-owner sign-off.

## Project Structure

### Documentation (this feature)

```text
specs/001-ai-code-review-agent/
├── plan.md              # This file
├── spec.md              # Feature specification (5 clarifications applied)
├── research.md          # Phase 0 output — 12 decisions
├── data-model.md        # Phase 1 output — entities, validation, state
├── quickstart.md        # Phase 1 output — first manual dry run
├── contracts/           # Phase 1 output
│   ├── llm-response.schema.json   # The model's JSON contract (enforced)
│   ├── cli-contract.md            # CLI surface, config vars, exit codes
│   ├── tfs-endpoints.md           # Every TFS REST call and the fields relied on
│   └── pr-comment-format.md       # Comment layout + self-identification marker
├── checklists/
│   └── requirements.md  # Spec quality checklist (16/16 pass)
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
eShop/                              # this repo — origin is the eShop repo on TFS
├── .gitignore                      # NEW (task A1) — must ignore .env
├── .env                            # local only, untracked after A1
├── azure-pipelines.yml             # NEW — trigger: none; wired via master branch policy
├── review_agent/                   # the agent
│   ├── __init__.py
│   ├── main.py                     # entrypoint: CLI, orchestration, exit codes
│   ├── config.py                   # .env + pipeline vars, validation, redaction registry + emit()
│   ├── errors.py                   # SetupFailure / DegradedReview taxonomy (FR-029)
│   ├── models.py                   # frozen dataclasses for every data-model.md entity
│   ├── tfs_client.py               # ONLY module that speaks TFS REST
│   ├── checklist.py                # enumerate + fetch + combine checklist files
│   ├── diff_builder.py             # changes → unified diff + new-file line map
│   ├── selection.py                # exclusion list + review bound + relevance order
│   ├── textutil.py                 # HTML → plain text (stdlib only)
│   ├── prompt_builder.py           # story + checklist + scope + diff → prompt(s)
│   ├── llm_adapter.py              # ONLY module that knows a provider's wire format
│   ├── parser.py                   # JSON contract validation + one strict retry
│   ├── dedupe.py                   # finding fingerprints; reconcile with posted threads
│   ├── output_layer.py             # routing, dry-run printing, thread posting
│   ├── requirements.txt
│   ├── requirements-dev.txt
│   ├── .env.example
│   └── README.md
├── demo/                           # NEW (workstream E) — the SC-001 acceptance scenario
│   ├── user-story-101.md
│   ├── pr-description.md
│   └── code-review-checklist.md    # local copy of the shared checklist
├── src/eShop.Api/                  # NEW (workstream E) — minimal .NET code under review
└── tests/
    ├── unit/                       # per-module: config, textutil, selection, parser, dedupe
    ├── integration/                # full pipeline over recorded fixtures
    ├── contract/                   # schema + import-boundary + exit-code + redaction tests
    └── fixtures/                   # recorded TFS payloads, story #101, PR description
```

**Structure Decision**: Single-project layout with the agent isolated in `review_agent/` beside the
code it reviews, exactly as the brief prescribes, because the demo runs the agent and the reviewed
.NET code from one repo. The package has no imports from `src/` and no knowledge of eShop, so the
later "central repo" rollout is a `git mv` of `review_agent/` plus a pipeline reference — no code
change. `tests/` sits at the repo root rather than inside `review_agent/` so the shipped agent
directory stays deployable as-is.

## Implementation Workstreams

Ordered by dependency. `/speckit-tasks` will decompose these into numbered tasks.

**A. Secure the repo and the config surface** *(blocks everything; do first)*
- A1 — **Untrack `.env`**: add `.gitignore` (`.env`, `__pycache__/`, `.pytest_cache/`, `bin/`,
  `obj/`, `.venv/`), `git rm --cached .env`, keep the local file. History needs no rewrite (the
  committed blob is empty) — verify with `git log -p -- .env`. Then rotate the PAT and LLM key that
  have been sitting in an ignored-but-tracked file, as a precaution.
- A2 — `config.py`: load `.env`, overlay pipeline variables with pipeline precedence, validate
  required values, register every secret value with the redaction filter, expose a frozen config
  object. Fails as `SetupFailure` listing every missing value (FR-003).
- A3 — `errors.py` + exit-code contract: `SetupFailure` → 2, blocking findings → 1, completed or
  degraded → 0.
- A4 — `.env.example` and `README.md` covering every variable including the new ones (R9).

**B. TFS integration** *(depends on A)*
- B1 — `tfs_client.py`: session construction, auth-mode selection (Bearer pipeline token preferred
  over Basic PAT), `truststore` injection, retry/backoff policy, error→taxonomy mapping.
- B2 — Fetches: linked work items, work-item detail, PR detail, iterations, iteration changes, file
  content at a commit, existing threads.
- B3 — Writes: create thread (active), add comment to thread, patch an existing comment.
- B4 — `textutil.py`: HTML→plain-text for work-item fields (stdlib `html.parser`).
- B5 — `checklist.py`: derive the checklist repo name from `CHECKLIST_REPO_URL`, enumerate under
  `CHECKLIST_PATH`, filter blobs by `CHECKLIST_FILE_PATTERN`, fetch each file, combine with a
  per-file source label. Empty/unreachable → degraded, not fatal.

**C. Review content assembly** *(depends on B)*
- C1 — `diff_builder.py`: iteration changes → per-file unified diff computed locally from base and
  iteration blobs, plus a map from diff line to new-file line for comment anchoring (R3).
- C2 — `selection.py`: apply the exclusion list, then the review bound with relevance ordering;
  record every skipped and unreviewed file with its reason (FR-014c, FR-014f).
- C3 — `prompt_builder.py`: assemble the system role, story, combined checklist, declared scope, and
  diff; embed the classification rules verbatim in substance; chunk on file boundaries when oversized.

**D. Review, validation, and feedback** *(depends on C)*
- D1 — `llm_adapter.py`: `call_llm(prompt) -> str` for claude / openai / azure / ollama; normalize to
  raw text. The only module with provider payload knowledge.
- D2 — `parser.py`: validate against `contracts/llm-response.schema.json`, enforce the status and
  severity enums, one stricter retry, then degrade (FR-022).
- D3 — Merge chunk results, dedupe by fingerprint, and derive false coverage claims by intersecting
  the declared "Covered" list with non-`appears_addressed` criteria.
- D4 — `dedupe.py`: fingerprint each finding (line-independent, so a shifted line does not re-post);
  read existing threads, identify the agent's own via the `ai-review:` marker, and split findings into
  new / already-posted (FR-030).
- D5 — `output_layer.py`: render the summary (coverage map, false-coverage callout, skipped and
  unreviewed files) and inline comments; dry-run printing first, then real posting with active status;
  update the single summary comment in place.
- D6 — `main.py`: orchestrate, time each phase, emit the structured run log, set the exit code.

**E. Demo scaffolding for the SC-001 acceptance test** *(depends on nothing; parallel with B–D)*
- E1 — Minimal `src/eShop.Api` with a `CartController.AddItem` that validates quantity, handles
  not-found, and increments quantity — and deliberately omits the stock-limit check.
- E2 — `demo/user-story-101.md` with the five acceptance criteria.
- E3 — `demo/pr-description.md` with a Covered / Not-in-this-PR section that falsely claims stock
  checking is covered and scopes out #102 and #103.
- E4 — `demo/code-review-checklist.md`, including the "business rules from the story are enforced"
  item, to seed the shared `TestMarkDown` repo.
- E5 — Record the fixture payloads for the integration test from a real dry run.

**F. Pipeline and rollout** *(depends on D)*
- F1 — `azure-pipelines.yml`: `trigger: none`, self-hosted pool, install requirements, run the agent,
  pass PR context and `SYSTEM_ACCESSTOKEN` via `env:`, LLM key from a variable group.
- F2 — Document the one-time manual settings: master branch policy → Build Validation; "Allow scripts
  to access the OAuth token"; **Contribute to pull requests** for the build service identity.
- F3 — Run the section 9 acceptance test end to end and confirm all five expectations.

## Phase status

- [x] **Phase 0 — Research**: [research.md](./research.md) — 12 decisions, all Technical Context
  unknowns resolved or converted into a named verification task.
- [x] **Phase 1 — Design & Contracts**: [data-model.md](./data-model.md),
  [contracts/](./contracts/), [quickstart.md](./quickstart.md), `CLAUDE.md` updated.
- [x] **Phase 2 — Tasks**: [tasks.md](./tasks.md) — 92 tasks across 7 phases.
- [x] **Phase 3 — Implementation**: 82 of 92 tasks complete, 322 tests passing. The 10 open tasks each
  need the live TFS collection, real credentials, or a user decision — see the "Blocked on the live
  environment" table in `tasks.md`.

## Post-Design Constitution Re-Check

Re-evaluated after Phase 1, and again against the ratified constitution (v1.0.0):

| Gate | Status after design |
|---|---|
| No hard-coded configuration | **Holds.** Every value in `contracts/cli-contract.md` resolves through `config.py`; no module reads `os.environ` directly. |
| No secret in logs, prompt, or output | **Holds.** Redaction filter wraps every log and print path (R8); prompts carry only story, checklist, description, and diff text. |
| TLS verification never disabled | **Holds.** `truststore` injected in `tfs_client.py` session setup; no `verify` parameter is ever passed. |
| TFS REST in one module | **Holds.** `diff_builder`, `checklist`, and `output_layer` receive data or a client interface; none import `requests`. |
| Provider format in one module | **Holds.** `llm_adapter.call_llm` returns raw text; `parser.py` is provider-blind. |
| `not_addressed` inert | **Holds.** Routing table in `data-model.md` maps it to summary-only, and it is excluded from the blocking-exit set. |
| Non-blocking default | **Holds.** Exit 0 for completed and degraded runs; exit 1 only when `FAIL_ON_CONTRADICTION=true`. |

**One design tension recorded, not resolved by design** (spec decision, already documented in
spec.md SC-009 and Assumptions): comments are posted **active** (FR-024a), so a team that also enables
the "all comments resolved" branch policy will need a human to resolve the summary discussion on every
PR — including clean ones. The design does not work around this because the clarification chose it
deliberately.

## Complexity Tracking

No constitutional violations to justify (no constitution). Three deliberate departures from the
brief's letter, each recorded with its reason:

| Departure from `Req.md` | Why needed | Simpler alternative rejected because |
|---|---|---|
| Five modules beyond the brief's list (`selection.py`, `dedupe.py`, `textutil.py`, `errors.py`, `models.py`) | The clarifications added exclusion + bound (FR-014a–f), incremental re-review (FR-030), and the degraded/setup split (FR-029) — none of which existed when the brief named its modules | Folding them into `main.py`/`output_layer.py` would put the three most test-sensitive rules inside the least testable modules |
| `truststore` as a third runtime dependency | `requests` validates against `certifi`, which cannot contain a private internal CA — without it FR-034 is unsatisfiable except by disabling verification, which FR-034 forbids | `REQUESTS_CA_BUNDLE` pointing at an exported CA file works but is per-machine manual setup that silently rots; kept as the documented fallback |
| Diff text computed locally from blobs rather than read from an endpoint | The iteration `changes` endpoint returns change *entries* (path, changeType), not patch text; no on-prem endpoint reliably returns per-file unified diff on older TFS (R3) | Fetching from `diffs/commits` returns the same entry shape, so it does not avoid the work |
