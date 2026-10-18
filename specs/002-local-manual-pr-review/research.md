# Phase 0 Research: Local Manual Pull Request Review Run

**Feature**: `002-local-manual-pr-review` | **Date**: 2026-09-15 | **Plan**: [plan.md](./plan.md)

Every decision below was taken against the **actual state of the three repositories and the five real
checklist documents**, inspected on 2026-09-15 — not against assumptions. Facts that changed a decision
are marked **[verified]** with the evidence.

---

## Preconditions verified before deciding anything

| Fact | Evidence | Consequence |
|---|---|---|
| This repo (`CodeReviewSample`) is the **sample code** repo | `git remote -v` → `.../_git/CodeReviewSample` | The code under review lives here. The demo pull request for the end-to-end test is opened here. |
| The agent source lives in a **separate** repo, already cloned at `../CodeReviewAgent` | `git remote -v` there → `.../_git/CodeReviewAgent`; 15 modules, 3,291 lines | 001's implementation exists and is the baseline. 002 is a delta on it, not a greenfield build. |
| The checklist repo is cloned at `../CodeReviewChecklist` and holds **3 .xlsx + 2 .docx** | `ls` → API Integration v3.0, API Security v3.0, NFR v2.0 (xlsx); Najm_Application_Security_Guidelines v1.0, Najm_Coding_Standards v1.1 (docx) | SC-006 ("at least two documents of each format") is satisfiable by the real content. Extraction must handle what these files actually look like — see R4, R5. |
| `tests/` (17 files, 322 tests) sits in **CodeReviewSample**, but imports `review_agent`, which is **not here** | `python -m pytest --collect-only` → *17 errors during collection* | The test suite is currently **entirely red** — not one test runs. Tests must move to the agent repo. Workstream A. |
| `.env` is **not** tracked, and `.gitignore` covers it | `git ls-files --error-unmatch .env` → not tracked; `.gitignore` lists `.env`, `*.pem`, `*.key`, `*.pfx` | The `CLAUDE.md` warning about a tracked `.env` is **stale**. No remediation task is needed; the constitution's Principle II is not currently breached by the repo layout. |
| `.env` (both repos) is already provisioned with 9 variables | key names only: `TFS_BASE_URL`, `TFS_PROJECT`, `TFS_PAT`, `TFS_REPO_CODE_SAMPLE`, `TFS_REPO_CODE_REVIEW_CodeReviewAgent`, `TFS_REPO_CODE_REVIEW_CodeReviewChecklistT`, `TFS_PAT_FOR_PR`, `LLM_MODEL`, `LLM_API_KEY` | `config.py` knows **none** of these names. Reconciling them is a required task, not a nicety — see R10. |
| Python 3.13.14; `requests`, `python-dotenv`, `truststore` installed; `openpyxl` **not** installed | `python -c "import openpyxl"` → ModuleNotFoundError | One new runtime dependency is needed for spreadsheets. See R14. |
| The console here is **cp1252**, not UTF-8 | A probe script died with `UnicodeEncodeError: 'charmap' codec can't encode 'ℹ'` | The real checklist documents contain `ℹ`, `✖`, `—`, `’`. Any console printing of checklist text must survive cp1252 — the existing `tests/contract/test_output_encoding.py` is guarding exactly this and must keep passing. |

---

## R1 — Which repository each part of this feature lands in

**Decision**: All code and all tests land in **`CodeReviewAgent`**. `CodeReviewSample` receives only the
demo change and pull request used for the end-to-end test. The orphaned `tests/` tree moves from
`CodeReviewSample` to `CodeReviewAgent` as the first task.

**Rationale**: The constitution governs `review_agent/` and `tests/` as one unit — Principle IV is
*enforced by* `tests/contract/test_import_boundaries.py`. With the source in one repo and its enforcing
tests in another, none of the seven principles is actually enforced: the suite cannot even be collected.
FR-012 makes this worse, not better — the agent repo is about to become the thing that is fetched and
executed, so its tests must travel with it.

**Alternatives considered**:
- *Leave tests in the sample repo and add the agent as a submodule* — rejected: submodules pin a commit,
  which directly contradicts FR-012's "latest committed version", and the red suite stays red for anyone
  who clones only one repo.
- *Duplicate the tests* — rejected: two copies drift, and the copy that drifts is always the one enforcing
  the constitution.

---

## R2 — How "the fetched copy executes" works (FR-012, FR-012a, FR-012b)

**Decision**: A two-stage entry point inside the agent package.

1. `python -m review_agent --pr <id> [--dry-run]` starts **`bootstrap.py`**.
2. Bootstrap loads configuration, fetches the agent repository at the configured ref into a per-run
   temporary directory, and **re-executes** `python -m review_agent.run` inside that directory with
   `REVIEW_AGENT_BOOTSTRAPPED=<commit-sha>` set in the child environment.
3. The child sees the marker, skips fetching, and performs the review.
4. `--local` skips the fetch entirely and runs the working copy.
5. Either way, the run report opens with the provenance line the child derives from the marker:
   `review logic: fetched a1b2c3d (CodeReviewAgent@master)` or `review logic: LOCAL WORKING COPY`.

**Rationale**: Re-exec is the only mechanism that makes the *fetched* code genuinely the code that runs;
importing from a temp path would leave the local package's already-imported modules in `sys.modules` and
silently review with a mixture of both. The marker is the recursion guard and the provenance source in one
value, so FR-012b cannot drift from FR-012 — the report prints what actually ran.

**Alternatives considered**:
- *`sys.path` injection instead of re-exec* — rejected: import order decides which copy wins, which is the
  exact ambiguity the clarification was asked to remove.
- *`pip install` from the repo URL per run* — rejected: slower, mutates the developer's environment, and
  leaves the token in pip's log output.
- *A launcher script in the sample repo* — rejected: it would have to be copied into every reviewed
  repository, and each copy becomes a stale second implementation of FR-012.

---

## R3 — How the two repositories are obtained, without leaking a token

**Decision**: `git clone --depth 1 --branch <ref>` into a temp directory, with the PAT supplied through a
**`GIT_ASKPASS` helper**, `credential.helper=` forced empty, and the origin URL rewritten to a
credential-free form immediately after the clone. One module, `source_fetch.py`, owns every `git`
invocation.

**Rationale**: Three ways of passing a PAT to git leak it, and all three are common:
`https://user:pat@host/...` writes the token into `.git/config` of the clone; `-c http.extraHeader=...`
puts it in the process argument list, visible to any other process on the machine; and
`credential.helper=store` writes it to `~/.git-credentials`. `GIT_ASKPASS` passes it over a pipe to a
child that git already trusts, leaving no copy on disk or in `argv`. Both PATs are registered with the
existing redaction registry (Principle II) before the first `git` call, so even an unexpected git error
message is scrubbed on the way out.

**TLS**: git on Windows uses the schannel backend and therefore the machine trust store, which is the same
place `truststore` points `requests` — so the private CA on `devops.najm.sa` validates for both, and
Principle III needs no exception. `http.sslVerify` is never set; a test asserts it never appears.

**Alternatives considered**:
- *Fetch checklists over the TFS REST items API instead of cloning* (what 001 does for markdown) —
  rejected as the primary path: `get_file_on_branch` returns decoded text, and a `.xlsx` is a binary zip
  that decoding corrupts. A bytes-returning variant is a fine fallback, but the agent repo must be cloned
  anyway for R2, so one mechanism for both is less code and one less boundary to test.
- *`git archive`* — rejected: not enabled on this on-prem TFS for HTTP remotes.

---

## R4 — Extracting rules from the real spreadsheets (FR-016, FR-017a)

**[verified]** All three `.xlsx` files share a house style, and it does **not** match the naive
"row 1 is the header" assumption:

| Document | Sheets | Header row | Identifier column | Rule-text column |
|---|---|---|---|---|
| API Integration Checklist v3.0 | `Document Control`, `API Integration Checklist`, `lookups` | **row 2** (row 1 is a merged banner) | `#` (populated 1..n) | `Title` (+ `Description`) |
| API Security Checklist_v3.0 | `Document Control`, `API Security Checklist`, `lookups` | **row 2** | `#` (populated) | `Title` (+ `Description`) |
| Non Functional Requirements v2.0 | `Document Control`, `Arch Quality Attributes` | **row 2** | `#` — **present but empty in every data row** | `Checklist Item` (+ `Category`, `Sub-Category`) |

**Decision**: A sheet qualifies as a rule sheet when, within its first 5 rows, a row is found that carries
a recognised **rule-text** header. Column roles are resolved by case-insensitive synonym:

- rule text ← `Checklist Item` | `Title` | `Requirement` | `Rule` | `Check`
- detail ← `Description` | `Sub-Category` | `Details` | `Guidance`
- identifier ← `#` | `Item` | `ID` | `Ref` | `No`
- ignored ← `Comply?` | `Comments` | `Status` | `Owner` (answer columns, not rules)

Sheets with no qualifying header row — `Document Control` and `lookups` in every file — contribute nothing
and are reported as such. A data row contributes a rule when its rule-text cell is non-empty.

**Identifier handling**: where the identifier cell is empty (the entire NFR document), the identifier is
**derived** as `<document-stem>:<sheet>:<row>` — for example `NFR-v2.0:Arch Quality Attributes:17`.

**This contradicts FR-017a as currently written**, which makes an identifier column mandatory and declares a
spreadsheet without one unreadable. Applying the spec literally would reject `Non Functional Requirements
Checklist v2.0.xlsx` — a real, well-formed, in-use checklist — and SC-006 would fail on the actual corpus.
The clarification's intent was "don't guess at layouts", and that intent is fully served by requiring the
**rule-text** column; the identifier is the one field that can be derived without guessing, exactly as
FR-017b already allows for Word documents. **Spec amended** — see plan.md § Spec Adjustments.

**Alternatives considered**:
- *Require the quality leads to add a `#` value to the NFR file* — rejected: FR-015 promises that adding or
  editing a document needs no coordination, and the first thing the agent would do is demand a document edit.
- *Positional columns (A=id, B=rule)* — rejected: the three documents already disagree on position
  (`Title` in B, `Checklist Item` in D).

---

## R5 — Extracting rules from the real Word documents (FR-016, FR-017b)

**[verified]** Both `.docx` files are generated from one template and are far more structured than
free-form prose: 1,021 and 1,072 paragraphs, **125 and 136 tables**, `Heading1`/`Heading2` outline, and —
decisively — every rule is introduced by a single-row table whose first cell carries a **stable rule ID**:

```
table 1:  ['SEC-WEB-01  Prevention of Cross-Site Scripting (XSS)', 'HIGH']
table 1:  ['DEV-SEC-01  Adopt a Secure Coding Standard',           'HIGH']
```

The documents say so themselves: *"Rule ID & Title — a stable identifier (e.g., SEC-WEB-01) you can cite in
code reviews, pull requests…"*. Supporting tables (`ℹ PROVENANCE`, `✖ EXPLOIT SCENARIO`) carry commentary,
and `Contents` / `Appendix A — Rule-to-Gap Traceability` carry no rules at all.

**Decision**: Rule-ID-marker segmentation, with the tolerant scan as the fallback:

1. Walk the document body in order, tracking the current `Heading1`/`Heading2` as section context.
2. A rule **starts** at any table cell matching `^([A-Z]{2,4}(?:-[A-Z]{2,6}){1,2}-\d{1,3})\s+(.{3,})$` —
   captured as identifier and title; an adjacent `HIGH`/`MEDIUM`/`LOW` cell is captured as severity.
3. The rule **body** is the following paragraphs and table cells until the next rule marker or the next
   `Heading1`, minus the decorated commentary blocks (see R6).
4. `Contents`, `Appendix *`, and `How to Use This Guide` sections are skipped by heading name.
5. **Fallback** — if a document yields zero rules this way, every `Heading2`, `ListParagraph`, and table row
   is treated as a candidate rule with a derived `<document-stem>:<n>` identifier, per FR-017b. A document
   that still yields nothing is reported as contributing nothing (FR-018), never silently dropped.

**Rationale**: The real IDs (`SEC-WEB-01`) are exactly what FR-027's citation needs — a developer can paste
one into a search and find the rule. Marker segmentation yields ~40 clean rules per document; the purely
tolerant reading of the same file yields 130+ fragments, most of them commentary, and the model would then
"violate" a table caption. The fallback keeps FR-017b's promise for any future document that lacks IDs.

**Alternatives considered**:
- *Tolerant-only extraction* — rejected on the evidence above: it discards the stable identifiers the
  documents deliberately provide.
- *Heading-based segmentation* — rejected: rules live in tables, not headings; `Heading2` marks whole
  sections such as `4.1 Web Security Baseline`.

---

## R6 — Keeping the combined checklist inside a sane prompt budget

**[verified]** The two Word documents alone hold **52,290 and 54,852 characters** of text. Combined with
~170 spreadsheet rows, a naive "concatenate every document" checklist is **~115,000 characters ≈ 30,000
tokens** — and `prompt_builder.build_prompts` repeats the entire checklist **in every chunk** of an
oversized diff. A 3-chunk review would spend ~90,000 tokens on checklist text alone.

**Decision**: The combined checklist is a list of **compact rule items**, not document text. Each item
carries: identifier, title, rule text trimmed to 400 characters, severity where known, section, and source
document. Commentary blocks (`PROVENANCE`, `EXPLOIT SCENARIO`, `REMEDIATION` narrative) are dropped at
extraction. The rendered checklist block is capped by `CHECKLIST_MAX_CHARS` (default 40,000); if the cap is
reached, lowest-priority items are dropped **and named in the report** — the same transparency rule the
review bound already follows (FR-021e, Principle VII).

**Rationale**: The model needs the *rule*, not the essay explaining the rule. Trimming at extraction rather
than at prompt time keeps one checklist representation everywhere, so what the dry run prints is what the
model saw.

> **Corrected during implementation (2026-09-15).** This decision predicted ~22,000 characters and set the
> cap at 40,000 on that basis. The readers were then run against the real corpus: **165 rules rendering to
> 48,485 characters** — compaction works (115,000 → 48,485, a 58% reduction) but the prediction was low by
> more than half, and a 40,000 cap would have silently dropped roughly a third of the corpus on the first
> real run. The default is now **60,000**. The estimate was wrong because it assumed the spreadsheets carried
> short rule text; the NFR document alone contributes 66 rules, most with substantial `Description` detail.

**Alternatives considered**:
- *Send full documents and rely on the model's context window* — rejected: 3× the cost per run for text that
  is mostly prose, and it silently couples the agent to a large-context model, breaking the spirit of FR-031.
- *Retrieve only checklist items relevant to the diff* — rejected for this iteration: it adds a retrieval
  step whose failure mode is a silently unenforced rule, which Principle VII forbids. Reconsider only if the
  corpus outgrows the cap.

---

## R7 — Making the backend boundary a real interface (FR-030 – FR-033)

**[verified]** `llm_adapter.py` already routes every call through one function (`call_llm`) that returns raw
text, and `tests/contract/test_import_boundaries.py` already forbids provider knowledge elsewhere. So
FR-030 holds today. What does **not** hold is FR-032: adding a provider means editing the `if provider ==`
chain inside `_request_for`, plus `_DEFAULT_BASE_URLS`, plus a new `_extract_*` function, plus the error
message listing valid providers — four edits to shared code, in a function every other provider also uses.

**Decision**: Refactor to a **backend registry**. Each backend is a small object exposing
`build_request(prompt, config) -> (url, headers, body)` and `extract_text(payload) -> str`, registered
under its configuration name in one dict. `call_llm` keeps its signature, its retry/backoff policy, and its
`DegradedReview` semantics — all provider-neutral — and simply looks the backend up. Adding a provider then
means: one new module-level object, one registry line, one `.env.example` line. No shared function is
edited, which is what FR-032 actually asks for and what the audit in FR-033 will measure.

**Rationale**: The current design satisfies "one module knows the wire format"; it does not satisfy "adding
one does not change the review logic", because `_request_for` *is* shared logic. The registry makes the
interface explicit enough that the FR-033 audit has something to assert rather than to judge.

**Alternatives considered**:
- *Declare the existing branch chain sufficient* — rejected: FR-032 names result parsing and prompt
  construction as untouched, but says nothing about the dispatcher itself being rewritten per provider;
  a reviewer would rightly read the current code as failing the requirement.
- *Entry-point plugin discovery* — rejected: real extensibility this feature does not need, and it would let
  an unvetted third-party package inject itself into a credential-bearing path.

---

## R8 — What the "Copilot" backend actually is

**Decision**: Implement `copilot` as an **OpenAI-compatible chat-completions backend** with a
**required** `LLM_BASE_URL` and a configurable auth header style (`Authorization: Bearer` by default,
`api-key` selectable), defaulting the endpoint path to `/chat/completions`. Ship it against the GitHub
Models OpenAI-compatible surface, and treat the exact endpoint and credential as an
**environment fact to confirm with the platform owner before the end-to-end run** (task in workstream F).

**Rationale, stated plainly**: "Copilot" names several different things with different APIs — GitHub Copilot
Chat (no supported general-purpose completions API for this use), GitHub Models (OpenAI-compatible,
token-authenticated), and Copilot-branded Azure OpenAI deployments (the `azure` backend already implemented).
Every one of them speaks the OpenAI chat-completions shape, so the *backend* is the same code; only the base
URL, the auth header, and the model name differ, and all three are already configuration. Choosing the shape
now and confirming the endpoint later is therefore safe: if the organisation's Copilot entitlement turns out
to be an Azure OpenAI deployment, `LLM_PROVIDER=azure` already works and no code changes at all.

This is the one decision in this plan that rests on an environment fact not yet verifiable from the repo.
It is isolated to one registry entry precisely so that being wrong costs one object, not a redesign.

**Alternatives considered**:
- *Reverse-engineer the Copilot Chat protocol used by the IDE plugins* — rejected: undocumented, unstable,
  and outside the terms the organisation's licence is granted under.
- *Defer the Copilot backend to a later iteration* — rejected: FR-031 names both backends, and the registry
  work (R7) is only demonstrably complete when a second, differently-shaped backend exists.

---

## R9 — Which token does what (FR-006, FR-008)

**Decision**: Two credentials, two clients, no sharing:

| Credential | Used for | Failure is |
|---|---|---|
| `TFS_PAT` | cloning both repositories (R3), reading the pull request, its iterations, changes, file blobs, linked work items | setup failure — the review cannot start |
| `TFS_PAT_FOR_PR` | reading the pull request's existing threads, creating threads, adding and updating comments | setup failure **only in a posting run**; absent is fine in a dry run (FR-008) |

`TfsClient` gains an explicit `purpose` at construction (`read` or `post`), selecting which token builds its
auth header. The overlap in the spec's wording ("reading the pull request" appears under both tokens) is
resolved in favour of the read token for everything the review needs to *see*, and the post token for
everything under `/pullRequests/{id}/threads` — so a posting-permission problem surfaces on the posting
path, where FR-002's "dry run posts nothing" guarantee can still be proven.

**Rationale**: Two clients with one token each is the only arrangement where "the dry run never uses the
posting credential" is testable by construction rather than by inspection — the dry-run path simply never
builds the posting client. That turns FR-002 into a contract test rather than a promise.

**Alternatives considered**:
- *One client that picks a token per call* — rejected: every new call site becomes a chance to pick the wrong
  one, and the dry-run guarantee becomes unprovable.

---

## R10 — Reconciling the provisioned `.env` with `config.py`

**[verified]** The provisioned `.env` and the code disagree on every repository variable, and the code has no
concept of a second token:

| Provisioned name | `config.py` today | Resolution |
|---|---|---|
| `TFS_REPO_CODE_SAMPLE` | `TFS_REPO` | accept both; provisioned name wins |
| `TFS_REPO_CODE_REVIEW_CodeReviewAgent` | — (no concept) | new: the agent repo to fetch (R2) |
| `TFS_REPO_CODE_REVIEW_CodeReviewChecklistT` | `CHECKLIST_REPO` / `CHECKLIST_REPO_URL` | accept both; provisioned name wins |
| `TFS_PAT_FOR_PR` | — (no concept) | new: the posting credential (R9) |
| `LLM_MODEL=claude-sonnet-4-6` | `LLM_MODEL` | unchanged |
| — | `LLM_PROVIDER` | still required; defaults to `claude` |

**Decision**: `config.py` reads the **provisioned names as canonical** and keeps the 001 names as accepted
aliases, with the provisioned name taking precedence when both are set. Missing-value errors name the
provisioned spelling. `.env.example` is rewritten to the provisioned names, trailing `T` and mixed case
included.

**Rationale**: The developer's `.env` already exists and already works for the parts of the flow that run
today; renaming variables would break the only known-good configuration on the machine to satisfy a style
preference. Aliases cost one `or` per variable and keep 001's own `.env` files working. The odd spellings
(`...ChecklistT`, mixed case) are preserved deliberately — inventing a "corrected" name would mean the
documented variable and the real one differ, which is worse than an ugly name.

---

## R11 — An empty checklist stops the run (FR-019) — a deliberate reversal of 001

**Decision**: When no checklist document yields a single rule, the run **stops as a setup failure**. 001's
`fetch_checklist` returns a degraded review in the same situation and continues.

**Rationale**: The two behaviours are right for two different callers. In 001 the agent runs automatically on
every pull request, and an unreachable checklist repo must not gate anyone's merge — degrading is correct. In
002 a developer has deliberately typed a command whose entire purpose is a checklist review; completing with
"no checklist was available" would post a clean-looking review built on nothing, which is precisely the
failure Principle VII exists to prevent. A human is present to read the error and fix it.

**Consequence**: `checklist.py`'s degraded path is kept for the coverage-only case (no rules *and* no linked
story is still a stop), but the manual entry point converts "zero rules" into a `SetupFailure`. The
distinction is recorded in the routing table in `data-model.md`.

---

## R12 — Citations become (item id, document), not a filename

**Decision**: `ChecklistViolation` gains `item_id` and `document`, and `source` is retired. The response
schema requires both; the parser rejects a violation missing either, and the renderer prints
`SEC-WEB-01 — Prevention of Cross-Site Scripting (XSS)` with the document name beneath.

**Rationale**: FR-027 requires both the item and the document. 001's single `source` field held a markdown
filename, which was sufficient when a checklist file *was* the unit; with 40 identified rules per document it
is no longer traceable. Requiring the model to echo an identifier it was given also gives the parser a cheap
hallucination check: a violation citing an identifier absent from the combined checklist is dropped and
counted in the report, rather than posted as a rule nobody wrote.

---

## R13 — Test fixtures for the document readers

**Decision**: The test corpus is **generated, not copied**. A fixture builder writes small `.xlsx` and
`.docx` files with stdlib `zipfile` — one per real-world shape found in R4 and R5: banner-then-header sheet,
populated `#`, empty `#`, metadata-only sheet, rule-ID tables, a no-rule document, and a deliberately corrupt
file. Two checked-in **redacted excerpts** (≤ 20 rows / 3 rules) stand in for the real documents in the
integration test.

**Rationale**: The real documents are internal Najm standards, 38–63 KB each, and would land in a repository
whose whole purpose is to be cloned onto developer machines. Generated fixtures also let a test assert the
empty-`#` case that the NFR document happens to exercise, without depending on that document keeping its
shape. Copying all five would couple the suite to documents the quality leads edit on their own schedule.

---

## R14 — Reading the two formats: one dependency, not two

**Decision**: `openpyxl` (new runtime dependency, pure Python, read-only mode) for spreadsheets. For Word,
**stdlib `zipfile` + `xml.etree`** — no `python-docx`.

**Rationale**: `openpyxl` earns its place: shared-string tables, merged banner cells, and cell types are
genuinely fiddly, and it is pure Python with no compiled extensions. `python-docx` does not: it pulls
`lxml`, a compiled dependency, to do what 30 lines of `ElementTree` already did in this session's probe —
the paragraph/table walk in R5 was written and run against the real documents with the standard library
alone. One less compiled dependency also keeps 001's "installs reliably on a self-hosted agent" property
intact for when this agent goes back into a pipeline.

**Alternatives considered**:
- *stdlib for both* — rejected: re-implementing shared strings and cell-type coercion is a real source of
  silent wrong values in spreadsheets.
- *`pandas` / `openpyxl` + `python-docx`* — rejected: `pandas` for 170 rows is a 40 MB dependency for a
  header-row scan it cannot even do correctly (it assumes row 0).

---

## Resolved unknowns

| Unknown at plan start | Resolved by | Status |
|---|---|---|
| Does the agent source exist to be audited? | Preconditions | Yes — 15 modules, 3,291 lines, in `CodeReviewAgent` |
| Is the test suite a usable safety net for the refactor? | Preconditions | **No** — 17 collection errors; fixing it is task A1 |
| Do the real spreadsheets have id + rule columns? | R4 | Rule columns yes; identifiers **absent in one document** → spec amended |
| Do the real Word documents have citable rule identifiers? | R5 | Yes — `SEC-WEB-01`-style, by design |
| Will the combined checklist fit a prompt? | R6 | Only after compaction: 115 K → ~22 K chars |
| Is the LLM boundary already agnostic? | R7 | Partly — one call path yes, pluggable no |
| What is the Copilot endpoint? | R8 | Shape decided (OpenAI-compatible); **exact endpoint to confirm with the platform owner** |
| Which token does what? | R9 | Read vs post, two clients |
| Do the provisioned variable names match the code? | R10 | No — aliasing required |
