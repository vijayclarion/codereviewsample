# Quickstart: your first manual review

**Feature**: `002-local-manual-pr-review` | Audience: a developer on this collection, first run.

The whole point of this feature is that the first run is safe: **dry run is the default**, and nothing
reaches a pull request until you type `--post`.

---

## Before you start

You need three things:

1. **Python 3.11+** (3.13.14 is what this was developed against).
2. **A clone of the agent repository**:
   ```
   git clone https://devops.najm.sa/tfs/Najm/Najm-Architecture-Unit/_git/CodeReviewAgent
   cd CodeReviewAgent
   pip install -r review_agent/requirements.txt
   ```
3. **Two personal access tokens** on `devops.najm.sa`:

   | Token | Scopes | Used for |
   |---|---|---|
   | `TFS_PAT` | Code (read), Work items (read) | cloning the agent and checklist repos; reading the PR, its changes and its work item |
   | `TFS_PAT_FOR_PR` | Code (read), **Pull request threads (read & write)** | reading and posting comments |

   Two tokens, not one, so a dry run can be proven never to touch the posting credential.

## Configure

Copy `review_agent/.env.example` to `.env` in the repository root and fill it in:

```ini
TFS_BASE_URL=https://devops.najm.sa/tfs/Najm
TFS_PROJECT=Najm-Architecture-Unit
TFS_PAT=<your read token>
TFS_PAT_FOR_PR=<your posting token>

TFS_REPO_CODE_SAMPLE=CodeReviewSample
TFS_REPO_CODE_REVIEW_CodeReviewAgent=CodeReviewAgent
TFS_REPO_CODE_REVIEW_CodeReviewChecklistT=CodeReviewChecklist

LLM_PROVIDER=claude
LLM_MODEL=claude-sonnet-4-6
LLM_API_KEY=<your key>

DRY_RUN=true
```

`.env` is already covered by `.gitignore`. Never commit it, and never paste a token into a command line —
the agent reads them from here and nowhere else.

## Your first run

Open a pull request in `CodeReviewSample`, note its number, then:

```
python -m review_agent --pr 42
```

That is a dry run. It will:

1. fetch the agent repository and re-run itself from that copy — the first output line tells you which copy
   actually reviewed;
2. fetch the checklist repository and read all five documents;
3. fetch the pull request, its changes, and its linked work item if it has one;
4. review, and **print** the findings.

Expect something like:

```
review logic: fetched a1b2c3d (CodeReviewAgent@master)
checklist:    148 rules from 5 documents (4 sheets ignored)

  SEC-WEB-01  Prevention of Cross-Site Scripting (XSS)          [HIGH]
  source:  Najm_Application_Security_Guidelines v1.0.docx
  where:   src/eShop.Api/Controllers/CartController.cs:48
  issue:   User-supplied `note` is concatenated into an HTML response without encoding.

posted: nothing (dry run)
completed
```

## Posting for real

When the preview looks right:

```
python -m review_agent --pr 42 --post
```

You get one inline comment per located finding, plus one summary comment carrying the outcome, any finding
without a location, and everything that was skipped or not reviewed. The summary comment is posted even when
the review is clean — a silent pull request should never be mistaken for a reviewed one.

## Developing the agent itself

The default run reviews with the **committed** logic, so your uncommitted edits have no effect. While
working on the agent, use:

```
python -m review_agent --pr 42 --local
```

The provenance line then reads `review logic: LOCAL WORKING COPY`, so a review produced this way can never
be mistaken for one produced by the committed logic.

---

## Troubleshooting

| What you see | What it means | Fix |
|---|---|---|
| `setup failure: missing TFS_PAT, LLM_API_KEY` | Config validated before any work | Fill both in `.env` |
| `setup failure: pull request 999 was not found in CodeReviewSample` | Exit 2, nothing posted | Check the number and the repo |
| `setup failure: no checklist rules could be read from 5 documents` | Every document was unusable | See the per-document reasons printed above the error |
| `setup failure: TFS_PAT_FOR_PR is required to post` | You used `--post` without the posting token | Add it, or drop `--post` |
| `setup failure: posting is not permitted with this credential` | Token lacks PR thread write | Re-issue with *Pull request threads (read & write)*; your findings are printed above the error |
| `degraded: the AI provider was unreachable after 3 attempts` | Exit 3 — no review produced, nothing posted | Retry; check `LLM_BASE_URL` for `azure`/`ollama`/`copilot` |
| `checklist: … (2 documents skipped)` | Some documents unreadable, run continued | Reasons are printed per document; a password-protected file must be unprotected by its owner |
| A rule you expect is missing | The document may have no rule-text column | Check the per-document line; see `contracts/checklist-extraction.md` |
| `UnicodeEncodeError` in your terminal | cp1252 console vs `ℹ ✖ —` in the documents | `set PYTHONIOENCODING=utf-8`, or use Windows Terminal |

## What this will not do

It comments. It does not approve, reject, vote, complete, or block anything — a human decides the merge.
It does not build, run, or test the code under review, so treat its findings as a reviewer's suggestions.
