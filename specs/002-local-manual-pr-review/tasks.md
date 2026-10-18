---
description: "Task list for feature 002 — Local Manual Pull Request Review Run"
---

# Tasks: Local Manual Pull Request Review Run

**Input**: Design documents from `/specs/002-local-manual-pr-review/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Test tasks are **included and mandatory**. The project constitution (v1.0.0) states that each
of its seven principles is "enforced by a test", and `plan.md` § Constitution Check names the specific test
file enforcing each one. Tests are not optional here — they are the enforcement mechanism.

**Organization**: Tasks are grouped by user story so each story can be implemented and tested independently.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story the task belongs to (US1–US5)
- Every task carries an exact file path

## Path Conventions

This feature spans **two repositories**, both cloned side by side under the same workspace root
(`D:/VijayN/Work/Repo/Najm/`). Every path below is prefixed with its repository:

- `CodeReviewAgent/review_agent/…` — the agent package (all implementation)
- `CodeReviewAgent/tests/…` — the test suite (after Phase 1 moves it there)
- `CodeReviewSample/…` — the code under review; only the demo change and the specs live here

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Restore a working test suite and a safe repository, so every later task has a safety net.

**⚠️ T004 is a hard gate.** The suite currently fails collection on all 17 files; until it is green,
nothing in this plan is verifiable and no constitutional principle is actually enforced.

- [X] T001 Move the whole test tree from `CodeReviewSample/tests/` to `CodeReviewAgent/tests/`, preserving `contract/`, `integration/`, `unit/`, `fixtures/`, `conftest.py`, `fakes.py`, and the `__init__.py` files
- [X] T002 Move `CodeReviewSample/pytest.ini` to `CodeReviewAgent/pytest.ini` and confirm `testpaths = tests` still resolves
- [X] T003 Delete `CodeReviewSample/tests/` and `CodeReviewSample/pytest.ini`, and confirm no file in `CodeReviewSample` still imports `review_agent`
- [X] T004 Run `python -m pytest` in `CodeReviewAgent/` and get all 322 existing tests passing — **blocking gate for every task below**
- [X] T005 [P] Create `CodeReviewAgent/.gitignore` covering `.env`, `__pycache__/`, `*.py[cod]`, `.venv/`, `.pytest_cache/`, `*.pem`, `*.key`, `*.pfx` — the repo has none today, so its live `.env` is untracked only by luck
- [X] T006 [P] Remove the 15 committed `review_agent/__pycache__/*.pyc` files from `CodeReviewAgent` with `git rm --cached`, since the agent repo is about to be cloned and executed on every run
- [X] T007 [P] Add `openpyxl` to `CodeReviewAgent/review_agent/requirements.txt` (research R14)
- [X] T008 [P] Extend `CodeReviewAgent/tests/contract/test_output_encoding.py` with the glyphs the real checklist documents contain (`ℹ`, `✖`, `—`, `’`) so a cp1252 console is proven safe

**Checkpoint**: Test suite green, repository safe to commit, spreadsheet dependency available.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Configuration, credentials, entities, and the fetch-and-execute mechanism. Every user story
needs all of it — a run cannot start without knowing which logic to run and which token to use.

**⚠️ CRITICAL**: No user story work begins until this phase is complete.

### Configuration and credentials

- [X] T009 Rewrite variable resolution in `CodeReviewAgent/review_agent/config.py` so the provisioned names are canonical and the 001 names are accepted aliases with lower precedence: `TFS_REPO_CODE_SAMPLE`/`TFS_REPO`, `TFS_REPO_CODE_REVIEW_CodeReviewChecklistT`/`CHECKLIST_REPO` (research R10)
- [X] T010 Add `TFS_REPO_CODE_REVIEW_CodeReviewAgent`, `AGENT_REF` (default `master`), and `CHECKLIST_MAX_CHARS` (default 40000) to `CodeReviewAgent/review_agent/config.py`
- [X] T011 Implement the two-credential model in `CodeReviewAgent/review_agent/config.py`: `read_token` from `TFS_PAT` always required, `post_token` from `TFS_PAT_FOR_PR` (FR-006)
- [X] T012 Make `post_token` required only when the run will post, in `CodeReviewAgent/review_agent/config.py`, so a dry run succeeds without it (FR-008)
- [X] T013 Register both tokens and the LLM key with the redaction registry at load time in `CodeReviewAgent/review_agent/config.py`, before any network or subprocess call (Principle II)
- [X] T014 Update the missing-value error in `CodeReviewAgent/review_agent/config.py` to name every missing value using its provisioned spelling (FR-007)

### Entities

- [X] T015 [P] Add `RunInvocation`, `AgentSource`, and `CredentialSet` frozen dataclasses to `CodeReviewAgent/review_agent/models.py` per data-model.md §1
- [X] T016 [P] Replace `ChecklistFile`/`Checklist` with `ChecklistDocument`, `ChecklistItem`, and `CombinedChecklist` in `CodeReviewAgent/review_agent/models.py` per data-model.md §2
- [ ] T017 (deferred to Phase 3, with its parser and renderer consumers, to keep the suite green between tasks) Change `ChecklistViolation` in `CodeReviewAgent/review_agent/models.py` to carry `item_id`, `item_title`, and `document`, retiring `item`/`source` (research R12)
- [X] T018 Add `agent_source`, `documents`, `dropped_citations`, and `dropped_for_budget` to `ReviewReport` in `CodeReviewAgent/review_agent/models.py`

### Credential-scoped TFS access

- [X] T019 Add a `purpose` argument (`read` | `post`) to `TfsClient.__init__` in `CodeReviewAgent/review_agent/tfs_client.py` so each client instance can reach exactly one token
- [X] T020 Update `CodeReviewAgent/tests/unit/test_auth_modes.py` to cover read-vs-post token selection and a dry run with no posting token present

### Fetching and executing the review logic

- [X] T021 Create `CodeReviewAgent/review_agent/source_fetch.py` as the only module that invokes `git`: shallow clone of a named repo and ref into a temp directory
- [X] T022 Implement credential passing in `CodeReviewAgent/review_agent/source_fetch.py` via a `GIT_ASKPASS` helper with `credential.helper=` forced empty — never in the URL, `argv`, or `.git/config` (research R3)
- [X] T023 Scrub the origin URL after cloning and remove the temp directory on exit in `CodeReviewAgent/review_agent/source_fetch.py`
- [X] T024 Create `CodeReviewAgent/review_agent/bootstrap.py`: load config, fetch the agent repo at `AGENT_REF`, re-execute `python -m review_agent.run` inside the clone with `REVIEW_AGENT_BOOTSTRAPPED=<sha>` (FR-012, research R2)
- [X] T025 Implement the recursion guard and the `--local` override in `CodeReviewAgent/review_agent/bootstrap.py` (FR-012a)
- [X] T026 Map a failed fetch of either repository to a setup failure naming which one failed, in `CodeReviewAgent/review_agent/bootstrap.py` — never a silent fallback to the local copy (FR-014)
- [X] T027 Split the reviewing entry point out of `CodeReviewAgent/review_agent/main.py` into `CodeReviewAgent/review_agent/run.py`, leaving `main.py` as argument parsing plus dispatch to bootstrap
- [X] T028 Construct `AgentSource` from the bootstrap marker and thread it into `ReviewReport` in `CodeReviewAgent/review_agent/run.py` (FR-012b)

### Foundational tests

- [X] T029 [P] Write `CodeReviewAgent/tests/unit/test_source_fetch.py` covering clone invocation shape, askpass handling, origin scrubbing, and temp cleanup
- [X] T030 [P] Write `CodeReviewAgent/tests/unit/test_bootstrap.py` covering re-exec, the recursion guard, `--local`, and fetch-failure mapping
- [X] T031 Extend `CodeReviewAgent/tests/contract/test_no_secrets_leak.py` to assert no token appears in any subprocess `argv`, subprocess environment dump, or git error text
- [X] T032 Extend `CodeReviewAgent/tests/contract/test_tls_never_disabled.py` to assert `http.sslVerify` is never set on any git invocation
- [X] T033 Extend `CodeReviewAgent/tests/contract/test_import_boundaries.py` to assert `git` is invoked only from `source_fetch.py` and no module outside it imports `subprocess` for git (Principle IV)

**Checkpoint**: A run can locate its configuration, choose its credential, fetch its own logic, and report which copy ran. User stories can now begin.

---

## Phase 3: User Story 1 - Preview a checklist review of a real pull request (Priority: P1) 🎯 MVP

**Goal**: One command against a real pull request number prints the complete review — every finding citing
its checklist item and source document — and posts absolutely nothing.

**Independent Test**: Open a pull request in `CodeReviewSample` containing a deliberate checklist violation,
run `python -m review_agent --pr <id>`, confirm the violation is printed with its item and document, and
confirm the pull request has gained no comments.

### Tests for User Story 1

> Write these first and confirm they fail before implementing.

- [X] T034 [P] [US1] Create `CodeReviewAgent/tests/fixtures/docs_builder.py` generating fixture `.xlsx` and `.docx` files with stdlib `zipfile`: banner-then-header sheet, populated `#`, empty `#`, metadata-only sheet, rule-ID tables, a no-rule document, a corrupt file (research R13)
- [X] T035 [P] [US1] Write `CodeReviewAgent/tests/unit/test_checklist_docs_xlsx.py` covering header-row detection at row 2, column synonyms, derived identifiers for the empty-`#` case, and non-qualifying sheets
- [X] T036 [P] [US1] Write `CodeReviewAgent/tests/unit/test_checklist_docs_docx.py` covering rule-marker segmentation, severity capture, heading skips, commentary dropping, and the tolerant fallback
- [ ] T037 [P] [US1] Write `CodeReviewAgent/tests/contract/test_missing_pr_fails_clean.py` asserting a non-existent pull request number exits 2, names the pull request, and performs zero writes (FR-039, SC-005)
- [ ] T038 [P] [US1] Extend `CodeReviewAgent/tests/contract/test_preview_makes_no_writes.py` to assert the posting client is never constructed in a dry run (FR-002)
- [ ] T039 [P] [US1] Write `CodeReviewAgent/tests/integration/test_us1_dry_run.py` driving a full dry run over generated documents and a recorded pull request, asserting citations are present and nothing is posted

### Implementation for User Story 1

- [X] T040 [P] [US1] Create `CodeReviewAgent/review_agent/checklist_docs.py` with the spreadsheet reader: sheet qualification, header scan within 5 rows, synonym column mapping, derived identifiers (contracts/checklist-extraction.md)
- [X] T041 [US1] Add the Word reader to `CodeReviewAgent/review_agent/checklist_docs.py`: rule-ID marker regex, severity cell, body capture to the next marker or `Heading1`, heading skips, commentary dropping
- [X] T042 [US1] Add the tolerant fallback path to `CodeReviewAgent/review_agent/checklist_docs.py` for a Word document yielding zero marked rules (FR-017b)
- [ ] T043 [US1] Rewrite `CodeReviewAgent/review_agent/checklist.py` to discover documents in the cloned checklist repository and delegate per-format reading to `checklist_docs.py`
- [ ] T044 [US1] Implement the character budget in `CodeReviewAgent/review_agent/checklist.py`: cap at `CHECKLIST_MAX_CHARS`, naming every dropped item (research R6)
- [ ] T045 [US1] Make zero extracted rules a setup failure in `CodeReviewAgent/review_agent/checklist.py`, reversing 001's degraded behaviour (FR-019, research R11)
- [ ] T046 [US1] Rewrite `_checklist_section` in `CodeReviewAgent/review_agent/prompt_builder.py` to render compact rule items with their identifiers instead of document text
- [ ] T047 [US1] Instruct the model in `CodeReviewAgent/review_agent/prompt_builder.py` to echo `item_id` exactly as supplied, per the v2 schema
- [ ] T048 [US1] Update `CodeReviewAgent/review_agent/parser.py` to validate against `contracts/llm-response.schema.json` v2, requiring `item_id`, `item_title`, and `document`
- [ ] T049 [US1] Drop violations citing an `item_id` absent from the combined checklist in `CodeReviewAgent/review_agent/parser.py`, counting them as dropped citations (research R12)
- [ ] T050 [US1] Render the finding block with its citation, severity, and location in `CodeReviewAgent/review_agent/output_layer.py` per contracts/cli-contract.md
- [ ] T051 [US1] Add the provenance line and the checklist line to the report header in `CodeReviewAgent/review_agent/output_layer.py` (FR-012b)
- [ ] T052 [US1] Add the report's transparency sections — skipped documents, excluded files, unreviewed files, dropped citations — to `CodeReviewAgent/review_agent/output_layer.py` (Principle VII)
- [ ] T053 [US1] Implement the CLI surface in `CodeReviewAgent/review_agent/main.py`: `--pr` required, `--dry-run` default, `--post`, `--local`, `--quiet` (contracts/cli-contract.md)
- [ ] T054 [US1] Wire exit codes 0 / 2 / 3 in `CodeReviewAgent/review_agent/main.py`, retiring exit 1 for this feature
- [ ] T055 [US1] Orchestrate the dry-run pipeline in `CodeReviewAgent/review_agent/run.py`: config → fetch → checklist → pull request → select → prompt → backend → parse → render
- [ ] T056 [US1] Map a pull request that cannot be found to a setup failure naming it, in `CodeReviewAgent/review_agent/run.py` (FR-039)

**Checkpoint**: A developer can preview a real review safely. This is the MVP — it delivers value with no posting path in place at all.

---

## Phase 4: User Story 2 - Post the review onto the pull request (Priority: P2)

**Goal**: The same findings, posted — one inline comment per located finding plus one summary comment on
every run, each carrying its citation.

**Independent Test**: Take the pull request previewed in US1, run with `--post`, and confirm the same
findings appear as comments with citations intact.

### Tests for User Story 2

- [ ] T057 [P] [US2] Write `CodeReviewAgent/tests/contract/test_dry_run_matches_posted.py` asserting the dry-run text contains the summary body and every inline body the posting path would send (FR-003)
- [ ] T058 [P] [US2] Write `CodeReviewAgent/tests/integration/test_us2_posting.py` covering inline placement, the always-present summary comment, and a clean review that still posts a summary
- [ ] T059 [P] [US2] Add a posting-permission failure case to `CodeReviewAgent/tests/integration/test_us2_posting.py` asserting exit 2, the permission named, and the findings still printed (FR-041)

### Implementation for User Story 2

- [ ] T060 [US2] Construct the posting client with `purpose="post"` only on the posting path in `CodeReviewAgent/review_agent/run.py` (FR-002, research R9)
- [ ] T061 [US2] Render the summary comment — outcome, unlocatable findings, skipped documents, unreviewed files — in `CodeReviewAgent/review_agent/output_layer.py` (FR-035a)
- [ ] T062 [US2] Post exactly one summary comment per run, including for a clean review, from `CodeReviewAgent/review_agent/output_layer.py`
- [ ] T063 [US2] Post one inline comment per located finding, anchored to file and line, from `CodeReviewAgent/review_agent/output_layer.py` (FR-036)
- [ ] T064 [US2] Route findings with no usable location into the summary comment rather than dropping them, in `CodeReviewAgent/review_agent/output_layer.py`
- [ ] T065 [US2] Keep the `ai-review:` identity marker on every posted comment in `CodeReviewAgent/review_agent/output_layer.py` (FR-037)
- [ ] T066 [US2] Map a posting permission error to a setup failure naming the missing permission, while still printing the findings, in `CodeReviewAgent/review_agent/run.py`
- [ ] T067 [US2] Print the `posted: 1 summary + N inline comments` line in `CodeReviewAgent/review_agent/output_layer.py`

**Checkpoint**: Both US1 and US2 work independently. The feature is usable end to end.

---

## Phase 5: User Story 3 - Checklists maintained as Excel and Word documents (Priority: P3)

**Goal**: The quality lead's promises — every document read or explained, rules citable, a new document
picked up with no configuration change.

**Independent Test**: Place several documents of both formats in the checklist repository, run a dry run,
confirm rules from all of them are in force and each finding names its document; then add one more document
and re-run with no configuration change.

**Note**: US1 delivered the readers. This phase delivers the corpus-level guarantees around them.

### Tests for User Story 3

- [ ] T068 [P] [US3] Create two small redacted checklist excerpts (≤ 20 rows / 3 rules, one per format) in `CodeReviewAgent/tests/fixtures/checklist_excerpts/` — the real internal standards documents are never committed (research R13)
- [ ] T069 [P] [US3] Write `CodeReviewAgent/tests/integration/test_us3_documents.py` asserting rules from both formats are combined into one checklist and every finding names its document
- [ ] T070 [P] [US3] Add an "add a document, change no configuration" case to `CodeReviewAgent/tests/integration/test_us3_documents.py` (FR-015, SC-007)
- [ ] T071 [P] [US3] Write `CodeReviewAgent/tests/contract/test_checklist_transparency.py` asserting every document ends `read`, `no_rules`, or `skipped` and is named with a reason in the report (Principle VII)
- [ ] T072 [P] [US3] Cover the failure taxonomy — corrupt, password-protected, unsupported extension, no rules — in `CodeReviewAgent/tests/unit/test_checklist_docs_failures.py` (contracts/checklist-extraction.md)

### Implementation for User Story 3

- [ ] T073 [US3] Merge items whose normalised titles match into one item citing every source document, in `CodeReviewAgent/review_agent/checklist.py` (FR-020)
- [ ] T074 [US3] Record per-document status, skip reason, rule count, and ignored sheets on `ChecklistDocument` in `CodeReviewAgent/review_agent/checklist.py` (FR-018)
- [ ] T075 [US3] Distinguish `no_rules` from `skipped` in `CodeReviewAgent/review_agent/checklist_docs.py` so a content problem is never reported as a technical one
- [ ] T076 [US3] Classify corrupt, encrypted, and unsupported files into skip reasons in `CodeReviewAgent/review_agent/checklist_docs.py` rather than raising
- [ ] T077 [US3] Report every document's outcome in both the console report and the summary comment, in `CodeReviewAgent/review_agent/output_layer.py`

**Checkpoint**: The checklist repository is a first-class, self-describing input.

---

## Phase 6: User Story 4 - Change the model backend by configuration alone (Priority: P4)

**Goal**: Claude and Copilot behind one interface, selected by configuration, with the LLM-agnosticism audit
automated rather than asserted.

**Independent Test**: Run the same pull request twice changing only `LLM_PROVIDER`; confirm both produce the
same shape of review. Separately, confirm every model call passes through the one interface.

### Tests for User Story 4

- [ ] T078 [P] [US4] Write `CodeReviewAgent/tests/contract/test_backend_interface.py` asserting every registered backend satisfies the interface shape (contracts/llm-backend-interface.md)
- [ ] T079 [P] [US4] Add the automated FR-033 audit to `CodeReviewAgent/tests/contract/test_backend_interface.py`: no provider name appears in any module outside `llm_adapter.py`
- [ ] T080 [P] [US4] Add a `FakeBackend` registered at test time and flowed end to end in `CodeReviewAgent/tests/contract/test_backend_interface.py` — the executable proof of FR-032
- [ ] T081 [P] [US4] Assert the unknown-provider message is generated from the registry keys in `CodeReviewAgent/tests/unit/test_llm_adapter.py` (FR-034)

### Implementation for User Story 4

- [ ] T082 [US4] Refactor `CodeReviewAgent/review_agent/llm_adapter.py` from the `_request_for` branch chain to a backend registry, keeping `call_llm`'s signature, retry policy, and error semantics unchanged (research R7)
- [ ] T083 [US4] Express `claude`, `openai`, `azure`, and `ollama` as registry objects in `CodeReviewAgent/review_agent/llm_adapter.py` with no behaviour change — the existing tests are the proof
- [ ] T084 [US4] Add the `copilot` backend to `CodeReviewAgent/review_agent/llm_adapter.py` as an OpenAI-compatible shape with a required, configurable base URL and selectable auth header (research R8)
- [ ] T085 [US4] Validate `requires_key` and `requires_base_url` per backend in `CodeReviewAgent/review_agent/config.py`, failing before any review work (FR-034)
- [ ] T086 [US4] Generate the unsupported-provider error text from the registry keys in `CodeReviewAgent/review_agent/llm_adapter.py` so it cannot drift

**Checkpoint**: Adding a provider is one object plus one registry line — and a test proves it.

---

## Phase 7: User Story 5 - Use the linked user story as review context (Priority: P5)

**Goal**: Where a pull request links a work item, the review reasons with it; where it does not, the review
says so and carries on.

**Independent Test**: Review one pull request with a linked work item and one without; confirm the first uses
the story and the second completes normally stating no story was linked.

**Note**: 001 already implements work-item fetching and HTML-to-text reduction. These tasks verify that path
against this feature's entry point and close its gaps.

### Tests for User Story 5

- [ ] T087 [P] [US5] Write `CodeReviewAgent/tests/integration/test_us5_story_context.py` covering a linked work item used as context and an unlinked pull request completing as checklist-only
- [ ] T088 [P] [US5] Add an unreadable-work-item case to `CodeReviewAgent/tests/integration/test_us5_story_context.py` asserting the review continues and says the story could not be read (FR-024)

### Implementation for User Story 5

- [ ] T089 [US5] Thread linked work items into the review path of `CodeReviewAgent/review_agent/run.py`, reusing the existing fetch and `textutil` reduction (FR-023)
- [ ] T090 [US5] State "no story was linked" or "the story could not be read" in the report and summary comment, in `CodeReviewAgent/review_agent/output_layer.py` (FR-024)

**Checkpoint**: All five user stories are independently functional.

---

## Phase 8: Polish, Documentation & Live Validation

**Purpose**: Cross-cutting concerns and the end-to-end proof the spec requires.

### Documentation

- [ ] T091 [P] Rewrite `CodeReviewAgent/review_agent/.env.example` to the provisioned variable names, both tokens, and the new values, with no real credentials
- [ ] T092 [P] Update `CodeReviewAgent/review_agent/README.md` for the manual flow, `--local`, exit codes, and the checklist document contract
- [ ] T093 [P] Add a `CHECKLIST_FORMAT.md` in `CodeReviewChecklist/` telling quality leads what makes a document readable, derived from contracts/checklist-extraction.md

### Cross-cutting verification

- [ ] T094 Run the full suite in `CodeReviewAgent/` and confirm every constitutional principle has a passing enforcing test per plan.md § Constitution Check
- [ ] T095 [P] Verify the dry-run performance budget from plan.md (≤ 15 s fetch, ≤ 5 s parse, ≤ 2 min total) by timing the phase log of `python -m review_agent --pr <id>` run from `CodeReviewAgent/`
- [ ] T096 [P] Confirm the combined checklist built from `CodeReviewChecklist/*.xlsx` and `*.docx` fits `CHECKLIST_MAX_CHARS` with an empty `dropped_for_budget` (research R6 predicts ~22,000 chars)

### Live validation (needs the real collection, real credentials, and an open pull request)

- [ ] T097 Confirm the Copilot endpoint, credential, and model name with the platform owner, then record them in `CodeReviewAgent/review_agent/.env.example` and set `LLM_BASE_URL` in the local `.env` — the one open environment fact (research R8)
- [ ] T098 Make a change in `CodeReviewSample/src/eShop.Api/` that breaks a known rule from the real checklist corpus and open a pull request for it
- [ ] T099 Run `python -m review_agent --pr <id>` from `CodeReviewAgent/` and confirm the finding prints with its citation and the pull request gains zero comments (SC-002, SC-003, SC-004)
- [ ] T100 Run `python -m review_agent --pr <id> --post` from `CodeReviewAgent/` and confirm the inline comment and the summary comment appear on the pull request with citations intact (SC-002, SC-003)
- [ ] T101 Run `python -m review_agent --pr 999999 --post` from `CodeReviewAgent/` and confirm exit 2, the number named in the message, and zero comments anywhere (SC-005)
- [ ] T102 Switch `LLM_PROVIDER` in `.env` between `claude` and `copilot`, re-run T099, and confirm `git status` in `CodeReviewAgent/` shows zero changes to `review_agent/run.py`, `checklist*.py`, `prompt_builder.py`, `parser.py`, or `output_layer.py` (SC-008)
- [ ] T103 Commit a sixth document to `CodeReviewChecklist/` and confirm its rules apply on the next run with no edit to `.env` or to `CodeReviewAgent/review_agent/` (SC-007)
- [ ] T104 Inspect a complete dry run and a complete posting run — console output, any log file, the posted comments, and the cloned `.git/config` under the temp dir — for any credential (SC-010)
- [ ] T105 Confirm every changed file in the reviewed pull request appears in the report of T100 as reviewed, excluded, or unreviewed with a reason — none silently absent (SC-012)

---

## Dependencies & Execution Order

### Phase dependencies

- **Phase 1 (Setup)**: no dependencies. **T004 gates everything else in the plan.**
- **Phase 2 (Foundational)**: depends on Phase 1. Blocks all user stories.
- **Phase 3 (US1)**: depends on Phase 2. The MVP.
- **Phase 4 (US2)**: depends on Phase 3 — it posts what US1 renders.
- **Phase 5 (US3)**: depends on Phase 3 for the readers; independent of US2.
- **Phase 6 (US4)**: depends on Phase 2 only; can run in parallel with Phases 3–5 once foundations land.
- **Phase 7 (US5)**: depends on Phase 3.
- **Phase 8**: depends on the stories being validated; T097 should start early since it waits on a person.

### User story dependencies

- **US1 (P1)**: needs Foundational only. Fully independent.
- **US2 (P2)**: builds on US1's renderer — the parity guarantee (FR-003) is what couples them.
- **US3 (P3)**: builds on US1's readers; adds corpus-level guarantees. Independently testable.
- **US4 (P4)**: genuinely independent — the backend boundary touches no story logic. Good parallel work.
- **US5 (P5)**: builds on US1's pipeline; smallest phase, mostly verification of existing 001 behaviour.

### Within each story

Tests are written first and must fail. Then: readers/models → orchestration → rendering → CLI wiring.

### Parallel opportunities

- Phase 1: T005, T006, T007, T008 in parallel after T004.
- Phase 2: T015/T016 in parallel; T029–T033 in parallel once their subjects exist.
- Phase 3: all six test tasks (T034–T039) in parallel; T040 parallel with the other readers' tests.
- Phase 6 can be staffed entirely in parallel with Phases 3–5.
- Phase 8: T091–T093 and T095/T096 in parallel.

---

## Parallel Example: User Story 1

```bash
# Write the failing tests together:
Task: "Create fixture document generator in CodeReviewAgent/tests/fixtures/docs_builder.py"
Task: "Write xlsx reader tests in CodeReviewAgent/tests/unit/test_checklist_docs_xlsx.py"
Task: "Write docx reader tests in CodeReviewAgent/tests/unit/test_checklist_docs_docx.py"
Task: "Write missing-PR contract test in CodeReviewAgent/tests/contract/test_missing_pr_fails_clean.py"
Task: "Write dry-run integration test in CodeReviewAgent/tests/integration/test_us1_dry_run.py"
```

---

## Implementation Strategy

### MVP first (User Story 1 only)

1. Phase 1 — restore the test suite. **Do not skip; nothing is verifiable until T004 is green.**
2. Phase 2 — configuration, credentials, entities, fetch-and-execute.
3. Phase 3 — the dry-run pipeline.
4. **Stop and validate**: T099 against a real pull request. A developer can now preview a review with zero
   risk of commenting on anything.

### Incremental delivery

1. Setup + Foundational → a run can start and say what it is running.
2. US1 → safe preview (**MVP**).
3. US2 → comments land on the pull request.
4. US3 → the checklist corpus becomes self-describing.
5. US4 → backend portability, with the audit automated.
6. US5 → story context sharpens the review.

### Parallel team strategy

After Phase 2, one developer can take US4 (backend registry) end to end while another works US1 → US2 → US3;
the two touch disjoint modules (`llm_adapter.py` versus `checklist*.py` / `output_layer.py`).

---

## Notes

- `[P]` means a different file with no incomplete dependency.
- Every task names its repository — half the work is in `CodeReviewAgent`, not in the repo you are reading.
- The suite is currently **red** (17 collection errors). T001–T004 exist to fix that, and every claim this
  plan makes about enforcement is false until they are done.
- Commit after each task or logical group. `CodeReviewAgent` has no `.gitignore` until T005 — do not run
  `git add -A` there before it lands, or the live `.env` goes with it.
- T097 waits on another person; raise it on day one rather than at Phase 8.
