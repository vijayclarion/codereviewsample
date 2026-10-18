# Implementation Plan: Local Manual Pull Request Review Run

**Branch**: `002-local-manual-pr-review` | **Date**: 2026-09-15 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/002-local-manual-pr-review/spec.md`

## Summary

Turn the existing review agent into something a developer can actually run: one command, a pull request
number, a dry run by default. The run fetches the review logic from the agent repository and executes *that*
copy, fetches the checklist repository, extracts rules from five Excel and Word documents into one combined
checklist, reviews the pull request's changes against it, and posts inline comments plus one summary comment
— every finding citing a rule identifier and the document it came from.

**This is a delta on working code, not a greenfield build.** Feature 001 is implemented in the
`CodeReviewAgent` repository: 15 modules, 3,291 lines, with the exclusion list, review bound, summary +
inline posting, work-item context, dry-run mode, redaction registry, and TLS-via-trust-store already done.
Roughly 60% of this spec is therefore already satisfied. The real work is five changes: **fetch-and-execute
bootstrapping**, **Excel/Word rule extraction** replacing markdown reading, **two separate credentials**,
**a pluggable backend registry with a Copilot backend**, and **citations that carry an item identifier**.

Two findings from inspecting the actual repositories shape everything below and are not optional
housekeeping: the test suite is **entirely red** (the tests live in this repo, the code lives in another, 17
collection errors), and one real checklist document would be **rejected by the spec as written** — amended
below with the reason.

## Preconditions (verified against the real repositories, 2026-09-15)

| Fact | Evidence | Consequence |
|---|---|---|
| Agent source is in a sibling repo, already implemented | `../CodeReviewAgent`, 15 modules, 3,291 lines | 002 is a delta; the FR-033 audit has real code to audit |
| Checklist repo holds 3 `.xlsx` + 2 `.docx` | `../CodeReviewChecklist` | SC-006 is satisfiable on the real corpus |
| **The test suite does not run at all** | `pytest --collect-only` → *17 errors during collection* | No safety net exists for any refactor below. Task A1 |
| One spreadsheet has an empty identifier column | `Non Functional Requirements v2.0.xlsx`, `#` blank in all data rows | FR-017a amended — see [Spec Adjustments](#spec-adjustments) |
| Word rules carry stable IDs (`SEC-WEB-01`) | Rule-marker tables in both `.docx` | Citations get real identifiers, not invented ones |
| Combined raw document text ≈ 115,000 chars | 52,290 + 54,852 + ~170 spreadsheet rows | Checklist must be compacted before prompting (research R6) |
| `.env` is untracked and gitignored | `git ls-files --error-unmatch .env` → not tracked | **The `CLAUDE.md` warning about a tracked `.env` is stale.** No remediation needed; note corrected in this plan |
| Provisioned `.env` names match no variable `config.py` reads | 9 keys vs `TFS_REPO`, `CHECKLIST_REPO`… | Config aliasing is a required task (research R10) |
| Console is cp1252; documents contain `ℹ ✖ — ’` | `UnicodeEncodeError` in a probe | The existing output-encoding contract test must be extended with real glyphs |

## Technical Context

**Language/Version**: Python 3.11+ (3.13.14 local). No 3.12+ syntax, so the agent stays runnable on the
older interpreter a build agent may carry when this returns to a pipeline.
**Primary Dependencies**: `requests`, `python-dotenv`, `truststore` (all existing) + **`openpyxl`** (new,
pure-Python, read-only mode). Word documents are read with stdlib `zipfile` + `xml.etree` — no `python-docx`,
no `lxml` (research R14). `git` CLI is a runtime prerequisite, invoked only from `source_fetch.py`.
**Storage**: None. Stateless per run; temp clones are removed on exit.
**Testing**: `pytest` + `responses`, relocated into the agent repository (task A1). Document readers are
tested against **generated** fixtures plus two small redacted excerpts (research R13) — the real internal
standards documents are not committed.
**Target Platform**: Developer workstations (Windows 11, cp1252 console).
**Project Type**: Single-project command-line tool, living in its own repository and fetched at run time.
**Performance Goals**: A dry run against a typical pull request completes in ≤ 2 minutes: ≤ 15 s to fetch
both repositories (shallow), ≤ 5 s to parse five documents, ≤ 90 s of model time, ≤ 10 s to post.
**Constraints**: No secret in code, logs, console, prompt, posted comments, `argv`, or `.git/config`
(Principle II, research R3). TLS verification never disabled, for `requests` *and* `git` (Principle III).
Dry run performs zero writes and never constructs the posting client (FR-002). Combined checklist capped at
60,000 characters with every dropped item named (research R6; the real corpus measures 48,485).
**Scale/Scope**: One pull request per run. 5 checklist documents → ~150 rule items. Estimated delta: **4 new
modules** (`bootstrap.py`, `source_fetch.py`, `checklist_docs.py`, `run.py`), **7 modified**, ~1,100 new
lines, ~35 new tests on top of the 322 relocated ones.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design — see below.*

`.specify/memory/constitution.md` v1.0.0. **Gate result: PASS**, with one pre-existing breach this feature
must repair before it can honestly claim any of them.

| Principle | This design | Enforced by |
|---|---|---|
| I. Configuration Is External | New values (`AGENT_REF`, `TFS_PAT_FOR_PR`, `CHECKLIST_MAX_CHARS`, provisioned repo names) all resolve through `config.py`; `bootstrap.py` receives a frozen config | `test_import_boundaries.py` |
| II. Secrets Never Surface | Both PATs registered before the first `git` call; `GIT_ASKPASS` keeps the token out of `argv` and `.git/config`; origin URL scrubbed post-clone | `test_no_secrets_leak.py`, extended to subprocess `argv` + env |
| III. Transport Security Is Not Optional | `truststore` for `requests` (existing); git uses schannel → machine trust store. `http.sslVerify` never set | `test_tls_never_disabled.py`, extended to git invocations |
| IV. Replaceable Boundaries | TFS REST stays only in `tfs_client.py`; **git** only in `source_fetch.py`; **document formats** only in `checklist_docs.py`; provider wire format only in `llm_adapter.py` | `test_import_boundaries.py` + `test_backend_interface.py` (new) |
| V. Partial Work Is Not A Defect | `not_addressed` still routes to summary only; unchanged by this feature | `test_routing_not_addressed.py` |
| VI. The Human Decides The Merge | Exit 1 is retired here; no vote, approve, or complete path exists or may be added | `test_preview_makes_no_writes.py` |
| VII. Silence Must Never Read As Success | Every document ends `read`/`no_rules`/`skipped` and is named; dropped citations counted; budget-dropped items named; degraded run exits 3, not 0 | `test_checklist_transparency.py` (new), `test_bound_transparency.py` |

**Pre-existing breach, repaired by task A1**: the constitution says each principle is "enforced by a test",
but no test currently runs — `tests/` sits in `CodeReviewSample` while `review_agent/` sits in
`CodeReviewAgent`, so collection fails on all 17 files. Until A1 lands, every row above is an intention, not
an enforcement. **No other task in this plan may start before A1 is green.**

**Note on Principle VI and exit codes**: 001 used exit 1 for blocking findings. This feature is explicitly
non-gating (spec Out of Scope), so exit 1 is unused and exit 3 is introduced for a degraded run. That is a
strengthening of Principle VII, not a weakening of VI: a run that produced no review must not be reported
with the same code as one that did.

## Spec Adjustments

One requirement was amended during planning, with the clarification's intent preserved:

| Requirement | Change | Why |
|---|---|---|
| **FR-017a** | The item-identifier column is no longer required; only the rule-text column is. Identifiers are derived when absent | `Non Functional Requirements Checklist v2.0.xlsx` has a `#` column that is **empty in every data row**. The rule as clarified would reject a valid, in-use checklist and fail SC-006 on the real corpus. The clarification's intent — "don't guess at layouts" — is fully served by requiring the rule-text column; deriving an identifier guesses nothing. Word documents already derive identifiers under FR-017b |

Recorded in `spec.md` under both FR-017a and the Clarifications session, and in research R4.

## Project Structure

### Documentation (this feature)

```text
specs/002-local-manual-pr-review/
├── plan.md                        # This file
├── spec.md                        # Feature spec (5 clarifications + 1 planning amendment)
├── research.md                    # Phase 0 — 14 decisions against the real repositories
├── data-model.md                  # Phase 1 — entities, states, routing, config surface
├── quickstart.md                  # Phase 1 — first dry run, troubleshooting
├── contracts/                     # Phase 1
│   ├── cli-contract.md            # CLI surface, exit codes, output contract
│   ├── checklist-extraction.md    # What a checklist document must look like
│   ├── llm-backend-interface.md   # The backend interface; the FR-033 audit
│   └── llm-response.schema.json   # v2 — citations carry item_id + document
├── checklists/
│   └── requirements.md            # Spec quality checklist (16/16)
└── tasks.md                       # Phase 2 (/speckit.tasks — NOT created here)
```

### Source code — **two repositories**

```text
CodeReviewAgent/                       # all code and all tests land here
├── review_agent/
│   ├── bootstrap.py                   # NEW  fetch agent source, re-exec, provenance (FR-012)
│   ├── run.py                         # NEW  the reviewing entry point the fetched copy executes
│   ├── source_fetch.py                # NEW  ONLY module that invokes git (Principle IV)
│   ├── checklist_docs.py              # NEW  ONLY module that knows .xlsx/.docx (Principle IV)
│   ├── checklist.py                   # CHANGED  markdown reading → document orchestration
│   ├── config.py                      # CHANGED  two tokens, provisioned names + aliases
│   ├── llm_adapter.py                 # CHANGED  branch chain → backend registry + copilot
│   ├── models.py                      # CHANGED  ChecklistItem/Document, AgentSource, citations
│   ├── parser.py                      # CHANGED  v2 schema; drop unknown item_id
│   ├── prompt_builder.py              # CHANGED  render compact rule items, not documents
│   ├── output_layer.py                # CHANGED  render citations; provenance line
│   ├── main.py                        # CHANGED  --local, exit 3, thin entry
│   ├── tfs_client.py                  # CHANGED  purpose=read|post, one token each
│   ├── errors.py, dedupe.py, diff_builder.py, selection.py, textutil.py   # unchanged
│   ├── requirements.txt               # + openpyxl
│   └── .env.example                   # rewritten to provisioned names
└── tests/                             # MOVED here from CodeReviewSample (task A1)
    ├── contract/                      # + test_backend_interface, test_dry_run_matches_posted,
    │                                  #   test_missing_pr_fails_clean, test_checklist_transparency
    ├── integration/                   # + end-to-end over generated documents
    ├── unit/                          # + test_checklist_docs (xlsx/docx), test_bootstrap,
    │                                  #   test_source_fetch
    └── fixtures/
        ├── docs_builder.py            # NEW  generates fixture .xlsx/.docx with stdlib zipfile
        └── checklist_excerpts/        # NEW  two small redacted real-shaped documents

CodeReviewSample/                      # this repo — the code under review
├── src/eShop.Api/                     # the demo target; a deliberate violation is added for the E2E test
├── specs/002-local-manual-pr-review/  # this feature's documentation
└── tests/                             # REMOVED by task A1 (moves to the agent repo)
```

**Structure Decision**: Code and tests live together in `CodeReviewAgent`, because the constitution's
principles are enforced *by tests* and a test that cannot import the code enforces nothing. This also matches
FR-012: the repository that gets fetched and executed is the repository that carries its own proof of
correctness. `CodeReviewSample` keeps only the code under review and this feature's specs.

## Implementation Workstreams

Ordered by dependency. `/speckit.tasks` will decompose these.

**A. Restore the safety net** *(blocks everything)*
- **A1** — Move `tests/`, `pytest.ini`, and `requirements-dev.txt` into `CodeReviewAgent`; delete them from
  `CodeReviewSample`; run the suite until all 322 tests pass. **Nothing else starts until this is green.**
- A2 — Extend `test_output_encoding.py` with the real glyphs (`ℹ ✖ — ’`) found in the documents.

**B. Configuration and credentials** *(depends on A)*
- B1 — `config.py`: provisioned names as canonical, 001 names as aliases, precedence to the provisioned ones.
- B2 — Two-credential model: `read_token` always, `post_token` only for a posting run (FR-006, FR-008).
- B3 — `TfsClient(purpose=...)`; the dry-run path never constructs the posting client — with the contract
  test that proves it.
- B4 — New values: `AGENT_REF`, `CHECKLIST_MAX_CHARS`; rewrite `.env.example`; extend the missing-value
  error to name provisioned spellings.

**C. Fetching and executing the review logic** *(depends on B)*
- C1 — `source_fetch.py`: shallow clone via `GIT_ASKPASS`, `credential.helper=` empty, origin URL scrubbed,
  temp dir cleaned up. The only module allowed to invoke `git`.
- C2 — `bootstrap.py`: fetch, re-exec `run.py` with `REVIEW_AGENT_BOOTSTRAPPED=<sha>`, recursion guard,
  `--local` override.
- C3 — `AgentSource` + provenance line in every report and summary comment (FR-012b).
- C4 — Fetch failure → setup failure naming which repository failed; never a silent fallback to local (FR-014).

**D. Reading the checklist documents** *(depends on B; parallel with C)*
- D1 — `checklist_docs.py` XLSX reader: header-row scan, synonym column mapping, derived identifiers,
  sheet qualification (research R4).
- D2 — `checklist_docs.py` DOCX reader: rule-ID marker segmentation, severity capture, heading skips,
  commentary dropping, tolerant fallback (research R5).
- D3 — `checklist.py`: orchestrate discovery → per-document status → combine → merge duplicates (FR-020) →
  apply the character budget with named drops.
- D4 — Zero rules → setup failure (FR-019, research R11).
- D5 — `fixtures/docs_builder.py` + the redacted excerpts; unit tests for every row of the failure taxonomy.

**E. Review, citations, and backends** *(depends on C and D)*
- E1 — `llm_adapter.py`: registry refactor; `claude`/`openai`/`azure`/`ollama` become registry objects with
  no behaviour change (the existing tests are the proof).
- E2 — `copilot` backend (research R8).
- E3 — `test_backend_interface.py`: interface shape, provider names confined to `llm_adapter.py`, error
  message generated from the registry, and a `FakeBackend` flowing end to end — the automated FR-033 audit.
- E4 — `prompt_builder.py`: render compact rule items with identifiers; instruct the model to echo
  `item_id` exactly.
- E5 — `parser.py` + v2 schema: require `item_id`/`item_title`/`document`; drop violations citing unknown
  identifiers and count them.
- E6 — `output_layer.py`: render citations in inline and summary comments; add the provenance and checklist
  lines; keep dry-run and posted text identical.

**F. End-to-end validation** *(depends on E)*
- F1 — Confirm the Copilot endpoint and credential with the platform owner (the one open environment fact).
- F2 — Make a change in `CodeReviewSample` that breaks a known checklist rule; open a pull request.
- F3 — Dry run: findings printed with citations, zero comments on the pull request (SC-002, SC-004).
- F4 — Real run: inline comments + one summary comment, citations intact (SC-002, SC-003).
- F5 — Non-existent pull request number: exit 2, message names it, zero comments anywhere (SC-005).
- F6 — Switch `LLM_PROVIDER` between the two backends; confirm zero files of review logic changed (SC-008).
- F7 — Add a sixth checklist document; confirm its rules apply with no configuration change (SC-007).
- F8 — Inspect a full dry run and a full posting run for credential leakage (SC-010).

## Phase status

- [x] **Phase 0 — Research**: [research.md](./research.md) — 14 decisions; one open environment fact (R8).
- [x] **Phase 1 — Design & Contracts**: [data-model.md](./data-model.md), [contracts/](./contracts/),
  [quickstart.md](./quickstart.md), `CLAUDE.md` updated.
- [x] **Phase 2 — Tasks**: [tasks.md](./tasks.md) — 105 tasks across 8 phases.
- [ ] **Phase 3 — Implementation**: in progress — Phases 1 and 2 complete, 369 tests passing.

## Post-Design Constitution Re-Check

| Principle | Status after design |
|---|---|
| I. Configuration Is External | **Holds.** Every new value in `data-model.md` §6 resolves through `config.py`; `bootstrap.py` takes a frozen config |
| II. Secrets Never Surface | **Holds, with a new risk surface.** `git` is the first subprocess this agent has ever run; R3 keeps the token out of `argv`, `.git/config`, and the credential store, and the leak test is extended to cover subprocess env and argv |
| III. Transport Security Is Not Optional | **Holds.** Both HTTP paths use the machine trust store; no verification switch exists for either |
| IV. Replaceable Boundaries | **Strengthened.** Two new boundaries (git, document formats) isolated to one module each, and the backend boundary upgraded from a branch chain to an asserted interface |
| V. Partial Work Is Not A Defect | **Holds.** Untouched by this feature |
| VI. The Human Decides The Merge | **Holds.** Exit 1 unused; no vote or complete path |
| VII. Silence Must Never Read As Success | **Strengthened.** Three new silences closed: an unreadable document, a hallucinated citation, and a review that never happened (exit 3) |

## Complexity Tracking

No constitutional violations to justify. Four deliberate departures, each recorded with its reason:

| Departure | Why needed | Simpler alternative rejected because |
|---|---|---|
| Re-exec bootstrapping (2 entry points) | FR-012 requires the *fetched* copy to run; `sys.path` tricks leave the local package's modules already imported and review with a mixture | Import-path injection was tried on paper and fails the very ambiguity the clarification resolved |
| `openpyxl` as a fourth runtime dependency | Shared strings, merged banner cells, and cell typing are a real source of silently wrong values | Hand-rolling xlsx parsing on stdlib — accepted for `.docx`, where the probe proved 30 lines suffice, but not for spreadsheets |
| Checklist compaction before prompting | 115,000 chars of document text × every chunk is ~90,000 tokens of prose per run | Sending whole documents also couples the agent to large-context models, undermining FR-031 |
| Exit code 3 for a degraded run | A manual run has a human reading the terminal; reporting "no review happened" as success is the failure Principle VII names | Reusing 001's exit 0 hides a non-review behind a success code when nothing is gating a merge anyway |
