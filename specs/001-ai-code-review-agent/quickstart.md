# Quickstart: First Manual Dry Run

**Feature**: `001-ai-code-review-agent` | **Date**: 2026-08-26

Gets you from this repo to a printed review of a real pull request. Preview mode throughout — nothing
is posted until you ask for it.

---

## 0. Secure the repo first *(do this before any commit)*

`.env` is currently **tracked by git** and this repo's `origin` is the eShop repo on TFS. The committed
blob is empty, so nothing has leaked yet — but the working copy holds a live PAT and LLM key, and the
next commit that includes `.env` would publish both.

```bash
cat > .gitignore <<'EOF'
.env
.venv/
__pycache__/
.pytest_cache/
bin/
obj/
EOF

git rm --cached .env          # keeps the local file, stops tracking it
git status                    # confirm .env now shows as untracked/ignored
git log -p -- .env            # confirm no secret is in history (should show an empty blob)
```

Then rotate the PAT and the LLM key as a precaution — they have been sitting in a tracked file.

Do not run `/speckit-git-commit` or `git commit -a` before this step.

---

## 1. Environment

```bash
python -m venv .venv
.venv/Scripts/activate                       # Windows; use bin/activate on POSIX
pip install -r review_agent/requirements.txt
```

Requirements: `requests`, `python-dotenv`, `truststore`. `truststore` is what lets `requests` validate
`devops.najm.sa` against the private CA already trusted machine-wide — without it every call fails with
`SSLCertVerificationError`, and disabling verification is forbidden (FR-034).

---

## 2. Configuration

Your existing `.env` already has `TFS_BASE_URL`, `TFS_PROJECT`, `TFS_PAT`, `TFS_REPO`,
`CHECKLIST_REPO_URL`, `CHECKLIST_PATH`, `CHECKLIST_FILE_PATTERN`, `LLM_PROVIDER`, and `LLM_API_KEY`.

**Startup validation will fail until you add these:**

```dotenv
TFS_PR_ID=<the PR number to review>
LLM_MODEL=<model name>
CHECKLIST_BRANCH=master        # this collection integrates on master, not main
DRY_RUN=true
FAIL_ON_CONTRADICTION=false
```

The failure message lists every missing variable at once, so one run tells you everything to fix.

Full variable table: [`contracts/cli-contract.md`](./contracts/cli-contract.md).

---

## 3. Dry run

```bash
python review_agent/main.py --dry-run
```

Expect the phase log, then the exact markdown that would be posted:

```
ai-review run=1234.7 phase=config     outcome=ok ms=12
ai-review run=1234.7 phase=workitems  outcome=ok ms=340 count=1
...
ai-review run=1234.7 phase=post       outcome=dry-run ms=2 inline=2 suppressed=0 summary=create
ai-review run=1234.7 phase=done       outcome=complete ms=42972 exit=0
```

Preview mode makes **zero** write requests. Exit code 0.

### If it fails

| Symptom | Cause | Fix |
|---|---|---|
| `SetupFailure: missing required configuration: TFS_PR_ID, LLM_MODEL` | Step 2 incomplete | Add the listed variables |
| `SSLCertVerificationError` | `truststore` missing or not injected | `pip install truststore`; fallback `REQUESTS_CA_BUNDLE=<internal CA bundle>` |
| `authentication failed (HTML sign-in page received)` | Bad or expired PAT | Reissue the PAT with **Code (read & write)** scope |
| `api-version 6.0 rejected` | Older server | Try `TFS_API_VERSION=5.0`, then `4.1` |
| `checklist source unavailable` + review still runs | Wrong branch or path | Set `CHECKLIST_BRANCH=master`; check `CHECKLIST_PATH` |
| Exit 2, `404 pull request` | Wrong repo or PR id | Check `TFS_REPO` and `TFS_PR_ID` |

A checklist failure is a **degraded review, not a crash** — the coverage review still runs and exits 0
(FR-029).

---

## 4. Post for real

```bash
python review_agent/main.py --post
```

Adds one summary discussion plus an inline comment per located finding. Threads are created **active**,
so if your team has the "all comments resolved before merge" policy enabled, someone must resolve
them — including the summary on a clean review. That is the deliberate choice recorded in spec.md
SC-009; leave that policy off if you want the review fully out of the merge path.

Re-running is safe: the summary updates in place and already-posted findings are not repeated (FR-030).

---

## 5. The acceptance scenario (SC-001)

**The demo assets exist locally and are not yet on the remote.** The eShop remote still contains only an
empty `.env`, so the brief's "ready-made demo" was built here instead — `src/eShop.Api/`,
`demo/user-story-101.md`, `demo/pr-description.md`, and `demo/code-review-checklist.md` (workstream E in
[plan.md](./plan.md), tasks T006–T009). `CartController.AddItem` implements criteria 1, 2, 3, and 5 and
deliberately omits the stock-limit check, while the PR description claims stock checking is covered.

The steps below push them and wire the trigger:

1. Push this repo (code + `review_agent/` + `azure-pipelines.yml`) to TFS.
2. Create work item **#101** from `demo/user-story-101.md` — five acceptance criteria.
3. Seed the `TestMarkDown` repo with `demo/code-review-checklist.md` on the `CHECKLIST_BRANCH`.
4. Create `feature/add-to-cart`, commit the cart changes, open a PR into `master` with
   `demo/pr-description.md` as the description, linked to #101.
5. Run `python review_agent/main.py --dry-run` and confirm all five expectations:

| # | Expected |
|---|---|
| 1 | Criteria 1, 2, 3, 5 → `appears_addressed` |
| 2 | **Criterion 4 flagged as a false coverage claim** — the headline result |
| 3 | Checklist item "business rules from the story are enforced" raised as a violation |
| 4 | Scoped-out follow-ups #102 and #103 unflagged |
| 5 | One summary + an inline flag on `CartController.AddItem` |

6. Wire the pipeline as a **master branch policy** (Repos → Branches → `master` → Branch policies →
   Build Validation), then open the PR again and confirm the same feedback posts automatically.

Two one-time settings the pipeline needs, or posting silently fails with a 403:
- **"Allow scripts to access the OAuth token"** on the job.
- **Contribute to pull requests** for the build service identity on the eShop repo.

---

## 6. Tests

```bash
pip install -r review_agent/requirements-dev.txt
pytest tests/ -v
```

No live network — the suite runs against recorded fixtures. The contract tests are the ones worth
watching: they assert no secret reaches stdout, no `verify=False` exists anywhere, preview mode issues
no writes, and `not_addressed` never produces a comment.
