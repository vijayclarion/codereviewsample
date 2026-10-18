# PR Criteria & Prerequisites

Two parts: what a pull request must contain for the agent to review it well, and what must exist in the environment before the agent can run.

---

## Part A — PR criteria

What each pull request should satisfy so the agent produces useful, low-noise feedback.

### Required

**1. A linked work item (user story).**
The PR must be linked to its user story so the agent can check code against intent. Linking happens by manually adding it in the PR's Work Items tab, by putting `#123` in a commit message, or by creating the branch from the work item. Without a link, the agent falls back to a checklist-only review and notes that coverage was not checked. Recommended: a branch policy that requires at least one linked work item.

**2. A description with a scope section.**
The PR description must state what this PR does and does not cover. This is the one thing the agent cannot infer from the diff. Use a template so it's consistent:

```
Story: #123

Covered in this PR:
- Reset password via email endpoint
- Email delivery of reset link

Not in this PR (follow-up):
- Link expiry (#124)
- Password reuse rule (#125)
```

The agent treats the "Covered" list as authoritative scope, but still verifies each claimed item against the diff — a "Covered: X" with no matching code is flagged as a false coverage claim.

### Recommended

**3. Reasonable PR size.**
Very large diffs may exceed the model's context window and dilute review quality. Encourage smaller, story-sliced PRs; the agent chunks large diffs but smaller is better.

**4. A well-formed user story.**
Coverage mapping is only as good as the story's acceptance criteria. Stories with clear, itemized acceptance criteria produce a clean coverage map; vague stories produce vague coverage.

### Enforcement

- `pull_request_template.md` in each repo pre-fills the scope headings.
- A branch policy requires a linked work item.
- The agent gives a soft nudge (a comment, not a block) if the scope section is missing, so authors learn the convention without being gated.

---

## Part B — Prerequisites

What must be in place before the agent can run against TFS.

### TFS side

**1. An existing self-hosted build agent.**
Already present if pipelines run today. The review agent runs as a step on it; nothing is installed on the TFS server.

**2. Branch policy (build validation) on target branches.**
A policy on each protected branch that runs the validation pipeline on PRs. This is the trigger.

**3. "Allow scripts to access the OAuth token" enabled** on the pipeline job, if using `$(System.AccessToken)` instead of a PAT to post comments.

**4. A shared checklist repo.**
A central Git repo holding `code-review-checklist.md`, readable by the agent. Keep it to 10–20 high-value items — long checklists produce noisy reviews.

**5. Confirmed TFS version and REST API availability.**
The deployed TFS version determines which REST endpoints and `api-version` values work, and whether YAML pipelines exist. Confirm before building. (Still an open item.)

### Credentials

**6. A Personal Access Token (PAT)** for manual runs (pipeline runs can use `$(System.AccessToken)`).
Create via profile icon → Security → Personal Access Tokens → New Token.
Minimum scopes:
- **Code (Read)**
- **Code (Read & Write)** or **Pull Request Threads (Read & Write)** — to post comments
- **Work Items (Read)** — to fetch the story

Use a short expiry (7–30 days), do not grant Full access, and revoke when done.

**7. An LLM API key** (separate from TFS) for the chosen provider — unless using an internal model.

### Network & security

**8. Network reach to TFS.**
`devops.najm.sa` is internal — the machine running the agent must be on the Najm network/VPN. Quick test: can you open the TFS URL in a browser from that machine?

**9. Outbound reach to the LLM** (the on-prem split).
The build agent must reach TFS *inside* the network and the LLM *outside* it. If outbound is blocked, whitelist the LLM endpoint on the agent's egress, or host an internal model (`LLM_PROVIDER=ollama`) on a separate GPU box — never on the TFS server.

**10. TLS / certificate trust.**
Internal TFS often uses a private CA. Trust the organization's CA bundle on the agent machine rather than disabling certificate verification.

### Configuration values to have ready

**11. Concrete identifiers:**
- Repository name or ID
- An existing PR ID with a linked work item (for the first live run)
- The shared checklist repo URL

### Prerequisite checklist

```
[ ] Self-hosted build agent exists and runs pipelines
[ ] Branch policy (build validation) on target branch
[ ] OAuth token access enabled on the job (if using System.AccessToken)
[ ] Shared checklist repo with code-review-checklist.md (10–20 items)
[ ] TFS version + api-version confirmed
[ ] PAT created with Code R/W + Work Items Read, short expiry
[ ] LLM API key (or internal model reachable)
[ ] Agent machine on Najm network / VPN — TFS URL opens in browser
[ ] Outbound path to LLM open, or internal model hosted
[ ] Private CA bundle trusted on the agent machine
[ ] Repo name/ID, sample PR ID, checklist repo URL on hand
```
