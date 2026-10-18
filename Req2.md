Build an AI code review agent that runs locally and reviews a Team Foundation
Server (TFS / Azure DevOps Server, on-prem) pull request against a set of code
review checklist documents, then posts its feedback as comments on that pull
request.

## Environment and repositories

The agent works against a specific on-prem TFS instance:

- TFS collection URL: `https://devops.najm.sa/tfs/Najm`
- TFS project: `Najm-Architecture-Unit`
- Sample code repository (the code being reviewed): `CodeReviewSample`
- Agent repository (the review logic itself): `CodeReviewAgent`
- Checklist repository (the checklist documents): `CodeReviewChecklist`
- LLM model for the default backend: `claude-sonnet-4-6`

Credentials are supplied through configuration as placeholders, never hard-coded:
a TFS personal access token for cloning repositories and reading pull request and
work item data, a separate TFS personal access token for reading the pull request
and posting comments on it, and an LLM API key for the configured backend.

## What to build

Start with a local, manual mode: a developer makes code changes in the sample
code repository, opens a pull request, and then runs the agent by hand passing
the pull request number. When run, the agent clones the code review agent
repository and the code review checklist repository from TFS, reads every
checklist document, reviews the pull request's code changes against those
checklists, and adds the necessary review comments to the pull request.

The code being reviewed lives in a sample repository. The review logic lives in
its own agent repository, which is cloned at run time so the review always uses
the latest committed version. The checklists live in a separate checklist
repository and are provided as multiple Excel (.xlsx) and Word (.docx) documents,
so the agent must read rules from several files across both formats and combine
them into one checklist. Each review finding should cite which checklist item and
which source document it came from.

The agent must be able to work with different large language model backends —
Claude and Copilot — chosen by configuration, without changing the review logic.
The agent repository itself should be inspected and, where needed, adjusted so it
is genuinely LLM-agnostic: all model calls go through a single backend interface,
the provider is selected by configuration, and adding a new provider does not
require changing the review logic, the checklist reading, the prompt, the result
parsing, or the comment posting.

For safety, the agent must support a dry-run mode that prints its findings to the
console and posts nothing to the pull request, so a developer can preview the
review before it comments on a real pull request.

The agent connects to TFS at a configured collection URL and project, using
personal access tokens supplied through configuration: one token for cloning
repositories and reading pull request and work item data, and a separate token
for reading the pull request and posting comments on it. It should also, when
available, read the pull request's linked user story or work item and use it as
additional review context. No credentials or secrets may appear in code, logs, or
committed files.

Finally, this local manual mode must be tested end to end: make a code change that
breaks at least one checklist rule, open a pull request, run the agent in dry-run
to preview the findings, then run it for real and confirm the correct,
checklist-cited comments appear on the pull request — and confirm that running
against a non-existent pull request fails cleanly without posting anything.

Out of scope for this iteration: automated pipeline or branch-policy triggering,
blocking or gating merges, and any runtime or behavioral testing of the code
under review. Those come later; this iteration is local and manual only.