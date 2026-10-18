# Spec: AI Code Review Agent for On-Prem TFS

## 1. Goal and scope

Build a script-based AI agent that reviews TFS/Azure DevOps Server pull requests against (a) the linked user story and (b) a shared code-review checklist, then posts structured feedback back to the PR.

The agent runs **live against the real on-prem TFS** via REST + a PAT, both as a pipeline step and when invoked manually from a networked machine. A `--dry-run` flag runs the full pipeline but prints the feedback instead of posting it.

It is a **reviewer aid, not a merge gate by default**. It reasons over diff text and cannot verify runtime behavior; output must be framed as "likely covered / likely missing," not proof.

## 2. Target environment

- On-prem **TFS / Azure DevOps Server** (not Azure DevOps Services).
- Base/collection URL: `https://devops.najm.sa/tfs/najm`
- Project: `Najm-Architecture-Unit`
- REST API version: `6.0` (confirm against the deployed TFS version — **TFS version is an open input** and determines which endpoints exist; older TFS is more limited).
- Runs as a pipeline step on an existing self-hosted build agent. **Nothing is installed on the TFS server.**

## 3. Inputs

Three inputs, each fetched live:

| Input | Source |
|---|---|
| Code diff | PR iterations/changes REST or `git diff origin/target...origin/source` |
| User story | Work-item REST (`$expand=all`) |
| Shared checklist | Central Git repo `code-review-checklist.md` |
| PR description | PR REST |

Story `System.Description` and `Microsoft.VSTS.Common.AcceptanceCriteria` come back as **HTML — strip tags** before sending to the LLM.

## 4. Configuration contract

All configuration via environment variables. **No secrets in code.** Add the env file to `.gitignore`.

```
TFS_BASE_URL=https://devops.najm.sa/tfs/najm
TFS_PROJECT=Najm-Architecture-Unit
TFS_PAT=<token>
TFS_REPO=<repository name or id>
TFS_PR_ID=<pull request number>
CHECKLIST_REPO_URL=https://devops.najm.sa/tfs/Najm/Najm-Architecture-Unit/_git/TestMarkDown
LLM_PROVIDER=claude          # claude | openai | azure | ollama
LLM_MODEL=<model name>
LLM_API_KEY=<key>
```

`--dry-run` runs the full pipeline but prints feedback instead of posting to the PR.

Minimum PAT scopes: **Code (Read)**, **Code (Read & Write)** or **Pull Request Threads (Read & Write)**, **Work Items (Read)**. Short expiry; revoke after use.

## 5. Processing behavior

**Coverage map** — for each acceptance criterion, classify as exactly one:
- `appears_addressed` — diff plausibly implements it.
- `not_addressed` — no related changes. **This is neutral, never an error** (a PR usually implements only a slice of a story).
- `possible_contradiction` — diff conflicts with the criterion. **This is the only loud flag.**

**Scope reconciliation** — the PR description's "Covered / Not in this PR" section is *authoritative scope*. Criteria the author defers → mark out of scope, do not flag. BUT still verify implementation against the diff: if the description claims "Covered: X" and the diff shows nothing for X → flag the false coverage claim.

**What becomes a problem:** only `possible_contradiction`, real `code_issues`, and false coverage claims. `not_addressed` rows stay informational for the human reviewer.

**Empty-link case:** if no work item is linked to the PR, post "No user story linked to this PR — requirement coverage was not checked" and continue with checklist-only review.

## 6. Output contract

The LLM must return **exactly this JSON** (parser is vendor-agnostic and depends on this shape):

```json
{
  "coverage": [
    {"criterion": "...", "status": "appears_addressed|not_addressed|possible_contradiction", "evidence": "..."}
  ],
  "checklist_violations": [{"item": "...", "issue": "...", "file": "...", "line": 0}],
  "code_issues": [{"severity": "high|medium|low", "issue": "...", "file": "...", "line": 0}],
  "summary": "..."
}
```

**Routing to the PR:**
- `summary` + full coverage map → one summary thread on the PR.
- `possible_contradiction`, `checklist_violations`, and `code_issues` with a file/line → **inline comments** anchored via `threadContext`.
- Optional pass/fail gate (off by default): fail only on high-severity `code_issues` or `possible_contradiction`.

## 7. Interfaces / boundaries (must be replaceable)

- **LLM adapter** — one function `call_llm(prompt) -> normalized_json`. Hides provider differences (Claude `content[].text`, OpenAI `choices[].message.content`). Swapping providers touches only this module.
- **TFS client** — all REST calls isolated behind functions (`get_linked_work_items`, `get_work_item`, `get_diff`, `get_pr_description`, `post_thread`).

## 8. Module structure

```
review_agent/
  tfs_client.py       # all TFS REST calls
  prompt_builder.py   # assembles story + checklist + diff + scope into the prompt
  llm_adapter.py      # provider-agnostic call_llm()
  parser.py           # validates + parses the JSON contract
  output_layer.py     # routes summary/inline comments; --dry-run prints instead
  main.py             # orchestration + flag handling
```

## 9. REST reference

```
# Work items linked to a PR (returns IDs only)
GET {base}/{project}/_apis/git/repositories/{repoId}/pullRequests/{prId}/workitems?api-version=6.0

# Work item detail
GET {base}/{project}/_apis/wit/workitems/{id}?$expand=all&api-version=6.0
#   fields: System.Title, System.Description,
#           Microsoft.VSTS.Common.AcceptanceCriteria,
#           System.WorkItemType, System.State, System.Tags   (HTML — strip)

# Diff
GET .../pullRequests/{prId}/iterations?api-version=6.0
GET .../pullRequests/{prId}/iterations/{iterationId}/changes

# Post comment thread (threadContext for inline anchoring)
POST .../pullRequests/{prId}/threads?api-version=6.0
```

Auth: PAT as Basic auth, or `$(System.AccessToken)` when running as a pipeline step (requires "Allow scripts to access the OAuth token" on the job).

## 10. Acceptance criteria

The build is done when:
1. Live mode fetches a real PR's diff, linked story, and checklist, and produces the JSON contract.
2. A false coverage claim (description says "Covered: X", diff doesn't implement X) is flagged.
3. A `not_addressed` criterion appears in output **without** being treated as an error.
4. `--dry-run` prints the feedback; without it, a summary thread is posted to the PR.
5. Swapping `LLM_PROVIDER` between two providers changes only `llm_adapter.py`.

## 11. Constraints / non-goals

- **No install on the TFS server.** Runs on the existing build agent (production) or a networked machine (manual runs).
- **No secrets in code, prompts, or logs.**
- **On-prem network split:** agent reaches TFS *inside* the network but the LLM *outside* it. If outbound is blocked, whitelist the LLM endpoint on the build agent's egress or use an internal model (`LLM_PROVIDER=ollama`).
- **TLS:** internal TFS may use a private CA — trust the org CA bundle rather than disabling verification.
- **Context window:** story + checklist + large diff may exceed the model limit — chunk the diff and send story + checklist with each chunk.
- **Non-goal:** runtime/behavioral verification, and blocking merges by default.
