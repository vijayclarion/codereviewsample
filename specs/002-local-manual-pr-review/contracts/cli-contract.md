# Contract: Command-line surface

**Feature**: `002-local-manual-pr-review` | Supersedes the 001 CLI contract for manual runs.

## Invocation

```
python -m review_agent --pr <id> [--dry-run | --post] [--local] [--quiet] [--version]
```

| Argument | Required | Meaning |
|---|---|---|
| `--pr <id>` | yes (falls back to `TFS_PR_ID`) | The pull request to review. Positive integer (FR-001) |
| `--dry-run` | no | Print the review, post nothing. **Default** (FR-002) |
| `--post` | no | Post the review to the pull request. Mutually exclusive with `--dry-run` |
| `--local` | no | Run the local working copy instead of the fetched agent source (FR-012a) |
| `--quiet` | no | Suppress the phase log; the report still prints |
| `--version` | no | Print version and exit |

Safety: the default is dry-run in both the CLI and `DRY_RUN`. Posting is only ever reached by an explicit
`--post` or `DRY_RUN=false`. There is no flag that approves, rejects, votes on, or completes a pull request,
and none may be added (FR-038, Principle VI).

## Exit codes

| Code | Meaning | Examples |
|---|---|---|
| `0` | The review completed. Findings do not change this | Violations found and posted; clean review; a document was skipped but others were read |
| `1` | Reserved. **Not used by this feature** — gating is out of scope | — |
| `2` | Setup failure — the run could not start or could not deliver | Missing config value; pull request not found (FR-039); no readable checklist rule (FR-019); unknown backend (FR-034); posting credential lacks permission (US2-3); neither repository could be fetched (FR-014) |
| `3` | Degraded review — it started but could not produce a review | Backend unreachable or unusable response after the retry (FR-040) |

Exit `3` is new in this feature: 001 folded degradation into `0` so that provider flakiness could never gate
a merge in a pipeline. Nothing gates a merge here — a developer is reading the terminal — so a run that
produced no review must not exit `0` alongside one that did (Principle VII, FR-004).

## Output contract

Every run, in both modes, prints in this order:

1. **Provenance line** — `review logic: fetched a1b2c3d (CodeReviewAgent@master)` or
   `review logic: LOCAL WORKING COPY` (FR-012b)
2. **Checklist line** — `checklist: 148 rules from 5 documents (2 sheets ignored)`
3. **Phase log** unless `--quiet`
4. **The report** — findings with citations, then the skipped/unreviewed sections
5. **Outcome line** — `completed` / `setup failure: <cause>` / `degraded: <cause>` (FR-004)

In dry-run the report is followed by `posted: nothing (dry run)`. In a posting run it is followed by
`posted: 1 summary + N inline comments`.

A finding renders as:

```
  SEC-WEB-01  Prevention of Cross-Site Scripting (XSS)          [HIGH]
  source:  Najm_Application_Security_Guidelines v1.0.docx
  where:   src/eShop.Api/Controllers/CartController.cs:48
  issue:   User-supplied `note` is concatenated into an HTML response without encoding.
```

Both citation fields are mandatory (FR-027); a finding cannot render without them.

## Guarantees a test asserts

| Guarantee | Requirement | Test |
|---|---|---|
| A dry run performs zero writes to TFS | FR-002 | `test_preview_makes_no_writes.py` (extended: asserts the posting client is never constructed) |
| A dry run needs no posting credential | FR-008 | `test_auth_modes.py` |
| Dry-run text equals posted text | FR-003 | `test_dry_run_matches_posted.py` (new) |
| No credential appears in stdout, stderr, or logs | FR-009, Principle II | `test_no_secrets_leak.py` (extended: git subprocess env and argv) |
| A non-existent pull request exits 2 and posts nothing | FR-039, SC-005 | `test_missing_pr_fails_clean.py` (new) |
| Output survives a cp1252 console | — | `test_output_encoding.py` (existing; now also fed real checklist glyphs `ℹ ✖ — ’`) |
