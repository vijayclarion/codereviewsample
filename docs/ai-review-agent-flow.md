# Flow: AI Code Review Agent

End-to-end flow from a developer opening a pull request to feedback appearing on it.

## Trigger

A **branch policy (build validation)** on the target branch runs the pipeline when a PR is opened or updated. This is a policy on the target branch, not a plain CI trigger on push — the agent must run in the PR context so it can see the PR, its linked work item, and its description.

## Stages

**1. PR opened / updated**
Developer opens a PR into a policy-protected branch (e.g. `main`, `develop`). The branch policy queues the validation pipeline and passes the PR ID into the run.

**2. Pipeline runs the agent**
The pipeline step executes `main.py` on the existing self-hosted build agent. `$(System.AccessToken)` (or the PAT) authenticates the agent to TFS. Nothing is installed on the TFS server.

**3. Gather the three inputs**
- **Diff** — fetched from the PR's latest iteration changes (or `git diff` of source vs target).
- **User story** — the work item linked to the PR is fetched via REST; description and acceptance criteria are HTML-stripped.
- **Checklist** — the shared `code-review-checklist.md` is fetched from the central checklist repo.
- **PR description** — fetched to read the "Covered / Not in this PR" scope section.

If no work item is linked, the agent skips coverage and does a checklist-only review, noting this on the PR.

**4. Build the review prompt**
`prompt_builder.py` combines the four inputs into one prompt, with the instructions that (a) a PR usually implements only part of a story, (b) `not_addressed` is neutral, (c) the PR description's scope section is authoritative but implementation must still be verified against the diff, and (d) the response must match the JSON contract exactly.

**5. Call the LLM through the adapter**
`llm_adapter.py` sends the prompt to whichever provider `LLM_PROVIDER` selects and normalizes the response so the rest of the pipeline is provider-agnostic.

**6. Parse the structured JSON**
`parser.py` validates the response against the fixed schema (`coverage`, `checklist_violations`, `code_issues`, `summary`). A malformed response is retried or reported, never silently dropped.

**7. Post feedback to the PR**
`output_layer.py` posts:
- one **summary thread** with the overall summary and the full coverage map;
- **inline comments** for `possible_contradiction`, checklist violations, and code issues that carry a file and line;
- optionally sets a pass/fail status (off by default; fails only on high-severity issues or a contradiction).

With `--dry-run`, stages 1–6 run unchanged and stage 7 prints the feedback instead of posting.

## Flow at a glance

```
PR opened ──► branch policy ──► pipeline step (build agent)
                                      │
                                      ▼
                     gather: diff + story + checklist + PR description
                                      │
                                      ▼
                          build prompt (coverage rules)
                                      │
                                      ▼
                        LLM adapter (claude/openai/…)
                                      │
                                      ▼
                          parse JSON contract
                                      │
                                      ▼
              post to PR: summary thread + inline comments
                        (or print, if --dry-run)
```

## Two load-bearing principles

1. **Three-input gather** (diff + story + checklist) is what lets the agent judge code against *intent*, not just style.
2. **Adapter + fixed JSON** is what makes the LLM swappable without touching any other stage.
