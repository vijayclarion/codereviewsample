---
description: "Task list for AI Code Review Agent for On-Prem TFS Pull Requests"
---

# Tasks: AI Code Review Agent for On-Prem TFS Pull Requests

**Input**: Design documents from `/specs/001-ai-code-review-agent/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: **Included.** Not a default TDD assumption — plan.md specifies the testing approach
(`pytest` + `responses`, no live network) and, more importantly, makes its seven gates test-enforced
(six contract test files; `test_import_boundaries.py` covers three gates on its own). Those gates are
now ratified as the project constitution v1.0.0, whose "a principle without a test is a wish" rule
makes these tests mandatory rather than optional. SC-004, SC-006, SC-008, SC-010, and SC-011 are unverifiable without them.
Where a test only restates a requirement it is a plain unit test; where it enforces a gate it is a
contract test.

**Organization**: Grouped by user story so each is independently implementable and testable. The
demo scaffolding sits in Setup because it is the *target* of the review, has no code dependencies, and
US1's acceptance test (SC-001) cannot run without it.

**Test file paths**: tests are consolidated per user story (`test_us1_review.py`,
`test_us2_checklists.py`, `test_us3_posting.py`) rather than one file per task — a task's path names
where its assertions actually live, not a file dedicated to it.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1–US4)
- Exact file paths in every description

## Path Conventions

Single project at repository root, per plan.md: agent in `review_agent/`, tests in `tests/`, demo
target in `src/eShop.Api/` and `demo/`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Make the repo safe to commit to, create the skeleton, and build the demo target the
acceptance test reviews.

**⚠️ T001 BLOCKS EVERY COMMIT IN THIS FEATURE.** `.env` is tracked and `origin` is the eShop repo on
TFS. The committed blob is empty, so nothing has leaked, but the working copy holds a live PAT and LLM
key. Do not run `/speckit-git-commit` or `git commit -a` before T001.

- [X] T001 Untrack the credential file: create `.gitignore` (`.env`, `.venv/`, `__pycache__/`, `.pytest_cache/`, `bin/`, `obj/`), run `git rm --cached .env`, and confirm with `git log -p -- .env` that no secret exists in history
- [ ] T002 Rotate the TFS PAT and the LLM API key that were held in the tracked `.env`, then update the local untracked `.env` with the new values (manual, outside the repo)
- [X] T003 [P] Create the agent package skeleton: `review_agent/__init__.py`, `review_agent/requirements.txt` (`requests`, `python-dotenv`, `truststore`), `review_agent/requirements-dev.txt` (`pytest`, `responses`)
- [X] T004 [P] Create the test tree with shared fixtures: `tests/conftest.py`, `tests/unit/`, `tests/integration/`, `tests/contract/`, `tests/fixtures/`
- [X] T005 [P] Create `review_agent/.env.example` with every variable from `contracts/cli-contract.md`, redacted values, and `CHECKLIST_BRANCH=master` (per research.md R9)
- [X] T006 [P] Create the demo target under review: minimal `src/eShop.Api/Controllers/CartController.cs` whose `AddItem` validates quantity, handles product-not-found, increments an existing line, and recalculates the total — and deliberately omits any stock-limit check
- [X] T007 [P] Create `demo/user-story-101.md` — "Add items to shopping cart" with the five acceptance criteria, criterion 4 being the stock limit
- [X] T008 [P] Create `demo/pr-description.md` with a Covered / Not-in-this-PR section that falsely claims stock checking is covered and scopes out work items #102 and #103
- [X] T009 [P] Create `demo/code-review-checklist.md` including the item "business rules from the story are enforced"

**Checkpoint**: Repo safe to commit; skeleton and demo target exist.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Configuration, error taxonomy, secret hygiene, and the TFS transport — every user story
needs all four.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T010 Implement the error taxonomy in `review_agent/errors.py`: `SetupFailure` (exit 2) and `DegradedReview` (exit 0), each carrying a cause and the config value or permission to correct, per FR-029
- [X] T011 Implement `.env` and pipeline-variable loading with pipeline→`.env`→default precedence and strict boolean parsing (`true/false`, `1/0`, `yes/no`, `on/off`; anything else is a named validation error) in `review_agent/config.py`
- [X] T012 Implement configuration validation in `review_agent/config.py`: report **every** missing or invalid value in one `SetupFailure` message (FR-003), including the conditional rules for `LLM_API_KEY` and `LLM_BASE_URL`
- [X] T013 Implement auth-mode selection in `review_agent/config.py`: Bearer when `SYSTEM_ACCESSTOKEN` is **non-empty**, else Basic PAT — an empty-string token counts as absent (research.md R2)
- [X] T014 Implement the secret registry, `redact()`, and the single `emit()` output path in `review_agent/config.py`; override `__repr__` on the config dataclass to print `***` for every secret field (research.md R8)
- [X] T015 Implement session construction in `review_agent/tfs_client.py`: `truststore.inject_into_ssl()` at startup, auth header per request, `Accept: application/json`, identifying `User-Agent`, and never a `verify=` argument (FR-034, R7)
- [X] T016 Implement the retry and error-mapping policy in `review_agent/tfs_client.py`: 3 attempts with 1/2/4 s backoff honouring `Retry-After` on 429/502/503/504 and connection errors; no retry on other 4xx; map every response to the taxonomy per the table in `contracts/tfs-endpoints.md`, including HTML-sign-in-page detection
- [X] T017 [P] Implement HTML-to-plain-text conversion in `review_agent/textutil.py` using stdlib `html.parser` and `html.unescape` — `<br>`, `</p>`, `</div>`, `</li>` to newlines, `<li>` to `- ` (FR-009)
- [X] T018 Implement the CLI surface in `review_agent/main.py`: `--dry-run`, `--post`, `--pr`, `--help`, `--version`, the mutual exclusion of `--dry-run`/`--post`, the phase-log line format, and the 0/1/2 exit-code mapping from `contracts/cli-contract.md`
- [X] T019 [P] Unit tests for configuration in `tests/unit/test_config.py`: precedence, boolean parsing, all-missing-at-once reporting, conditional requirements
- [X] T020 [P] Unit tests for HTML stripping in `tests/unit/test_textutil.py` covering entities, nested tags, and list conversion
- [X] T021 [P] Contract test in `tests/contract/test_no_secrets_leak.py`: assert each registered secret's literal value appears nowhere in captured stdout/stderr for a full fixture run (SC-004, FR-006)
- [X] T022 [P] Contract test in `tests/contract/test_tls_never_disabled.py`: assert no `verify=False` or `verify =` occurrence anywhere in `review_agent/`, and that `truststore` injection runs before the first session is built (FR-034)
- [X] T023 [P] Contract test in `tests/contract/test_import_boundaries.py`: assert only `tfs_client.py` and `llm_adapter.py` import `requests`, and no module outside `config.py` reads `os.environ` (FR-001, FR-032)

**Checkpoint**: Configuration, errors, secret hygiene, and TFS transport ready — user stories can begin.

---

## Phase 3: User Story 1 - Story-coverage review with false-coverage detection (Priority: P1) 🎯 MVP

**Goal**: Point the agent at a pull request with a linked story and get a printed coverage
classification for every acceptance criterion, with any false coverage claim called out as the headline.

**Independent Test**: Run `python review_agent/main.py --dry-run` against the demo PR. It must print
criteria 1/2/3/5 as `appears_addressed`, criterion 4 as a false coverage claim, and flag none of the
scoped-out follow-ups — posting nothing.

### Tests for User Story 1

- [X] T024 [P] [US1] Record TFS response fixtures for the demo PR (PR detail, linked work items, work item #101, iterations, iteration changes, file blobs) in `tests/fixtures/pr_demo/`
- [X] T025 [P] [US1] Integration test in `tests/integration/test_us1_review.py`: criteria 1, 2, 3, 5 → `appears_addressed`; criterion 4 → `not_addressed` (spec US1 scenario 1)
- [X] T026 [P] [US1] Integration test in `tests/integration/test_us1_review.py`: criterion 4 surfaces as a false coverage claim because the description claims it (spec US1 scenario 2, SC-001)
- [X] T027 [P] [US1] Integration test in `tests/integration/test_us1_review.py`: work items #102 and #103 produce no findings (spec US1 scenario 3, FR-018)
- [X] T028 [P] [US1] Unit test in `tests/unit/test_routing_not_addressed.py`: `not_addressed` yields no inline comment and never contributes to a blocking exit (FR-016, SC-006)
- [X] T029 [P] [US1] Integration test in `tests/integration/test_us1_review.py`: zero linked work items → checklist-only review with an explicit note, exit 0 (FR-008, spec US1 scenario 5)
- [X] T030 [P] [US1] Contract test in `tests/contract/test_preview_makes_no_writes.py`: preview mode issues zero non-GET requests (FR-007, FR-026)
- [X] T031 [P] [US1] Unit tests in `tests/unit/test_parser.py` against `contracts/llm-response.schema.json`: valid payload, bad `status` enum, bad `severity` enum, missing top-level key, extra keys ignored, retry-then-degrade path
- [X] T032 [P] [US1] Unit tests in `tests/unit/test_selection.py`: each default exclusion glob matches, `REVIEW_EXCLUDE_MODE=replace` overrides, the bound truncates by both files and lines, relevance ordering puts story-related files first, and every skipped file is recorded with a reason
- [X] T033 [P] [US1] Unit tests in `tests/unit/test_diff_builder.py`: added file diffs against empty, modified file line map maps diff line → new-file line, binary detection via NUL byte, base-commit fallback chain

### Implementation for User Story 1

- [X] T034 [P] [US1] Define frozen dataclasses for every `data-model.md` entity in `review_agent/models.py`: `Config` view, `PullRequestContext`, `WorkItem`, `AcceptanceCriterion`, `DeclaredScope`, `ChangedFile`, `ChangeSet`, `ReviewableSet`, `SkippedFile`, `ReviewReport`, `CoverageEntry`, `ChecklistViolation`, `CodeIssue`, `FalseCoverageClaim`
- [X] T035 [US1] Implement acceptance-criterion enumeration in `review_agent/models.py`: split on markdown list markers, then `<li>` boundaries, then blank-line paragraphs; drop entries under 3 words; fall back to the description when the acceptance-criteria field is empty
- [X] T036 [US1] Implement `parse_declared_scope` in `review_agent/models.py`: locate the Covered and Not-in-this-PR sections case- and punctuation-insensitively, and set `parsed=False` when neither exists
- [X] T037 [US1] Implement the story fetches in `review_agent/tfs_client.py`: linked work items, work-item detail with `$expand=all`, and PR detail — calls 1, 2, 3 of `contracts/tfs-endpoints.md`
- [X] T038 [US1] Implement the change fetches in `review_agent/tfs_client.py`: iterations (latest, with `sourceRefCommit` and `commonRefCommit`), iteration changes, and file content at a commit — calls 4, 5, 6
- [X] T039 [US1] Implement `review_agent/diff_builder.py`: per-file unified diff via `difflib.unified_diff` with 3 context lines from the base and iteration blobs, the diff-line → new-file-line map, binary detection, and the `commonRefCommit` → `lastMergeTargetCommit` → per-file-degradation fallback chain (research.md R3)
- [X] T040 [US1] Implement `review_agent/selection.py`: the built-in exclusion list from research.md R10, `REVIEW_EXCLUDE_PATTERNS`/`REVIEW_EXCLUDE_MODE` handling, the relevance score and bound from R11, and the `excluded`/`unreviewed` records (FR-014a–f)
- [X] T041 [US1] Implement `review_agent/prompt_builder.py`: senior-reviewer system role, story, declared scope, and diff, with the three classification rules and the JSON-only instruction stated verbatim in substance per spec section 6
- [X] T042 [US1] Implement file-boundary chunking in `review_agent/prompt_builder.py`: `chars // 4` size estimate, split above `LLM_MAX_CHUNK_LINES` on file boundaries (hunk boundaries only for a single oversized file), repeating story, checklist, and scope in every chunk (FR-014, R12)
- [X] T043 [US1] Implement `call_llm(prompt) -> str` in `review_agent/llm_adapter.py` for the configured provider only, returning raw model text normalized out of the provider's response shape
- [X] T044 [US1] Implement `review_agent/parser.py`: validate against `contracts/llm-response.schema.json`, enforce the status and severity enums, ignore unknown keys, retry once with a stricter instruction, then raise `DegradedReview` (FR-022)
- [X] T045 [US1] Implement chunk merging and false-coverage derivation in `review_agent/parser.py`: union findings across chunks, dedupe on fingerprint, and derive `FalseCoverageClaim` by matching each Covered line to a `CoverageEntry` at ≥0.5 token overlap whose status is not `appears_addressed` — reporting unmatched Covered lines rather than dropping them (data-model.md)
- [X] T046 [US1] Implement the summary and inline-comment renderers in `review_agent/output_layer.py` per `contracts/pr-comment-format.md`, including the `ai-review:` footer, always-present "none" sections, and the skipped/unreviewed file tables — dry-run printing only in this story
- [X] T047 [US1] Wire the US1 orchestration in `review_agent/main.py`: config → story → changes → selection → prompt → provider → parse → render, with a phase-log line and duration per stage
- [ ] T048 [US1] Run the acceptance test from `quickstart.md` section 5 against the real demo PR in dry-run mode and confirm all five SC-001 expectations

**Checkpoint**: US1 is fully functional and independently testable — a reviewer gets a printed,
correct coverage review with the false coverage claim as the headline. MVP complete.

---

## Phase 4: User Story 2 - Review against the team's shared checklists (Priority: P2)

**Goal**: Every review is also measured against the current contents of the shared checklist
repository, with each violation citing its source file, and a new checklist file taking effect with no
configuration change.

**Independent Test**: Put two checklist files in the shared repo, run a dry review, confirm violations
cite their source files; add a third file and re-run with no config change and see its rules applied.

### Tests for User Story 2

- [X] T049 [P] [US2] Integration test in `tests/integration/test_us2_checklists.py`: several matching files are all read, combined under `## From: {filename}` labels, and folders and non-matching files are ignored (FR-012, FR-013)
- [X] T050 [P] [US2] Integration test in `tests/integration/test_us2_checklists.py`: the "business rules from the story are enforced" violation is raised and cites `code-review-checklist.md` (SC-001 expectation 3, SC-007)
- [X] T051 [P] [US2] Unit test in `tests/integration/test_us2_checklists.py`: a diff violating nothing yields an empty `checklist_violations` array with no filler findings (FR-019, spec US2 scenario 4)
- [X] T052 [P] [US2] Integration test in `tests/integration/test_us2_checklists.py`: no matching files, and an unreachable checklist repo, each degrade to a coverage-only review that says so and exits 0 (FR-029, spec US2 scenario 5)
- [X] T053 [P] [US2] Unit test in `tests/unit/test_config.py`: the repo name is derived from the `_git/{name}` segment of `CHECKLIST_REPO_URL`, and `CHECKLIST_REPO` overrides it (research.md R9)

### Implementation for User Story 2

- [X] T054 [US2] Implement checklist enumeration in `review_agent/checklist.py`: derive the repo name from `CHECKLIST_REPO_URL`, list `CHECKLIST_PATH` recursively on `CHECKLIST_BRANCH`, keep blobs matching `CHECKLIST_FILE_PATTERN`, drop folders — call 7 of `contracts/tfs-endpoints.md`
- [X] T055 [US2] Implement per-file content fetching and combination in `review_agent/checklist.py`, labelling each section with its source filename and setting `available=False` when nothing is found (FR-012)
- [X] T056 [US2] Extend `review_agent/prompt_builder.py` to include the combined checklist and to require the source filename on every reported violation
- [X] T057 [US2] Extend the renderers in `review_agent/output_layer.py` to emit the checklist-violations section with source citations and locations
- [X] T058 [US2] Wire the checklist-unavailable degraded path in `review_agent/main.py`, naming the branch and path searched in the message (FR-029)
- [ ] T059 [US2] Seed the `TestMarkDown` repo with `demo/code-review-checklist.md` on the configured branch and confirm a dry run raises the expected violation

**Checkpoint**: US1 and US2 both work independently — coverage review and checklist review.

---

## Phase 5: User Story 3 - Feedback appears automatically on the pull request (Priority: P3)

**Goal**: The review runs on its own when a feature-branch PR is opened, and its feedback lands on the
pull request as active discussions — incrementally, without duplicating itself on re-runs.

**Independent Test**: Configure the master branch policy, open a PR from `feature/add-to-cart`, and
confirm one summary discussion plus the inline flag on `CartController.AddItem` appear with no manual
step; push again and confirm nothing is duplicated.

### Tests for User Story 3

- [X] T060 [P] [US3] Unit tests in `tests/integration/test_us3_posting.py`: summary and inline thread bodies match `contracts/tfs-endpoints.md`, `status` is always `active`, and `threadContext` carries right-side lines only (FR-024, FR-024a, R4)
- [X] T061 [P] [US3] Unit tests in `tests/unit/test_dedupe.py`: fingerprints are line-independent, survive harmless re-wordings, and differ across kinds; marker parsing recovers fingerprints from existing comment text (research.md R6)
- [X] T062 [P] [US3] Integration test in `tests/integration/test_us3_posting.py`: a second run over the same findings posts no new inline comments and updates the single summary comment in place (FR-030)
- [X] T063 [P] [US3] Integration test in `tests/integration/test_us3_posting.py`: a finding with no usable file+line, and one whose line falls outside the diff map, both appear in the summary rather than being dropped (FR-025)
- [X] T064 [P] [US3] Contract test in `tests/integration/test_us3_posting.py`: exit 0 with findings by default, exit 0 when degraded, exit 1 only with `FAIL_ON_CONTRADICTION=true` plus a contradiction/false claim/high issue, exit 2 on each setup failure (SC-008, SC-009)
- [X] T065 [P] [US3] Unit test in `tests/contract/test_preview_makes_no_writes.py`: no code path resolves, closes, deletes a thread, or votes on a pull request (FR-024a, spec Out of Scope)

### Implementation for User Story 3

- [X] T066 [US3] Implement thread writes in `review_agent/tfs_client.py`: create thread with `status: active`, add a comment to an existing thread, and patch an existing comment — calls 9 and 10 of `contracts/tfs-endpoints.md`
- [X] T067 [US3] Implement existing-thread reading in `review_agent/tfs_client.py`, skipping deleted threads — call 8
- [X] T068 [US3] Implement `review_agent/dedupe.py`: the `sha256(kind|path|normalized_text)[:12]` fingerprint, `ai-review:` marker parsing, and `PriorFeedback` assembly identifying the agent's own threads by marker rather than author id — the same visible marker that makes automated comments unmistakable to a reader (FR-031, research.md R5, R6)
- [X] T069 [US3] Implement live posting in `review_agent/output_layer.py`: create-or-update the single summary discussion, post inline comments only for unposted fingerprints, and record suppressed findings — never resolving or deleting anything (FR-030)
- [ ] T070 [US3] Resolve the R5 `⚠ verify` item: confirm whether the build service identity may `PATCH` its own comment on the target server; if not, switch to appending in the same thread and record the decision in `specs/001-ai-code-review-agent/research.md`
- [X] T071 [US3] Implement the blocking exit path in `review_agent/main.py`: exit 1 when `FAIL_ON_CONTRADICTION=true` and the report holds a contradiction, false coverage claim, or high-severity issue (FR-028)
- [X] T072 [US3] Implement the degraded-review summary rendering in `review_agent/output_layer.py` per the "could not complete" template in `contracts/pr-comment-format.md`, stating that no findings should be inferred from its silence
- [X] T073 [US3] Create `azure-pipelines.yml` at the repo root: `trigger: none`, the self-hosted pool, requirements install, `python review_agent/main.py`, and PR context plus `SYSTEM_ACCESSTOKEN` passed via `env:` with the LLM key from a variable group and never in the YAML
- [X] T074 [US3] Add the environment probe step to `azure-pipelines.yml` printing `python --version` and `pip --version` before install, so the unverified build-agent Python resolves loudly (plan.md residual risks)
- [X] T075 [US3] Document the one-time manual settings in `review_agent/README.md`: master branch policy → Build Validation, "Allow scripts to access the OAuth token", and **Contribute to pull requests** for the build service identity
- [ ] T076 [US3] Wire the master branch policy, open the `feature/add-to-cart` PR into `master`, and confirm the same feedback posts automatically; then push a second commit and confirm nothing is duplicated

**Checkpoint**: US1–US3 all work independently — the review now runs and posts on its own.

---

## Phase 6: User Story 4 - Retarget and re-host without code changes (Priority: P4)

**Goal**: A platform engineer can point the agent at a different pull request, project, or repository
and switch AI provider entirely through configuration.

**Independent Test**: Run two reviews differing only in configuration — a different target PR and a
different provider — and confirm both work with no change to any reviewing logic.

### Tests for User Story 4

- [X] T077 [P] [US4] Unit tests in `tests/unit/test_llm_adapter.py`: request payload and text extraction for each of `claude` (`content[].text`), `openai`/`azure` (`choices[].message.content`), and `ollama`, plus `LLM_BASE_URL` honouring
- [X] T078 [P] [US4] Unit tests in `tests/unit/test_auth_modes.py`: pipeline token preferred over PAT, empty-string pipeline token treated as absent, PAT-only manual run succeeds, neither present fails as a setup error (FR-004, R2)
- [X] T079 [P] [US4] Integration test in `tests/unit/test_auth_modes.py`: changing only `TFS_PR_ID`, `TFS_PROJECT`, and `TFS_REPO` retargets the review with no code change (FR-035, SC-005)
- [X] T080 [P] [US4] Unit test in `tests/unit/test_llm_adapter.py`: `LLM_PROVIDER=ollama` does not require `LLM_API_KEY` (FR-005)

### Implementation for User Story 4

- [X] T081 [US4] Complete all four provider branches in `review_agent/llm_adapter.py` behind the single `call_llm` signature, with `LLM_BASE_URL` required for `azure` and `ollama`, keeping every wire-format detail inside this module (FR-033)
- [X] T082 [US4] Verify and complete pipeline-variable sourcing in `review_agent/config.py` for `System.TeamFoundationCollectionUri`, `System.TeamProject`, `Build.Repository.Name`, and `System.PullRequest.PullRequestId` (FR-002)
- [X] T083 [US4] Complete `review_agent/README.md` with the full variable table from `contracts/cli-contract.md`, the exit-code table, and the quickstart troubleshooting table
- [ ] T084 [US4] Run the same pull request through two different providers in dry-run mode and confirm identical report structure with zero changes outside `llm_adapter.py` (SC-005)

**Checkpoint**: All four user stories independently functional.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [ ] T085 [P] Verify SC-002 from the phase-log timings on a real run: end to end within 5 minutes for a change set at the review bound, recorded in `specs/001-ai-code-review-agent/quickstart.md`
- [X] T086 [P] Verify SC-010 in `tests/contract/test_excluded_files_never_sent.py`: a PR changing a secret-bearing file alongside code sends 0 bytes of that file to the provider and names it as skipped
- [X] T087 [P] Verify SC-011 in `tests/integration/test_bound_transparency.py`: a change set exceeding the bound names every unreviewed file and presents none as reviewed-and-clean
- [X] T088 [P] Verify SC-007 in `tests/integration/test_bound_transparency.py`: every reported violation traces to a real item in the combined checklist and cites the correct source file
- [ ] T089 Resolve the remaining research `⚠ verify` items and record the outcomes in `specs/001-ai-code-review-agent/research.md`: `commonRefCommit` availability (R3) and the working `TFS_API_VERSION` for this server
- [X] T090 [P] Add the `os.environ` containment check to `tests/contract/test_import_boundaries.py` as a permanent gate, and grep `review_agent/` for literal URLs, tokens, or model names (FR-001)
- [ ] T091 Run the full `quickstart.md` validation top to bottom on a clean checkout, including the failure-mode table
- [X] T092 Ratify the seven gates as project principles in `.specify/memory/constitution.md` (v1.0.0, 2026-08-26); propagate the result into plan.md's Constitution Check

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: T001 blocks every commit. T003–T009 are parallel after T001.
- **Foundational (Phase 2)**: Depends on Setup. **Blocks all user stories.**
- **User Stories (Phase 3–6)**: All depend on Foundational.
- **Polish (Phase 7)**: Depends on the stories whose criteria it verifies.

### User Story Dependencies

- **US1 (P1)**: Foundational only. No dependency on any other story. **This is the MVP.**
- **US2 (P2)**: Foundational only. Touches `prompt_builder.py` and `output_layer.py` that US1 creates, so building it after US1 avoids merge friction — but it is independently testable, and a checklist-only review works with zero linked work items (FR-008).
- **US3 (P3)**: Foundational only for its code, but its *acceptance* test is most meaningful once US1 produces real findings to post. Its renderers extend US1's.
- **US4 (P4)**: Foundational only. `llm_adapter.py` is created in US1 with one provider and completed here — the only cross-story file dependency in the plan.

### Within Each User Story

- Tests before implementation.
- `models.py` (T034) before anything that constructs an entity.
- `diff_builder` and `selection` before `prompt_builder`.
- `parser` before `output_layer` rendering.
- `main.py` wiring last in each story.

### Parallel Opportunities

- **Setup**: T003–T009 all parallel — the demo scaffolding (T006–T009) shares no file with the agent skeleton and can be done by a different person entirely.
- **Foundational**: T017 and T019–T023 parallel; T010–T016 are sequential within `config.py` and `tfs_client.py`.
- **US1**: all ten test tasks (T024–T033) parallel; T034 parallel with the test writing.
- **US2**: all five test tasks (T049–T053) parallel.
- **US3**: all six test tasks (T060–T065) parallel.
- **US4**: all four test tasks (T077–T080) parallel; the whole story is parallel with US2 and US3 if staffed.
- **Polish**: T085–T088 and T090 parallel.

---

## Parallel Example: User Story 1

```bash
# All US1 tests together (they touch different files):
Task: "Record TFS response fixtures in tests/fixtures/pr_demo/"
Task: "Integration test for coverage classification in tests/integration/test_coverage_classification.py"
Task: "Integration test for false coverage claim in tests/integration/test_false_coverage_claim.py"
Task: "Integration test for scoped-out items in tests/integration/test_scoped_out_unflagged.py"
Task: "Unit test for not_addressed routing in tests/unit/test_routing_not_addressed.py"
Task: "Contract test for preview-mode writes in tests/contract/test_preview_makes_no_writes.py"
Task: "Unit tests for parser in tests/unit/test_parser.py"
Task: "Unit tests for selection in tests/unit/test_selection.py"
Task: "Unit tests for diff_builder in tests/unit/test_diff_builder.py"
```

```bash
# Setup phase: agent skeleton and demo target in parallel:
Task: "Create agent package skeleton in review_agent/"
Task: "Create CartController.cs in src/eShop.Api/Controllers/"
Task: "Create demo/user-story-101.md"
Task: "Create demo/pr-description.md"
Task: "Create demo/code-review-checklist.md"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1 Setup — **T001 first, before any commit**
2. Phase 2 Foundational (blocks everything)
3. Phase 3 US1
4. **STOP and VALIDATE**: `python review_agent/main.py --dry-run` against the demo PR; confirm the five SC-001 expectations
5. This is demonstrable on its own — a reviewer reads the printed report and acts on it, with no posting and no pipeline

### Incremental Delivery

1. Setup + Foundational → foundation ready
2. US1 → the printed coverage review with false-coverage detection → **MVP demo**
3. US2 → checklist violations with source citations → demo
4. US3 → automatic triggering and posting on the PR → demo
5. US4 → provider and target portability → hand-off ready

Each increment adds value without breaking the previous one. US1 alone satisfies the feature's headline
claim; everything after it broadens reach rather than completing the idea.

### Parallel Team Strategy

- One person does T001–T002 immediately (nobody commits until T001 lands).
- Setup splits cleanly: one person on the agent skeleton, another on the demo .NET target and demo docs.
- Foundational is best done by one person — T010–T016 concentrate in two files.
- After Foundational: US1 for the strongest developer (it carries `diff_builder`, the plan's largest
  risk), US4 in parallel for a second (isolated to `llm_adapter.py` and `config.py`), then US2 and US3.

---

## Notes

- **T001 is a security task, not housekeeping.** `.env` is tracked and `origin` is the eShop repo on
  TFS. Nothing has leaked yet; the next commit including that file would publish a live PAT and LLM key.
- Total: **92 tasks** — 9 Setup, 14 Foundational, 25 US1, 11 US2, 17 US3, 8 US4, 8 Polish.
- 44 tasks are marked `[P]`.
- `[Story]` labels appear only in Phases 3–6, per the format rules.
- Three tasks resolve the research `⚠ verify` items against the real server rather than guessing:
  T070 (comment patching), T074 (build-agent Python), T089 (`commonRefCommit`, api-version).
- Commit after each task or logical group — after T001.

---

## Blocked on the live environment

The 9 unchecked tasks are not skipped work - each needs something this session cannot
reach: the TFS collection at `devops.najm.sa`, a real pull request, real credentials, or a
decision that is the user's to make. Everything they verify is already covered against
recorded fixtures, so they are confirmations rather than unknowns.

| Task | Needs | Already verified against fixtures |
|---|---|---|
| T002 | Access to TFS + the AI provider console to rotate credentials | n/a - a manual security action |
| T048 | The live demo PR | All five SC-001 expectations pass on the recorded fixtures (`tests/integration/test_us1_review.py`) |
| T059 | Write access to the `TestMarkDown` repo | Combining, source citation, and the degraded path (`test_us2_checklists.py`) |
| T070 | The live server, to see whether the build identity may PATCH its own comment | Both branches are implemented and tested, including the append fallback (`test_us3_posting.py`) |
| T076 | Branch-policy admin rights + an open PR | Posting, thread payloads, and incremental re-review (`test_us3_posting.py`) |
| T084 | Two live provider credentials | Every provider's request shape and extraction path (`test_llm_adapter.py`) |
| T085 | A real timed run | Phase-log timings are emitted; the 5-minute budget cannot be measured offline |
| T089 | The live server, for `commonRefCommit` and the accepted api-version | Fallback chains implemented and tested (`test_diff_builder.py`, `test_auth_modes.py`) |
| T091 | A live dry run for step 3 | Steps 1, 2 and 6 of the quickstart verified: install, config validation, 322 tests |

**Run order once the environment is available**: T002 -> T048 -> T059 -> T085 -> T089 ->
T070 -> T076 -> T084.
