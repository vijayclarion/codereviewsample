# Claude Code Build Spec — AI Code Review Agent for On-Prem TFS

Hand this file to Claude Code. It is a complete build brief: read it top to bottom, then
build the agent described in section 4 onward. All runtime configuration comes from a
`.env` file (manual runs) or pipeline build variables (validation builds) — never
hard-code any value listed in section 2.

---

## 0. What you are building

A command-line **AI code review agent** that reviews a TFS / Azure DevOps **Server**
(on-prem) pull request against (a) its linked user story and (b) a shared code-review
checklist, then posts structured feedback back to the PR.

For this demo, the **agent and the .NET code under review live in the same repo**
(`eShop`). The agent is triggered automatically when a pull request is opened from a
`feature/*` branch into `master`, runs as a pipeline step, and posts its feedback back
onto that PR. (For a wider rollout the agent can later be split into its own central repo
referenced by many pipelines — the code does not change, only where it lives.)

It runs **live** against real TFS via REST. In a validation build it authenticates with
the pipeline's own OAuth token; for manual runs it uses a PAT. A dry-run mode prints
feedback instead of posting. It is a **reviewer aid, not a merge gate by default** — it
reasons over diff text and cannot verify runtime behavior.

Language: **Python 3.11+**. Keep dependencies minimal (`requests`, `python-dotenv`; the
`openai`/`anthropic` SDKs only if you prefer them over raw `requests`).

---

## 1. Repositories involved

- **eShop repo (everything for the demo):**
  `https://devops.najm.sa/tfs/Najm/Najm-Architecture-Unit/_git/eShop`
  Contains the sample .NET API (the code under review) **and** the agent itself under
  `review_agent/`, plus `azure-pipelines.yml`. This is the single repo you push and demo
  from.
- **Shared checklist:**
  `https://devops.najm.sa/tfs/Najm/Najm-Architecture-Unit/_git/TestMarkDown` →
  `code-review-checklist.md`

The eShop repo already includes a ready-made demo (sample .NET API, user story #101, and
a PR description containing a deliberate false coverage claim). Use it as the first
target — see section 9.

### Trigger (how the review starts)

On TFS/Azure DevOps **Server**, a PR does **not** trigger a pipeline via a `pr:` block in
YAML. The pipeline (`trigger: none`) is wired to run as a **branch policy** instead:

> **Repos → Branches → `master` → Branch policies → Build Validation → add the eShop
> pipeline.**

When a PR is opened from `feature/*` into `master`, that policy queues the pipeline, the
agent runs against the PR, and posts back. The PR context (id, repo, collection, project)
is supplied automatically by the validation build's variables — nothing hard-coded.

---

## 2. Configuration — read everything from `.env`

Load `.env` at startup with `python-dotenv`. **All of the following come from `.env`;
none may be hard-coded, printed, or logged.** A `.env.example` ships alongside this spec.

| Variable | Meaning |
|---|---|
| `TFS_BASE_URL` | Collection URL, e.g. `https://devops.najm.sa/tfs/Najm` |
| `TFS_PROJECT` | `Najm-Architecture-Unit` |
| `TFS_PAT` | Personal access token (Basic auth) |
| `TFS_REPO` | Repository name or id, e.g. `eShop` |
| `TFS_PR_ID` | Pull request number to review |
| `TFS_API_VERSION` | REST api-version, default `6.0` |
| `CHECKLIST_REPO_URL` | Git URL of the checklist repo |
| `CHECKLIST_PATH` | Folder within the repo to read checklists from, default `/` (root). All matching files under it are combined. |
| `CHECKLIST_FILE_PATTERN` | Glob for which files count as checklists, default `*.md` |
| `CHECKLIST_BRANCH` | Branch to read the checklists from, default `main` |
| `LLM_PROVIDER` | `claude` \| `openai` \| `azure` \| `ollama` |
| `LLM_MODEL` | Model name |
| `LLM_API_KEY` | LLM key (omit for a keyless internal model) |
| `LLM_BASE_URL` | Optional; for azure/ollama/self-hosted/gateway |
| `DRY_RUN` | `true` → print feedback, don't post |
| `FAIL_ON_CONTRADICTION` | `true` → non-zero exit on contradiction/high-severity |

**Startup validation:** fail fast with a clear message listing any missing required
variable. Always required: `TFS_BASE_URL`, `TFS_PROJECT`, `TFS_REPO`, `TFS_PR_ID`,
`LLM_PROVIDER`, `LLM_MODEL`. For auth, exactly one of `SYSTEM_ACCESSTOKEN` (pipeline) or
`TFS_PAT` (manual) must be present. `LLM_API_KEY` is required unless
`LLM_PROVIDER=ollama`. In a validation build, the TFS_* values may come from pipeline
build variables rather than `.env` (see section 5).

---

## 3. Repo layout & module structure

For the demo, the agent sits **inside the eShop repo** next to the .NET solution and the
pipeline that triggers it:

```
eShop/                        (the one repo you push to TFS)
  azure-pipelines.yml         # trigger: none; wired via master branch policy
  src/eShop.Api/...           # the .NET code under review
  demo/                       # user story #101, PR description, checklist copy
  review_agent/               # the agent (build this)
    main.py             # entrypoint: load config, orchestrate, set exit code
    config.py           # read + validate .env AND pipeline build vars
    tfs_client.py       # all TFS REST calls (the only module that talks to TFS)
    checklist.py        # fetch ALL checklist files from the TestMarkDown repo, combine them
    prompt_builder.py   # assemble story + checklist + diff + scope into the prompt
    llm_adapter.py      # provider-agnostic call_llm(prompt) -> dict
    parser.py           # validate + parse the JSON contract
    output_layer.py     # route summary + inline comments; honor DRY_RUN
    requirements.txt
    .env.example
    README.md
```

`config.py` reads from `.env` for manual runs, and falls back to pipeline build variables
when running as a validation build (section 5). The agent reviews whatever PR it is
pointed at, so keeping it in eShop for the demo does not tie it to eShop — only the
trigger does.

Two boundaries must stay clean so each is replaceable in isolation:
- **`tfs_client.py`** is the only place TFS REST is called.
- **`llm_adapter.py`** is the only place a provider's wire format appears.

---

## 4. Processing pipeline (what `main.py` orchestrates)

1. **Load & validate config** from `.env` or pipeline build variables (section 2).
2. **Fetch linked work item(s)** for the PR. If none, skip coverage and do a
   checklist-only review, and include a note that no story was linked.
3. **Fetch work-item detail** (title, description, acceptance criteria). These fields
   come back as **HTML — strip tags to plain text** before use.
4. **Fetch the diff** for the PR's latest iteration.
5. **Fetch the PR description** (for the scope section).
6. **Fetch the checklists** — enumerate the checklist repo under `CHECKLIST_PATH` and
   read every file matching `CHECKLIST_FILE_PATTERN`. Combine them into one checklist,
   each section labelled with its source filename so violations can cite which checklist
   they came from. Adding a new file to the repo must require no config change.
7. **Build the prompt** (section 6).
8. **Call the LLM** through the adapter (section 7).
9. **Parse & validate** the JSON contract (section 8). On malformed output, retry once
   with a stricter instruction; if still bad, report and exit non-zero.
10. **Emit feedback** (section 8): post to the PR, or print if `DRY_RUN=true`.
11. **Exit code:** `0` normally; if `FAIL_ON_CONTRADICTION=true` and there is any
    `possible_contradiction` or high-severity `code_issue`, exit `1`.

---

## 5. TFS REST reference (use `TFS_API_VERSION`)

Auth — support **both** modes, preferring the pipeline token when present:
- If `SYSTEM_ACCESSTOKEN` is set (pipeline/validation build), use it as a Bearer token:
  `Authorization: Bearer {SYSTEM_ACCESSTOKEN}`.
- Otherwise use the PAT via HTTP Basic — `Authorization: Basic base64(":" + TFS_PAT)`.

In a validation build the PR context is supplied by build variables, so `config.py`
should also accept `TFS_BASE_URL`/`TFS_PROJECT`/`TFS_REPO`/`TFS_PR_ID` sourced from the
pipeline (`System.TeamFoundationCollectionUri`, `System.TeamProject`,
`Build.Repository.Name`, `System.PullRequest.PullRequestId`). When `SYSTEM_ACCESSTOKEN`
is present, `TFS_PAT` is not required.

```
# Work items linked to a PR (returns ids only)
GET {TFS_BASE_URL}/{TFS_PROJECT}/_apis/git/repositories/{repo}/pullRequests/{prId}/workitems?api-version={ver}

# Work item detail (HTML fields — strip)
GET {TFS_BASE_URL}/{TFS_PROJECT}/_apis/wit/workitems/{id}?$expand=all&api-version={ver}
#   System.Title, System.Description,
#   Microsoft.VSTS.Common.AcceptanceCriteria,
#   System.WorkItemType, System.State, System.Tags

# PR detail (description)
GET {TFS_BASE_URL}/{TFS_PROJECT}/_apis/git/repositories/{repo}/pullRequests/{prId}?api-version={ver}

# Diff
GET .../pullRequests/{prId}/iterations?api-version={ver}
GET .../pullRequests/{prId}/iterations/{iterationId}/changes?api-version={ver}

# Post a comment thread (threadContext anchors inline comments to file+line)
POST .../pullRequests/{prId}/threads?api-version={ver}

# Checklist files — list the folder, then read each matching file.
# Option A: recurse the folder and get content in one call, then filter by pattern:
GET {TFS_BASE_URL}/{TFS_PROJECT}/_apis/git/repositories/{checklistRepo}/items?scopePath={CHECKLIST_PATH}&recursionLevel=Full&versionDescriptor.version={CHECKLIST_BRANCH}&includeContent=true&api-version={ver}
# Option B: list first, then fetch each file's content by its path:
GET .../items?scopePath={CHECKLIST_PATH}&recursionLevel=Full&api-version={ver}       # enumerate
GET .../items?path={eachFilePath}&includeContent=true&versionDescriptor.version={CHECKLIST_BRANCH}&api-version={ver}
# Keep only blobs (not folders) whose name matches CHECKLIST_FILE_PATTERN.
```

Handle gracefully: empty linked-workitems list, HTML entities in fetched fields, a diff
too large for one LLM call (chunk it and send story + checklist with each chunk), and
TLS trust for a private CA (respect the system trust store; never disable verification).

---

## 5a. Pipeline & trigger (`azure-pipelines.yml`, in the eShop repo)

The repo ships an `azure-pipelines.yml` that runs the agent as a validation build:

- `trigger: none` — no CI-on-push. The pipeline runs only as a PR validation build.
- Wired via **master branch policy** (Repos → Branches → `master` → Branch policies →
  Build Validation → this pipeline). This is what fires on a `feature/*` → `master` PR.
- Runs on the existing self-hosted agent pool.
- Installs `review_agent/requirements.txt`, then runs `python review_agent/main.py`.
- Passes PR context and auth through `env:`:
  `TFS_BASE_URL=$(System.TeamFoundationCollectionUri)`,
  `TFS_PROJECT=$(System.TeamProject)`, `TFS_REPO=$(Build.Repository.Name)`,
  `TFS_PR_ID=$(System.PullRequest.PullRequestId)`,
  `SYSTEM_ACCESSTOKEN=$(System.AccessToken)`; checklist and LLM values from a variable
  group / secret variables; `DRY_RUN=false`, `FAIL_ON_CONTRADICTION=false`.

Two one-time settings on the pipeline/repo for comment posting to work: enable **"Allow
scripts to access the OAuth token"** on the job, and grant the build service identity
**Contribute to pull requests** on the eShop repo.

---

## 6. Prompt (built in `prompt_builder.py`)

System role: a senior code reviewer. Provide the story (title, description, acceptance
criteria), the **combined checklist markdown** (all files from the checklist repo, each
section labelled with its source filename), the PR description, and the diff. Include
these rules verbatim in substance:

```
A pull request often implements only PART of a user story.
Do NOT treat an unaddressed acceptance criterion as a defect.
For each acceptance criterion, classify as exactly one:
  - "appears_addressed"      the diff plausibly implements it
  - "not_addressed"          no related changes (NEUTRAL — never an error)
  - "possible_contradiction" the diff conflicts with it (the only loud flag)

The PR description has a "Covered / Not in this PR" section. Treat the "Covered"
list as the author's authoritative scope. BUT still verify each covered item
against the diff: if the description claims an item is covered and the diff does
not implement it, report it as a false coverage claim.

Check the diff against each checklist item. Report only real violations.

Respond with ONLY the JSON object described below — no prose, no markdown fences.
```

---

## 7. LLM adapter (`llm_adapter.py`)

One function: `call_llm(prompt: str) -> dict`. It selects behavior on `LLM_PROVIDER`,
sends the request (honoring `LLM_BASE_URL` when set), and **normalizes** the reply to the
raw model text, then JSON-parses it. Provider differences live only here:

- `claude` → Anthropic Messages API; text at `content[].text`.
- `openai` / `azure` → Chat Completions; text at `choices[].message.content`.
- `ollama` → local endpoint at `LLM_BASE_URL`; keyless.

Swapping providers must require **no change outside this module**.

---

## 8. Output contract & routing

The model must return exactly:

```json
{
  "coverage": [
    {"criterion": "...", "status": "appears_addressed|not_addressed|possible_contradiction", "evidence": "..."}
  ],
  "checklist_violations": [{"item": "...", "source": "<checklist filename>", "issue": "...", "file": "...", "line": 0}],
  "code_issues": [{"severity": "high|medium|low", "issue": "...", "file": "...", "line": 0}],
  "summary": "..."
}
```

`parser.py` validates the shape and the `status`/`severity` enums. Routing in
`output_layer.py`:

- **Summary thread** — one PR thread with `summary` and the full coverage map (including
  a false-coverage-claim callout when detected).
- **Inline comments** — one per `possible_contradiction`, `checklist_violation`, and
  `code_issue` that carries a `file` + `line`, anchored with `threadContext`.
- **`not_addressed`** rows are informational — never a comment or a failure.
- If `DRY_RUN=true`, print all of the above to stdout instead of calling the POST.

---

## 9. First target — the eShop demo (acceptance test)

The eShop repo contains the sample .NET API and, under `demo/`, the story and PR
description for this scenario. The demo flow is a **`feature/*` → `master` pull request**:

1. Push the eShop repo (code + `review_agent/` + `azure-pipelines.yml`) to TFS.
2. Create work item **#101** from `demo/user-story-101.md`.
3. Create a `feature/add-to-cart` branch, commit the cart changes, and open a PR into
   `master` whose description is `demo/pr-description.md`, linked to #101.
4. With the **master branch policy** pointing at the pipeline, opening that PR triggers
   the agent automatically. (For a first check you can also run it manually from a
   machine with `.env` set and `DRY_RUN=true`.)

**User Story #101 "Add items to shopping cart"** has five acceptance criteria.
`CartController.AddItem` implements quantity validation, not-found handling, and quantity
increment — but **does not enforce the stock limit** (criterion 4). The PR description
**claims stock checking is covered**.

A correct run must:

1. Mark criteria 1, 2, 3, 5 as `appears_addressed`.
2. **Flag criterion 4 as a false coverage claim** (description says covered; diff has no
   stock check). This is the headline result.
3. Raise checklist item "business rules from the story are enforced" as a violation.
4. Leave scoped-out follow-ups (#102, #103) unflagged.
5. Post one summary thread + the inline flag on `CartController.AddItem` onto the PR — or
   print them when run manually with `DRY_RUN=true`.

---

## 10. Build order (suggested)

1. `config.py` + config loading (`.env` and pipeline build vars) + `--help`/`--dry-run`
   handling in `main.py`.
2. `tfs_client.py`: auth (Bearer `SYSTEM_ACCESSTOKEN` or Basic PAT), then fetch PR, work
   items, diff, PR description. Print them.
3. `checklist.py`: fetch and plain-text the checklist from the TestMarkDown repo.
4. `prompt_builder.py`, then `llm_adapter.py` for the configured provider.
5. `parser.py` with strict validation + one retry.
6. `output_layer.py`: dry-run printing first, then real thread posting.
7. Run manually against the eShop PR with `DRY_RUN=true` and confirm the five
   expectations in section 9. Then push, wire the master branch policy, open the
   `feature/add-to-cart` PR, and confirm the same feedback posts automatically.

## 11. Non-negotiables

- Read all config from `.env` (manual) or pipeline build variables (validation build);
  keep secrets out of code, logs, prompts, and the console.
- Keep `.env` in `.gitignore`; ship only `.env.example`. In the pipeline, keep the LLM
  key in a secret variable / variable group, never in the YAML.
- Don't disable TLS verification; respect the system/private-CA trust store.
- `not_addressed` is never an error. Only `possible_contradiction`, real `code_issues`,
  and false coverage claims are problems.
- Default to non-blocking (`DRY_RUN=true`, `FAIL_ON_CONTRADICTION=false`); a human
  decides on the merge.
