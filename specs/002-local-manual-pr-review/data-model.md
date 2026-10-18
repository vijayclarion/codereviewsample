# Phase 1 Data Model: Local Manual Pull Request Review Run

**Feature**: `002-local-manual-pr-review` | **Date**: 2026-09-15 | **Plan**: [plan.md](./plan.md)

Entities below are the spec's Key Entities made concrete. **New** marks an entity this feature introduces;
**Changed** marks one that already exists in `review_agent/models.py` and is modified; unmarked entities are
carried over unchanged and are listed only where a new rule touches them.

All entities are frozen dataclasses. Nothing is persisted — the agent is stateless between runs.

---

## 1. Run and provenance

### RunInvocation — **New**

One hand-started review.

| Field | Type | Rule |
|---|---|---|
| `pr_id` | int | Required. From `--pr` or `TFS_PR_ID`. Must be a positive integer; a non-existent id is a setup failure (FR-039), never a silent no-op |
| `dry_run` | bool | Defaults **true**. `--post` is the only way to make it false (FR-002) |
| `local_logic` | bool | True when `--local` was given (FR-012a) |
| `started_at` | datetime | Run clock, used for phase timings |

### AgentSource — **New**

Which copy of the review logic performed the review (FR-012b).

| Field | Type | Rule |
|---|---|---|
| `mode` | enum | `fetched` \| `local` |
| `commit` | str | Short SHA when `fetched`; empty when `local` |
| `ref` | str | Branch or tag fetched; empty when `local` |
| `repo` | str | Agent repository name |

**Invariant**: exactly one `AgentSource` per run, constructed by `bootstrap.py` before any review work, and
rendered as the first line of every report and of the posted summary comment. A run may not produce a report
without one.

### CredentialSet — **Changed** (was a single PAT + optional OAuth token)

| Field | Type | Rule |
|---|---|---|
| `read_token` | secret str | Required always. Clones both repositories; reads PR, changes, blobs, work items |
| `post_token` | secret str | Required **only** when `dry_run` is false (FR-008) |

**Invariants**: both values are registered with the redaction registry at load time, before any network or
subprocess call (Principle II). Neither is ever interpolated into a URL, an argument vector, or a log line
(research R3). A `TfsClient` is constructed with a `purpose` of `read` or `post` and can reach exactly one of
them; the dry-run path never constructs the `post` client.

---

## 2. Checklist ingestion

### ChecklistDocument — **New** (replaces `ChecklistFile`)

One `.xlsx` or `.docx` file found in the checklist repository.

| Field | Type | Rule |
|---|---|---|
| `filename` | str | As committed, e.g. `Najm_Coding_Standards v1.1.docx` |
| `format` | enum | `xlsx` \| `docx` |
| `status` | enum | `read` \| `skipped` \| `no_rules` |
| `skip_reason` | str | Required when `status == skipped`; empty otherwise |
| `rule_count` | int | 0 when not `read` |
| `sheets_ignored` | tuple[str] | Sheets with no rule-text column, e.g. `Document Control`, `lookups` |

**State transitions** — a document is in exactly one terminal state, and every state is reported (FR-018,
Principle VII):

```
discovered ─┬─► read      (≥1 rule extracted)
            ├─► no_rules  (parsed cleanly, yielded nothing)
            └─► skipped   (corrupt, password-protected, unsupported extension)
```

`no_rules` is deliberately distinct from `skipped`: a readable document that contributes nothing is a
content problem for the quality lead, while a skipped one is a technical problem for the developer. Merging
them would hide one behind the other.

### ChecklistItem — **New**

One review rule. The unit of citation.

| Field | Type | Rule |
|---|---|---|
| `item_id` | str | Required, unique within the combined checklist. Native (`SEC-WEB-01`, `#` value) or derived (`<stem>:<sheet>:<row>`, `<stem>:<n>`) |
| `title` | str | Required, non-empty — this is the rule-text column or the rule-marker title |
| `detail` | str | Optional supporting text, trimmed to 400 chars (research R6) |
| `severity` | str | `HIGH` \| `MEDIUM` \| `LOW` \| empty. Taken from the document; never invented |
| `section` | str | Enclosing heading or sheet name, for context |
| `documents` | tuple[str] | One or more source filenames — plural because of FR-020 |
| `derived_id` | bool | True when the identifier was derived rather than read |

**Validation**: an item with an empty `title` is never created. Two items whose normalised `title` matches
(case-folded, whitespace-collapsed, trailing punctuation removed) are **merged** into one item whose
`documents` lists both, per FR-020 — the surviving `item_id` is the first native identifier, or the first
derived one if none is native.

### CombinedChecklist — **Changed** (was `Checklist`, a single markdown blob)

| Field | Type | Rule |
|---|---|---|
| `items` | tuple[ChecklistItem] | The review's rule set |
| `documents` | tuple[ChecklistDocument] | Every document discovered, in every state |
| `dropped_for_budget` | tuple[str] | Item ids dropped at the `CHECKLIST_MAX_CHARS` cap (research R6) |
| `available` | bool | True when `len(items) > 0` |

**Invariant (FR-019)**: `available == False` in a manual run is a **setup failure**, not a degraded review —
the run stops and posts nothing. This reverses 001's behaviour deliberately; see research R11 and the
routing table below.

---

## 3. Review material

### ChangeSet, ExclusionList, ReviewBound

Carried over from 001 unchanged (`selection.py`). Recorded here only for the invariant this feature relies
on: every changed file ends in exactly one bucket — `reviewed`, `excluded` (with a pattern), or `unreviewed`
(over the bound) — and the last two are named in the report and the summary comment (FR-021c, FR-021e).
There is no fourth, silent bucket.

### LinkedStory, DeclaredIntent

Carried over. `LinkedStory` is optional: absent or unreadable degrades to a checklist-only review that says
so (FR-024) — it never stops the run.

---

## 4. Findings and output

### ChecklistViolation — **Changed**

| Field | Was | Now |
|---|---|---|
| `item` | free text | `item_id` — must match a `ChecklistItem.item_id` in the combined checklist |
| — | — | `item_title` — echoed for readability |
| `source` | checklist filename | `document` — the source filename, from the cited item |
| `issue` | free text | unchanged |
| `file`, `line` | anchor | unchanged |

**Validation (FR-027, research R12)**: a violation whose `item_id` is absent from the combined checklist is
**dropped** and counted in the report as a hallucinated citation. It is never posted. This is the one place
the agent silently discards model output, and it is reported rather than silent.

### Finding routing — the rule that decides what is posted where

| Finding kind | Dry run | Posting run | Blocks? |
|---|---|---|---|
| Checklist violation with file + line | printed, cited | inline comment at that line + cited in summary | no |
| Checklist violation without a location | printed, cited | in the summary comment | no |
| Violation citing an unknown `item_id` | printed as dropped, with count | dropped; count stated in summary | no |
| Coverage entry `not_addressed` | summary only | summary only | **never** (Principle V) |
| Excluded file | named with its pattern | named in summary | no |
| Unreviewed file (over bound) | named | named in summary | no |
| Document `skipped` / `no_rules` | named with reason | named in summary | no |

Nothing in this feature blocks or votes (FR-038, Principle VI, and the spec's Out of Scope).

### PostedFeedback — **New**

| Field | Type | Rule |
|---|---|---|
| `summary_comment` | str | Exactly one per posting run, always posted (FR-035a) — even for a clean review |
| `inline_comments` | tuple | One per located finding |
| `identity_marker` | str | `ai-review:` prefix, carried over from 001 (FR-037) |

**Invariant (FR-003)**: the dry-run renderer and the posting renderer consume the same `ReviewReport` and
produce the same text. A contract test asserts the dry-run output contains the summary body and every inline
body that the posting path would send.

### ReviewReport — **Changed**

Gains `agent_source` (§1), `documents` (§2), `dropped_citations`, and `dropped_for_budget`. Everything added
exists so that a run can state what it did *not* review — Principle VII.

---

## 5. Backend selection

### BackendProfile — **Changed**

| Field | Type | Rule |
|---|---|---|
| `provider` | str | Registry key: `claude` \| `openai` \| `azure` \| `ollama` \| `copilot` |
| `model` | str | e.g. `claude-sonnet-4-6` |
| `api_key` | secret str | Redacted; required unless the backend declares `requires_key = False` |
| `base_url` | str | Required for `azure`, `ollama`, `copilot`; defaulted for `claude`, `openai` |

**Validation (FR-034)**: an unknown `provider` is a setup failure whose message lists the registry's keys —
generated from the registry, so it can never drift from what is actually supported.

---

## 6. Configuration surface

Canonical names are the ones already provisioned in `.env` (research R10); 001 names remain as aliases.

| Variable | Required | Default | Feeds |
|---|---|---|---|
| `TFS_BASE_URL` | yes | — | all TFS access |
| `TFS_PROJECT` | yes | — | all TFS access |
| `TFS_PAT` | yes | — | `CredentialSet.read_token` |
| `TFS_PAT_FOR_PR` | posting runs only | — | `CredentialSet.post_token` |
| `TFS_REPO_CODE_SAMPLE` *(alias `TFS_REPO`)* | yes | — | the repo under review |
| `TFS_REPO_CODE_REVIEW_CodeReviewAgent` | yes unless `--local` | — | agent repo to fetch |
| `TFS_REPO_CODE_REVIEW_CodeReviewChecklistT` *(alias `CHECKLIST_REPO`)* | yes | — | checklist repo |
| `AGENT_REF` | no | `master` | ref to fetch |
| `CHECKLIST_BRANCH` | no | `master` | checklist ref |
| `CHECKLIST_MAX_CHARS` | no | `60000` | prompt budget (R6; measured 48,485 on the real corpus) |
| `LLM_PROVIDER` | no | `claude` | registry key |
| `LLM_MODEL` | yes | — | model id |
| `LLM_API_KEY` | yes (unless backend needs none) | — | backend auth |
| `LLM_BASE_URL` | for `azure`/`ollama`/`copilot` | — | backend endpoint |
| `DRY_RUN` | no | `true` | mode, overridden by `--dry-run`/`--post` |
| `REVIEW_MAX_FILES`, `REVIEW_MAX_DIFF_LINES`, `REVIEW_EXCLUDE_*`, `LLM_MAX_CHUNK_LINES` | no | 001 defaults | bound and exclusions |

**Invariant (Principle I)**: `config.py` remains the only module that reads the environment. `bootstrap.py`
receives a frozen config object like everything else, and the one environment variable it *writes* —
`REVIEW_AGENT_BOOTSTRAPPED` — is a process marker, not configuration, and is read back through `config.py`
like any other value.
