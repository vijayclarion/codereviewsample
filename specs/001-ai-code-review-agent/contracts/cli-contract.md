# Contract: CLI Surface, Configuration, and Exit Codes

**Feature**: `001-ai-code-review-agent` | **Date**: 2026-08-26

The agent's public interface. Anything not listed here is internal and may change freely.

---

## Invocation

```
python review_agent/main.py [--dry-run | --post] [--pr <id>] [--quiet] [--help] [--version]
```

| Flag | Effect |
|---|---|
| `--dry-run` | Force preview mode — print the feedback, post nothing. Overrides `DRY_RUN`. |
| `--post` | Force live posting. Overrides `DRY_RUN`. Mutually exclusive with `--dry-run`. |
| `--pr <id>` | Override `TFS_PR_ID`. Convenience for manual runs against a second PR. |
| `--quiet` | Suppress the phase log. The feedback itself still prints in preview mode. |
| `--help` | Print usage and the full variable table. Exit 0. Never touches the network. |
| `--version` | Print the agent version. Exit 0. |

No flag carries a secret — credentials come from configuration only, so they never appear in a shell
history or a process list.

---

## Configuration

Precedence: **pipeline variable → `.env` → default**. Pipeline values win so a validation build needs no
`.env` on the agent (FR-002).

### Always required

| Variable | Notes |
|---|---|
| `TFS_BASE_URL` | Collection URL, e.g. `https://devops.najm.sa/tfs/Najm`. Pipeline: `System.TeamFoundationCollectionUri` |
| `TFS_PROJECT` | Pipeline: `System.TeamProject` |
| `TFS_REPO` | Name or id. Pipeline: `Build.Repository.Name` |
| `TFS_PR_ID` | Positive integer. Pipeline: `System.PullRequest.PullRequestId` |
| `LLM_PROVIDER` | `claude` \| `openai` \| `azure` \| `ollama` |
| `LLM_MODEL` | |

### Authentication — exactly one mode

| Variable | Notes |
|---|---|
| `SYSTEM_ACCESSTOKEN` | Pipeline OAuth token → `Authorization: Bearer …`. **Preferred when non-empty.** Requires "Allow scripts to access the OAuth token" on the job. |
| `TFS_PAT` | Manual runs → `Authorization: Basic base64(":" + PAT)`. Not required when the pipeline token is present. |

An empty-string `SYSTEM_ACCESSTOKEN` counts as absent (R2) — that is exactly what a job without the
OAuth-token setting exports, and treating it as present produces a baffling auth error instead of a clear
setup message.

### Conditionally required

| Variable | Required when |
|---|---|
| `LLM_API_KEY` | `LLM_PROVIDER` ≠ `ollama` |
| `LLM_BASE_URL` | `LLM_PROVIDER` ∈ {`azure`, `ollama`} |

### Optional, with defaults

| Variable | Default | Meaning |
|---|---|---|
| `TFS_API_VERSION` | `6.0` | REST api-version |
| `CHECKLIST_REPO_URL` | — | Git URL of the checklist repo; the repo name is derived from its `_git/{name}` segment |
| `CHECKLIST_REPO` | derived | Explicit override when derivation is wrong |
| `CHECKLIST_PATH` | `/` | Folder to read checklists from; recursive |
| `CHECKLIST_FILE_PATTERN` | `*.md` | Which files count as checklists |
| `CHECKLIST_BRANCH` | `main` | Branch to read checklists from. **This collection integrates on `master`** — `.env.example` sets it explicitly (R9) |
| `DRY_RUN` | `true` | `true` prints, `false` posts |
| `FAIL_ON_CONTRADICTION` | `false` | `true` → exit 1 on a blocking finding |
| `REVIEW_EXCLUDE_PATTERNS` | built-in | Comma-separated globs never sent to the provider |
| `REVIEW_EXCLUDE_MODE` | `extend` | `extend` or `replace` the built-in list |
| `REVIEW_MAX_FILES` | `40` | Review bound |
| `REVIEW_MAX_DIFF_LINES` | `3000` | Review bound |
| `LLM_MAX_CHUNK_LINES` | `1200` | Split into another pass above this |

Boolean parsing accepts `true/false`, `1/0`, `yes/no`, `on/off`, case-insensitive. Anything else is a
validation error naming the variable and what it received — not a silent `False`.

---

## Exit codes

| Code | Meaning | When |
|---|---|---|
| `0` | Review completed, **or** degraded (FR-029) | Normal outcome. Also the outcome when findings exist and `FAIL_ON_CONTRADICTION=false` — the default (FR-027). Also a degraded review: provider unreachable, response unusable after the retry, checklist source unreachable, nothing reviewable. |
| `1` | Blocking findings | Only when `FAIL_ON_CONTRADICTION=true` **and** the report contains a `possible_contradiction`, a false coverage claim, or a `high`-severity code issue (FR-028). |
| `2` | Setup failure (FR-029) | Missing or invalid configuration, no usable credential, unreachable or unauthorized pull request, unsupported api-version, or no permission to post. |

**Exit 0 covers both "reviewed fine" and "could not review"** — deliberately. A provider outage must not
gate anyone's merge (the Q4 clarification), and the distinction a human needs is carried in the summary
discussion and the run log, not the exit code. Exit 2 is reserved for the class of failure that means the
review will *never* work until someone changes a setting, which is exactly what should turn a build red.

`--help` and `--version` always exit 0.

---

## Output on stdout

Two streams of text, both passing through the redaction filter (R8):

**Structured run log** — one line per phase:

```
ai-review run=1234.7 phase=config      outcome=ok       ms=12
ai-review run=1234.7 phase=workitems   outcome=ok       ms=340  count=1
ai-review run=1234.7 phase=checklists  outcome=ok       ms=520  files=2
ai-review run=1234.7 phase=changes     outcome=ok       ms=890  changed=6 reviewed=5 excluded=1 unreviewed=0
ai-review run=1234.7 phase=llm         outcome=ok       ms=41200 chunks=1 retries=0
ai-review run=1234.7 phase=parse       outcome=ok       ms=8    coverage=5 violations=1 issues=0 false_claims=1
ai-review run=1234.7 phase=post        outcome=dry-run  ms=2    inline=2 suppressed=0 summary=create
ai-review run=1234.7 phase=done        outcome=complete ms=42972 exit=0
```

Never contains a credential, a request or response body, or a header value.

**Preview rendering** (dry-run only) — exactly the markdown that would be posted, per
`pr-comment-format.md`, so preview and live output are the same text (FR-026).

---

## Guarantees

- No configuration value is read outside `config.py`; no module calls `os.environ` directly (FR-001).
- No credential appears in stdout, stderr, the prompt, or posted feedback (FR-006) — enforced by a
  contract test asserting each secret's literal value is absent from a captured full run.
- TLS verification is never disabled; `verify=` is never passed to `requests` (FR-034).
- Preview mode makes zero write requests — asserted by a test counting non-GET calls (FR-007).
- The agent never votes on, approves, completes, or abandons a pull request, and never resolves a
  comment thread (spec Out of Scope, FR-024a).
