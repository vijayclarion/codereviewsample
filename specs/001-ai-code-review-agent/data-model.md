# Data Model: AI Code Review Agent

**Feature**: `001-ai-code-review-agent` | **Date**: 2026-08-26

All entities are in-memory only — the agent has no database and no local state. Types are given as
Python shapes since the entities exist as frozen dataclasses inside one process.

---

## Lifecycle

```
Config ──> PullRequestContext ──> [WorkItem…]      ──┐
                              ──> DeclaredScope     │
                              ──> ChangeSet ──> ReviewableSet ──> PromptChunk[…] ──┐
                              ──> Checklist ───────┘                               │
                                                                                   v
   PostedFeedback <── FeedbackPlan <── ReviewReport <── LlmResponse (validated) <───┘
        ^                    ^
        └── PriorFeedback ───┘   (read back from the PR's existing threads)
```

---

## Config

Frozen, built once at startup. Any validation failure is a `SetupFailure` listing **every** missing or
invalid value at once (FR-003), never the first one only.

| Field | Type | Required | Default | Notes |
|---|---|---|---|---|
| `tfs_base_url` | `str` | yes | — | Collection URL; trailing slash stripped |
| `tfs_project` | `str` | yes | — | |
| `tfs_repo` | `str` | yes | — | Name or id |
| `tfs_pr_id` | `int` | yes | — | Must parse as a positive int |
| `tfs_api_version` | `str` | no | `6.0` | |
| `auth_mode` | `"bearer" \| "basic"` | derived | — | Bearer when `SYSTEM_ACCESSTOKEN` is non-empty (R2) |
| `tfs_token` | `secret str` | yes | — | The pipeline token or the PAT; registered for redaction |
| `checklist_repo` | `str` | derived | from `CHECKLIST_REPO_URL` | Last `_git/{name}` segment; overridable |
| `checklist_path` | `str` | no | `/` | |
| `checklist_file_pattern` | `str` | no | `*.md` | |
| `checklist_branch` | `str` | no | `main` | `.env.example` sets `master` (R9) |
| `llm_provider` | `"claude" \| "openai" \| "azure" \| "ollama"` | yes | — | |
| `llm_model` | `str` | yes | — | |
| `llm_api_key` | `secret str` | conditional | — | Required unless provider is `ollama` |
| `llm_base_url` | `str \| None` | no | provider default | Required for `azure` and `ollama` |
| `dry_run` | `bool` | no | `true` | Non-blocking default (FR-007) |
| `fail_on_contradiction` | `bool` | no | `false` | |
| `review_exclude_patterns` | `tuple[str, …]` | no | built-in (R10) | |
| `review_exclude_mode` | `"extend" \| "replace"` | no | `extend` | |
| `review_max_files` | `int` | no | `40` | |
| `review_max_diff_lines` | `int` | no | `3000` | |
| `llm_max_chunk_lines` | `int` | no | `1200` | |

**Precedence**: pipeline variable → `.env` → default. Secrets never appear in `repr()`; the dataclass
overrides `__repr__` to print `***` for every secret field.

---

## PullRequestContext

| Field | Type | Source |
|---|---|---|
| `pr_id` | `int` | config |
| `title` | `str` | PR detail |
| `description` | `str` | PR detail — raw markdown, used for `DeclaredScope` |
| `source_branch` / `target_branch` | `str` | PR detail |
| `iteration_id` | `int` | latest iteration |
| `iteration_commit` / `base_commit` | `str` (sha) | iteration `sourceRefCommit` / `commonRefCommit` |
| `run_id` | `str` | `f"{pr_id}.{iteration_id}"` — ties log lines together |

---

## WorkItem

Zero, one, or many. Empty list is valid and means checklist-only review (FR-008).

| Field | Type | Notes |
|---|---|---|
| `id` | `int` | |
| `title` | `str` | HTML-stripped |
| `description` | `str` | HTML-stripped |
| `acceptance_criteria` | `str` | HTML-stripped; from `Microsoft.VSTS.Common.AcceptanceCriteria` |
| `work_item_type`, `state` | `str` | |
| `tags` | `tuple[str, …]` | Split on `; ` |
| `criteria` | `tuple[AcceptanceCriterion, …]` | Enumerated from `acceptance_criteria`, falling back to `description` when that field is empty |

**Criterion enumeration**: split on markdown list markers (`-`, `*`, `1.`), `<li>` boundaries, and
blank-line-separated paragraphs, in that order of preference; drop entries shorter than 3 words. The
criteria list is *advisory* — the model receives the full criteria text as well, so a mis-split degrades
the coverage map's granularity but never loses information.

---

## AcceptanceCriterion

| Field | Type | Notes |
|---|---|---|
| `ordinal` | `int` | 1-based; how the summary refers to it ("criterion 4") |
| `text` | `str` | |
| `work_item_id` | `int` | Which story it came from |

---

## DeclaredScope

Parsed from the PR description's "Covered / Not in this PR" section.

| Field | Type | Notes |
|---|---|---|
| `covered` | `tuple[str, …]` | The author's claim — authoritative for intent, verified against the diff |
| `not_in_this_pr` | `tuple[str, …]` | Never flagged (FR-018) |
| `parsed` | `bool` | `False` when no such section exists — then no false-coverage claim can be derived, and the summary says so |

**Parsing**: locate a heading or bold line matching `/cover(ed)?/i` and `/not in this PR|out of scope|
follow-?up/i`, take the list items beneath each. Case- and punctuation-insensitive. If only one of the
two sections is present, the other is empty and `parsed` is still `True`.

---

## ChangeSet → ReviewableSet

`ChangeSet` is everything the iteration changed. `ReviewableSet` is what survives exclusion and the
bound — and, critically, it also carries what did *not* survive, because both must appear in the report.

**ChangeSet**

| Field | Type |
|---|---|
| `files` | `tuple[ChangedFile, …]` |

**ChangedFile**

| Field | Type | Notes |
|---|---|---|
| `path` | `str` | Repo-relative, forward slashes |
| `change_type` | `"add" \| "edit" \| "delete" \| "rename"` | |
| `is_binary` | `bool` | Detected by extension and by a NUL byte in the fetched blob |
| `diff` | `str \| None` | Unified diff, 3 lines of context (R3) |
| `line_map` | `dict[int, int]` | diff line → new-file line (R3), for anchoring |
| `changed_lines` | `int` | Added + removed; drives the bound |
| `relevance` | `float` | R11 score |

**ReviewableSet**

| Field | Type | Notes |
|---|---|---|
| `reviewed` | `tuple[ChangedFile, …]` | Sent to the provider |
| `excluded` | `tuple[SkippedFile, …]` | FR-014c — named in the report with the matched pattern |
| `unreviewed` | `tuple[SkippedFile, …]` | FR-014f — bound reached; named with change size |
| `is_empty` | `bool` | Nothing reviewable → FR-014d "nothing to review" |

**SkippedFile**: `path`, `reason` (`"excluded" \| "bound" \| "binary" \| "deleted"`), `detail` (the
matched glob, or the change size).

---

## Checklist

| Field | Type | Notes |
|---|---|---|
| `sources` | `tuple[ChecklistFile, …]` | One per matching file |
| `combined_markdown` | `str` | Each section prefixed `## From: {filename}` (FR-012) |
| `available` | `bool` | `False` → coverage-only degraded review (FR-029, spec US2 scenario 5) |

**ChecklistFile**: `filename`, `path`, `content`.

---

## PromptChunk

| Field | Type | Notes |
|---|---|---|
| `index` / `total` | `int` | Stated in the prompt so the model knows it sees a part |
| `files` | `tuple[ChangedFile, …]` | Whole files where possible (R12) |
| `text` | `str` | The assembled prompt |
| `estimated_size` | `int` | `len(text) // 4` |

Story, combined checklist, and declared scope are repeated in **every** chunk (FR-014).

---

## ReviewReport

The validated model output, merged across chunks. Schema enforced by
`contracts/llm-response.schema.json`.

| Field | Type | Notes |
|---|---|---|
| `coverage` | `tuple[CoverageEntry, …]` | |
| `checklist_violations` | `tuple[ChecklistViolation, …]` | |
| `code_issues` | `tuple[CodeIssue, …]` | |
| `summary` | `str` | |
| `false_coverage_claims` | `tuple[FalseCoverageClaim, …]` | **Derived, not model-supplied** — see below |
| `skipped` | `tuple[SkippedFile, …]` | Carried from `ReviewableSet` |
| `outcome` | `"complete" \| "degraded"` | FR-029 |
| `degraded_reason` | `str \| None` | Stated on the PR when degraded |

**CoverageEntry**: `criterion` (str), `status` (enum), `evidence` (str), `ordinal` (int \| None).
**ChecklistViolation**: `item`, `source` (checklist filename), `issue`, `file`, `line`.
**CodeIssue**: `severity` (enum), `issue`, `file`, `line`.
**FalseCoverageClaim**: `criterion`, `claimed_text` (the matching "Covered" line), `status` (the
coverage status that contradicts the claim), `evidence`.

### Derivation of false coverage claims

Computed in `parser.py`, not asked of the model:

```
weights = distinctiveness(all criterion texts)      # log(1 + N/df) per token
for each covered_item in DeclaredScope.covered:
    for each CoverageEntry:
        score = sum(weights[t] for t in tokens(covered_item) & tokens(entry.criterion))
    best, runner_up = two highest scores
    matched = best >= MIN_DISTINCTIVE_WEIGHT (1.0)
              and (runner_up == 0 or best >= runner_up * RUNNER_UP_RATIO (1.4))
    if matched and entry.status != "appears_addressed":
        emit FalseCoverageClaim
    elif not matched:
        report as an unmatched claim
```

**Why derived**: the model already reports the coverage status and already sees the declared scope;
asking it for a fourth list invites disagreement between that list and its own `coverage` array. Deriving
it makes the headline finding a deterministic function of the coverage map — so the same coverage map
always yields the same headline, and the SC-001 regression test pins one rule rather than two.

**Why distinctive-token weighting rather than plain overlap** — this is the part worth reading before
changing the matcher. The obvious rule (token overlap ≥ 0.5 as a fraction of the shorter side) was
implemented first and **failed on the reference scenario**: the description line "Stock limit enforced
when adding to cart" and criterion 4 "The system must not allow adding more items than are available in
stock" share exactly one content word, scoring 0.25 — so the feature's headline finding was silently
missed while every test that did not exercise the real wording still passed.

Raw overlap is the wrong metric because it treats every shared word as equally identifying. Weighting by
how few criteria contain a token fixes that: `stock` appears in one criterion out of five and is
therefore decisive on its own, while `cart` appears in three and identifies nothing. The runner-up
margin then refuses to guess — a claim that matches two criteria equally well identifies neither, and
placing the headline on the wrong criterion is worse than reporting it as unmatched.

An unmatched "Covered" line (no criterion clears the distinctiveness floor, or two tie) is reported in
the summary as "claimed but not matched to any acceptance criterion" rather than silently dropped.

`claim_overlap` (plain fraction-of-shorter overlap) still exists and is still correct for its one
remaining use: matching the model's near-verbatim echo of a criterion back to the story's numbered
criterion, where both sides are restatements of the same sentence.

---

## Validation rules

| Rule | Source | Enforced in |
|---|---|---|
| `status` ∈ {`appears_addressed`, `not_addressed`, `possible_contradiction`} | FR-015 | `parser.py` + JSON schema |
| `severity` ∈ {`high`, `medium`, `low`} | FR-020 | `parser.py` + JSON schema |
| All four top-level keys present; arrays may be empty; `summary` non-empty | FR-022 | JSON schema |
| `line` is an int ≥ 0; `0` means "no specific line" | FR-025 | `parser.py` |
| `file` must match a path in `ReviewableSet.reviewed`, else the finding routes to the summary | FR-025 | `output_layer.py` |
| Unknown extra keys are ignored, not rejected | robustness | `parser.py` |
| Malformed → one stricter retry → degraded, never partial | FR-022, FR-029 | `parser.py` |

Rejecting on an unknown extra key would turn a chatty model into a degraded review for no reader-visible
benefit, so extras are dropped.

---

## PriorFeedback

Read back from the PR's threads each run (R5). This is what makes re-review incremental.

| Field | Type | Notes |
|---|---|---|
| `summary_thread_id` | `int \| None` | Thread whose marker is `ai-review:summary` |
| `summary_comment_id` | `int \| None` | The comment to `PATCH` in place |
| `posted_fingerprints` | `frozenset[str]` | Every `ai-review:` fingerprint already on the PR |
| `own_thread_ids` | `frozenset[int]` | For diagnostics only — never deleted or resolved |

---

## FeedbackPlan → PostedFeedback

**FeedbackPlan** is what the run intends to do; rendering it is identical in dry-run and live mode, which
is what makes FR-026 ("present exactly the feedback it would otherwise post") testable by comparing the
two renderings.

| Field | Type | Notes |
|---|---|---|
| `summary_markdown` | `str` | Summary + coverage map + false-coverage callout + skipped/unreviewed files |
| `inline` | `tuple[InlineComment, …]` | Only findings with a usable `file` + mapped `line` |
| `summary_only` | `tuple[Finding, …]` | Findings with no usable location (FR-025) |
| `suppressed` | `tuple[Finding, …]` | Fingerprint already posted (FR-030) |
| `action` | `"create_summary" \| "update_summary"` | Depends on `PriorFeedback` |

**InlineComment**: `path`, `right_line`, `markdown`, `fingerprint`.

### Routing table (FR-016, FR-023, FR-024, FR-025)

| Finding | Inline comment? | In summary? | Counts toward blocking exit? |
|---|---|---|---|
| `appears_addressed` | no | yes (coverage map) | no |
| `not_addressed` | **never** | yes (coverage map, neutral) | **never** |
| `possible_contradiction` | yes, if located | yes | yes |
| False coverage claim | yes, if located | yes — **called out prominently** | yes |
| `checklist_violation` | yes, if located | yes | no |
| `code_issue` severity `high` | yes, if located | yes | yes |
| `code_issue` severity `medium` / `low` | yes, if located | yes | no |
| Excluded / unreviewed file | no | yes (named, with reason) | no |

Blocking exit applies only when `FAIL_ON_CONTRADICTION=true` (FR-028).

---

## Thread state

Every thread the agent creates is created **active** (FR-024a) and is never resolved or deleted by the
agent. There are no other state transitions — the agent has no lifecycle to manage beyond
create-or-update.

| Transition | Actor |
|---|---|
| (none) → active | agent, on first post of a finding |
| summary comment content → updated | agent, on each re-review (R5) |
| active → resolved / closed | **human only** |
