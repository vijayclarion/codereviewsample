# Feature Specification: Local Manual Pull Request Review Run

**Feature Branch**: `002-local-manual-pr-review`  
**Created**: 2026-09-15  
**Status**: Draft  
**Input**: User description: "Req2.md — local, manual mode for the AI code review agent on on-prem TFS"

## Overview

The review agent currently has no supported way for a developer to simply *use* it. This feature
delivers the first end-to-end, hands-on path: a developer changes code in the sample repository,
opens a pull request on the on-prem TFS collection, and runs the agent by hand with that pull
request number. The agent fetches the review logic and the checklist documents from their own
repositories, reads every checklist document, reviews the changed code against the combined
checklist, and leaves comments on the pull request — each finding naming the checklist item and the
document it came from.

Three properties make this iteration more than a script. The **checklists are documents, not code** —
multiple Excel and Word files maintained by quality leads in a separate repository, combined into one
checklist at run time. The **review logic is fetched, not bundled** — it is obtained from its own
repository on each run, so a review always reflects the latest committed rules. And the **model is a
configuration choice, not a dependency** — Claude and Copilot are selected by configuration behind a
single backend boundary, and the agent repository is audited and adjusted where that boundary leaks.

A **dry-run mode** is the safety rail throughout: it produces the complete review for the console and
posts nothing, so a developer always sees what the agent would say before it says it on a real pull
request.

This iteration is **local and manual only**. Nothing here triggers from a pipeline, gates a merge, or
runs the code under review.

## Clarifications

### Session 2026-09-15

- Q: Which copy of the review logic actually performs the review — the copy obtained from the agent repository at run time, or the developer's local copy? → A: The obtained copy executes by default, with a documented override that runs the developer's local working copy for agent development; every run states which copy it used
- Q: How is a rule recognised inside a checklist document? → A: Spreadsheets require named columns (at minimum an item identifier and the rule text) and are skipped as unreadable without them; word-processing documents are read tolerantly, with headings, list items, and table rows all treated as candidate rules
  - *Amended during planning (2026-09-15)*: the identifier column is **not** required. Inspecting the three real spreadsheets found one (`Non Functional Requirements Checklist v2.0.xlsx`) whose `#` column is present but empty in every data row; requiring it would reject a valid in-use checklist and fail SC-006. Only the rule-text column is required; identifiers are derived when absent, as they already are for Word documents. See research.md R4.
- Q: What does the agent leave on the pull request after a real run? → A: One inline comment per located finding, plus one summary comment on every run — including a clean one — carrying the outcome, any unlocatable findings, and what was skipped or not reviewed
- Q: Are any changed files held back from the model backend? → A: Yes — a built-in default exclusion list covering secret-bearing and machine-generated paths, replaceable or extendable through configuration, with every excluded file and its reason named in the report
- Q: How much change does one run review? → A: Up to a configurable bound, prioritising the files most relevant to the checklists and the linked story, with every unreviewed file named in the report

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Preview a checklist review of a real pull request (Priority: P1)

A developer has opened a pull request in the sample code repository. From their own machine, they run
the agent once, passing the pull request number, and ask it not to post anything. The agent gathers
everything it needs — the current review logic, the current checklist documents, the pull request's
changed code — reviews the change, and prints the findings it *would* post, each naming the checklist
item and source document behind it.

**Why this priority**: This is the smallest slice that proves the whole chain works — acquisition,
checklist reading, review, and finding formatting — and it is the slice a developer can run against a
live pull request with no risk of leaving comments on it. It delivers value on its own: a developer
can read the printed review and act on it before anyone else sees the pull request.

**Independent Test**: Open a pull request in the sample repository containing a deliberate checklist
violation, run the agent in dry-run mode with that pull request number, and confirm the violation
appears in the printed output with its checklist item and source document — and that the pull request
itself has gained no comments.

**Acceptance Scenarios**:

1. **Given** a pull request that exists in the sample code repository, **When** the developer runs the
   agent in dry-run mode with that pull request number, **Then** the complete review is printed to the
   console and no comment, discussion, or other change appears on the pull request.
2. **Given** a change that breaks at least one checklist rule, **When** the review runs in dry-run
   mode, **Then** at least one finding for that rule is printed, naming the checklist item and the
   document it came from.
3. **Given** a run in dry-run mode, **When** the output is inspected, **Then** each finding identifies
   the file, and where determinable the line, it applies to.
4. **Given** a pull request number that does not exist in the target repository, **When** the agent
   runs in dry-run mode, **Then** the run stops with a message naming the pull request it could not
   find, and reports failure.
5. **Given** a run in dry-run mode, **When** the console output and any log file are inspected,
   **Then** no personal access token or model key appears in either.

---

### User Story 2 - Post the review onto the pull request (Priority: P2)

Having previewed the findings, the developer runs the agent again without the dry-run switch. The same
findings are now posted as comments on the pull request, where reviewers already are, each still
citing its checklist item and source document.

**Why this priority**: Posting is the point of the feature, but it is only safe and useful once the
review it posts has been seen and trusted — Story 1 is what makes this story shippable rather than
alarming.

**Independent Test**: Take the pull request previewed in Story 1, run the agent without dry-run, and
confirm the same findings now appear as comments on the pull request with their checklist citations
intact.

**Acceptance Scenarios**:

1. **Given** a review that produced findings, **When** the agent runs without dry-run, **Then** each
   finding that identifies a file and line appears as an inline comment there, and one summary comment
   appears carrying the run's outcome, any finding with no location, and anything skipped or not
   reviewed.
2. **Given** the same pull request reviewed in dry-run immediately beforehand, **When** the agent runs
   for real, **Then** the posted comments carry the same findings and the same checklist and document
   citations as the preview.
3. **Given** a posting credential that lacks permission to comment on the pull request, **When** the
   agent runs, **Then** the run reports failure naming the missing permission, and still shows the
   developer the findings it could not post.
4. **Given** a pull request number that does not exist, **When** the agent runs without dry-run,
   **Then** the run fails cleanly and no comment is posted anywhere.
5. **Given** a review that found no violations, **When** the agent runs without dry-run, **Then** it
   posts no inline comments and invents no findings, and the summary comment still appears stating that
   the review ran and found nothing.

---

### User Story 3 - Checklists maintained as Excel and Word documents (Priority: P3)

A quality lead keeps the team's review rules as a set of spreadsheets and documents in the checklist
repository — some rules in Excel, some in Word. They add a rule, or a whole new document, and the next
review applies it without anyone touching configuration or code. When a rule is broken, the developer
reading the comment can tell exactly which rule, in which document, was broken.

**Why this priority**: This is what makes the checklist the team's own artefact rather than something
embedded in the agent, and it is the source of the traceability every finding depends on. It is,
however, only observable once Stories 1 and 2 give it somewhere to appear.

**Independent Test**: Place several checklist documents of both formats in the checklist repository,
run a dry-run review, and confirm rules from every document are in force and every finding names its
source document. Then add one more document and re-run with no configuration change; its rules take
effect.

**Acceptance Scenarios**:

1. **Given** a checklist repository holding several Excel and several Word documents, **When** the
   review runs, **Then** rules from all of them are read and combined into a single checklist used for
   the review.
2. **Given** a new checklist document added to that repository, **When** the next review runs, **Then**
   its rules are applied with no configuration change and no code change.
3. **Given** a reported violation, **When** the developer reads the finding, **Then** it names both the
   checklist item and the document file it came from.
4. **Given** a document that cannot be read (corrupt, password-protected, or an unsupported format),
   **When** the review runs, **Then** the review continues with the documents it could read and the
   report names each document it skipped and why.
5. **Given** a checklist repository with no readable checklist documents at all, **When** the review
   runs, **Then** the run stops and says so rather than silently reviewing against an empty checklist.

---

### User Story 4 - Change the model backend by configuration alone (Priority: P4)

A platform engineer needs the review to run against a different large language model — Claude today,
Copilot for a team whose data policy requires it — and needs that to be a configuration change, not a
code change. They also need confidence that the agent repository genuinely honours that boundary, so
adding a third backend later stays a contained piece of work.

**Why this priority**: Backend independence protects the investment and unblocks teams with different
model policies, but the first working review does not depend on it.

**Independent Test**: Run the same pull request twice, changing only the configured backend, and
confirm both runs produce the same shape of review through the same review logic. Separately, audit
the agent repository and confirm every model call passes through the single backend boundary.

**Acceptance Scenarios**:

1. **Given** the agent configured for one supported backend, **When** the configuration names the other
   supported backend and its model, **Then** the review runs and produces the same shape of findings
   with no change to any reviewing logic.
2. **Given** the agent repository, **When** it is inspected, **Then** every call to a language model
   passes through a single backend interface, and the backend is chosen by configuration.
3. **Given** a hypothetical third backend, **When** the work to add it is assessed, **Then** it requires
   an implementation of the backend interface and a configuration value only — no change to the review
   logic, the checklist reading, the prompt construction, the result parsing, or the comment posting.
4. **Given** a configured backend whose credential is missing or invalid, **When** the agent runs,
   **Then** it stops with a message naming the missing or rejected configuration value, before doing
   any review work.
5. **Given** a configured backend name that is not supported, **When** the agent runs, **Then** it stops
   immediately and names the backends it does support.

---

### User Story 5 - Use the linked user story as review context (Priority: P5)

Where the pull request is linked to a user story or work item, the developer wants the review to take
that intent into account, so its comments speak to what the change was supposed to do and not only to
the rules it broke.

**Why this priority**: It sharpens the review, but a checklist review is already useful without it, and
many pull requests in this environment carry no link.

**Independent Test**: Review one pull request with a linked work item and one without, and confirm the
first uses the story as context while the second completes normally and says no story was linked.

**Acceptance Scenarios**:

1. **Given** a pull request with a linked user story or work item, **When** the review runs, **Then**
   the story's text is used as additional context for the review.
2. **Given** a pull request with no linked work item, **When** the review runs, **Then** the review
   completes as a checklist-only review and the report states that no story was linked.
3. **Given** a linked work item whose fields carry rich-text markup, **When** the review runs, **Then**
   the text is reduced to plain text before use, so the review never reasons over markup noise.
4. **Given** a linked work item that cannot be read, **When** the review runs, **Then** the review
   continues without it and the report says the story could not be read.

---

### Edge Cases

- **Pull request number does not exist, or names a pull request in another repository**: the run stops
  with a message naming the pull request, reports failure, and posts nothing — in dry-run and real runs
  alike.
- **TFS collection unreachable, or a clone or read credential rejected**: the run stops before any
  review work, names the credential or host at fault, and posts nothing.
- **Agent repository cannot be obtained**: the run stops and says the review logic could not be
  obtained, rather than silently falling back to the local working copy — that fallback happens only
  when the developer asks for it explicitly with the override.
- **Checklist repository reachable but empty, or holding no readable checklist document**: the run stops
  and says so — a review against an empty checklist would read as a clean review.
- **A single checklist document is corrupt, password-protected, or in an unsupported format**: the
  review proceeds with the rest, and the skipped document and the reason are named in the report.
- **A spreadsheet carries no identifiable item-identifier and rule-text columns**: it is skipped as
  unreadable and named in the report with that reason, rather than having a layout guessed for it.
- **A checklist document contains no recognisable rules**: it contributes nothing and is named in the
  report as contributing nothing, so it is never mistaken for rules that were silently applied.
- **The same checklist rule appears in more than one document**: it is reviewed once and the finding
  cites every document that carries it, rather than producing duplicate comments.
- **Pull request with no changed files, or only binary or non-text files**: the report says there was
  nothing to review rather than inventing findings, and nothing is posted beyond that statement.
- **Change set exceeds the configured review bound**: the most checklist- and story-relevant portion is
  reviewed, every unreviewed file is named in the report and the summary comment, and the run completes
  normally rather than failing.
- **Every changed file is excluded** (for example a pull request that only rotates a certificate):
  nothing is sent to the model backend, and the report lists the skipped files so the developer knows
  why the review is silent.
- **The pull request is updated and the agent is run again**: the developer may run it as often as they
  like; the report is regenerated from the current state of the pull request.
- **Model backend unreachable, or its response unusable**: the run reports failure with the cause, and
  posts no partial or invented findings.
- **Posting credential valid for reading but not for commenting**: failure names the missing permission
  and the findings are still shown on the console.
- **Only the clone-and-read token is configured and the run is a dry run**: the run succeeds — a posting
  credential is required only when the agent will actually post.
- **Configuration file missing or incomplete**: the run stops at startup naming every missing value.
- **Private certificate authority on the TFS host**: the host is trusted through the machine's own trust
  store; certificate verification is never disabled.

## Requirements *(mandatory)*

### Functional Requirements

**Invocation and modes**

- **FR-001**: The agent MUST be runnable by hand from a developer's machine against a single pull
  request identified by its number, supplied at run time.
- **FR-002**: The agent MUST provide a dry-run mode that produces the complete review for the console
  and posts nothing to the pull request, and MUST NOT post anything in that mode under any outcome.
- **FR-003**: The agent MUST present, in dry-run mode, exactly the findings and citations it would post
  in a real run, so a preview is a faithful rehearsal.
- **FR-004**: The agent MUST report, at the end of every run, whether it completed, what it reviewed,
  and — where it did not complete — why.

**Configuration and credentials**

- **FR-005**: The agent MUST read every runtime value from external configuration supplied at run time:
  collection URL, project, the three repository names, pull request number, credentials, backend
  selection, and model. No such value may be embedded in the deliverable.
- **FR-006**: The agent MUST accept two distinct personal access tokens — one used to obtain the
  repositories and read pull request and work item data, and a separate one used to read the pull
  request and post comments on it — and MUST use each only for its own purpose.
- **FR-007**: The agent MUST validate configuration before doing any work and, where required values are
  missing or unusable, MUST stop immediately with a message naming every missing or rejected value.
- **FR-008**: The agent MUST require the posting credential only for a run that will post; a dry run
  MUST succeed without it.
- **FR-009**: The agent MUST keep credentials out of console output, log files, the material sent to the
  model backend, the comments posted to the pull request, and version control.
- **FR-010**: The agent MUST distribute only a redacted example configuration, and MUST keep any
  populated configuration file out of version control.
- **FR-011**: The agent MUST verify the identity of the TFS host using the machine's trust store, and
  MUST NOT offer or use an option to skip that verification.

**Obtaining the review logic and the checklists**

- **FR-012**: The agent MUST obtain the review logic from the agent repository at run time and MUST
  execute that obtained copy by default, so that every review reflects the latest committed version of
  that logic rather than a bundled or locally-modified one.
- **FR-012a**: The agent MUST provide a documented override that runs the developer's local working copy
  of the review logic instead, so the agent itself can be developed without committing every trial.
- **FR-012b**: The agent MUST state, in every run's report, which copy of the review logic performed the
  review — the obtained copy and the version it came from, or the local working copy — so a review can
  never be mistaken for one produced by the committed logic when it was not.
- **FR-013**: The agent MUST obtain the checklist documents from the checklist repository at run time, so
  that every review reflects the latest committed checklists.
- **FR-014**: The agent MUST stop, rather than continue with an unknown or stale copy, when either
  repository cannot be obtained, and MUST say which one failed.
- **FR-015**: The agent MUST pick up checklist documents added to the checklist repository with no
  configuration change and no code change.

**Reading the checklists**

- **FR-016**: The agent MUST read checklist rules from both spreadsheet (.xlsx) and word-processing
  (.docx) documents, across as many documents as the checklist repository holds.
- **FR-017**: The agent MUST combine the rules from every readable document into a single checklist used
  for the review, preserving for each rule the document it came from and an identifier for the item
  itself.
- **FR-017a**: The agent MUST read a spreadsheet document as tabular rows under named columns, requiring
  a column carrying the rule text, and MUST treat a sheet that offers no such column as contributing no
  rules rather than guess at its layout. Where a sheet also names an item-identifier column and populates
  it, the agent MUST use those identifiers; where it does not, the agent MUST derive a stable identifier
  from the document, sheet, and row, as it does for word-processing documents.
- **FR-017b**: The agent MUST read a word-processing document tolerantly, treating its headings, list
  items, and table rows as candidate rules, and MUST derive an identifier for each extracted rule where
  the document itself supplies none.
- **FR-018**: The agent MUST continue the review using the documents it could read when an individual
  document is unreadable, and MUST name every skipped document and the reason in its report.
- **FR-019**: The agent MUST stop and say so when no checklist document in the repository yields any
  rule, rather than perform a review against an empty checklist.
- **FR-020**: The agent MUST collapse a rule that appears in more than one document into a single
  checklist item that cites each document carrying it.

**Gathering review material**

- **FR-021**: The agent MUST retrieve the changed content of the named pull request, addressable by file
  and, where determinable, line.
- **FR-021a**: The agent MUST withhold changed files matching a built-in default exclusion list from the
  material it sends to the model backend. The list MUST cover paths that commonly carry secrets
  (environment files, key and certificate files, credential stores) and paths not worth reviewing
  (dependency lock files, build output, generated code).
- **FR-021b**: The agent MUST allow that exclusion list to be replaced or extended through
  configuration, so a team whose checklists genuinely target configuration files can review them.
- **FR-021c**: The agent MUST name every excluded file, and the reason it was excluded, in its report.
  It MUST NOT drop a changed file silently, so an excluded file can never read as a reviewed-and-clean
  one.
- **FR-021d**: The agent MUST bound the amount of change it reviews in one run by a configurable limit,
  so review cost and latency stay predictable on an unusually large pull request.
- **FR-021e**: The agent MUST, when a change set exceeds that limit, review the portion most relevant to
  the combined checklist and the linked story rather than an arbitrary slice, and MUST name every file it
  left unreviewed in its report so no unreviewed file can be mistaken for a clean one.
- **FR-022**: The agent MUST retrieve the pull request's title and description for use as the author's
  declared intent.
- **FR-023**: The agent MUST retrieve the user story or work item linked to the pull request where one
  exists, reduce its rich-text fields to plain text, and use it as additional review context.
- **FR-024**: The agent MUST continue as a checklist-only review, and say so in its report, where no work
  item is linked or a linked work item cannot be read.
- **FR-025**: The agent MUST report that there was nothing to review, rather than fail or invent
  findings, where the pull request changes no reviewable content.

**Reviewing**

- **FR-026**: The agent MUST evaluate the pull request's changed code against every item of the combined
  checklist and report only real violations.
- **FR-027**: The agent MUST accompany every finding with the checklist item it violates and the source
  document that item came from.
- **FR-028**: The agent MUST accompany every finding, where the changed content identifies one, with the
  file and line it applies to.
- **FR-029**: The agent MUST NOT report speculative, stylistic, or filler findings where the change
  violates no checklist item.

**Model backend independence**

- **FR-030**: The agent MUST route every language-model call through a single backend interface.
- **FR-031**: The agent MUST select the backend by configuration, and MUST support both a Claude backend
  and a Copilot backend through that one interface.
- **FR-032**: The agent MUST make adding a further backend require an implementation of the backend
  interface and a configuration value only — with no change to the review logic, the checklist reading,
  the prompt construction, the result parsing, or the comment posting.
- **FR-033**: The agent repository MUST be inspected against FR-030 to FR-032, and adjusted where they do
  not already hold, so backend independence is a demonstrated property rather than an intention.
- **FR-034**: The agent MUST stop with a clear message, before doing review work, where the configured
  backend is unsupported or its credential is missing or rejected.

**Delivering feedback**

- **FR-035**: The agent MUST post its findings as comments on the named pull request in a real run,
  each carrying its checklist citation.
- **FR-035a**: The agent MUST post exactly one summary comment on every real run — including a run that
  found no violations — carrying the run's outcome, every finding that identifies no location, the
  checklist documents it skipped, and anything it did not review, so a silent review can never be
  mistaken for a clean one.
- **FR-036**: The agent MUST post one inline comment, anchored to its file and line, for each finding
  that identifies a location, and MUST carry every finding that identifies none in the summary comment
  rather than dropping it.
- **FR-037**: The agent MUST make the origin of its comments unmistakable, so readers of the pull request
  can tell an automated review comment from a human one.
- **FR-038**: The agent MUST NOT approve, reject, vote on, complete, or otherwise alter the state of the
  pull request; it comments only.

**Failing cleanly**

- **FR-039**: The agent MUST fail without posting anything where the named pull request does not exist or
  cannot be reached.
- **FR-040**: The agent MUST report failure, naming the cause, where the review could not be produced —
  the backend was unreachable, its response was unusable, or a required repository could not be obtained
  — and MUST NOT post partial or invented findings.
- **FR-041**: The agent MUST, where it produced findings but could not post them, still make those
  findings available to the developer on the console.

### Key Entities

- **Run Invocation**: one hand-started review — the pull request number, the mode (dry run or posting),
  and the configuration in force.
- **Agent Source**: the review logic that performed the review — either the copy obtained from the agent
  repository at run time and the version it came from, or the developer's local working copy when the
  override was used.
- **Checklist Document**: one Excel or Word file in the checklist repository — its name, its format, and
  whether it was read, skipped, or yielded no rules.
- **Checklist Item**: a single review rule — its identifier (taken from the document where the document
  supplies one, derived otherwise), its text, and the document or documents it came from.
- **Combined Checklist**: the single set of checklist items assembled from every readable document, and
  the record of which documents contributed and which were skipped.
- **Pull Request Target**: the collection, project, repository, and pull request number under review.
- **Change Set**: the reviewable changed content of the pull request, addressable by file and line, after
  excluded files have been removed.
- **Review Bound**: the configurable limit on how much change one run reviews, and the record of which
  files fell outside it.
- **Exclusion List**: the file patterns whose changed content is never sent to the model backend — a
  built-in default covering secret-bearing and machine-generated paths, replaceable or extendable
  through configuration.
- **Declared Intent**: the pull request's title and description — what the author says the change does.
- **Linked Story**: the user story or work item linked to the pull request, reduced to plain text and
  used as review context.
- **Finding**: one reported violation — the checklist item and source document it cites, the problem, and
  where it occurs.
- **Review Report**: the single result of one run — its findings, the documents that were skipped, what
  was not reviewed, and the run's outcome.
- **Posted Feedback**: what the run leaves on the pull request — one summary comment carrying the
  outcome, the unlocatable findings and anything skipped, plus one inline comment per located finding.
  Recognisable as the agent's own, and identical in content to what a dry run prints.
- **Backend Profile**: the configured model backend and model, selected by configuration and reached only
  through the backend interface.
- **Credential Set**: the clone-and-read token and the separate comment token, each supplied through
  configuration and used only for its own purpose.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A developer with a populated configuration file reviews an open pull request end to end —
  dry run, then real run — in a single command each, with zero code changes between them.
- **SC-002**: On a pull request deliberately written to break at least one checklist rule, the dry run
  reports that rule as a finding, and the real run posts a comment for it, in 3 of 3 consecutive runs.
- **SC-003**: 100% of reported findings name both the checklist item and the source document behind them.
- **SC-004**: A dry run against a real pull request leaves the pull request with exactly zero new
  comments — verified by comparing the pull request before and after the run.
- **SC-005**: A run against a pull request number that does not exist stops with a message naming that
  pull request, reports failure, and leaves zero comments anywhere.
- **SC-006**: Rules from every readable checklist document in the repository are in force in a single
  review, across both document formats, with at least two documents of each format present.
- **SC-007**: A quality lead adds a new checklist document and sees its rules applied on the next review
  with zero configuration changes and zero code changes.
- **SC-008**: Switching the configured backend between the two supported backends changes zero files of
  review logic, checklist reading, prompt construction, result parsing, or comment posting — verified by
  inspecting what changed between the two runs.
- **SC-009**: An audit of the agent repository finds zero language-model calls outside the single backend
  interface.
- **SC-010**: No personal access token or model key appears in console output, log files, posted
  comments, or version-controlled files — verified by inspecting one complete dry run and one complete
  real run.
- **SC-011**: Every run ends with an outcome a developer can act on: either a review they can read, or a
  named cause they can correct — measured as zero runs that end silently or ambiguously across the
  end-to-end test.
- **SC-012**: Every changed file in a reviewed pull request is either reviewed or named in the report as
  skipped, with a reason — zero files dropped silently, verified on a pull request that touches both
  reviewable and excluded paths.

## Assumptions

- This feature is the manual, local slice of the review agent; it scopes this iteration to hand-started
  runs and leaves the automated path to a later one.
- The developer's machine can reach the TFS collection and the configured model backend directly; no
  proxy or jump host is assumed.
- The pull request under review already exists and was opened by hand before the agent is run.
- Both personal access tokens are provisioned in advance with the access their role needs — repository
  read and work item read for the first, pull request read and comment for the second.
- Configuration is supplied through a local file or environment that is excluded from version control;
  only a redacted example ships with the agent.
- Checklist documents are ordinary, unprotected Excel and Word files, small enough to read in full on
  each run, and written in English.
- Checklist documents are authored by quality leads; the agent consumes them and never edits them.
- Where more than one work item is linked to a pull request, all of them are treated as context.
- The review reasons over changed text only. It does not build, run, or test the code, so its findings
  are indicative and a human decides what to do with them.
- Reviewing sends checklist text, story text, the pull request description, and the changed code of
  non-excluded files to the configured model backend; the organisation accepts that exposure for the
  backend it configures.
- The default review bound is set so that a typical feature pull request is reviewed in full and only
  unusually large changes (bulk renames, generated migrations, vendored dependency updates) reach it;
  a developer raises it where their pull requests are routinely larger and the extra cost is acceptable.
- The default exclusion list is a safety net against a secret committed by accident, not a substitute for
  secret scanning. It matches on file path and name only — it does not detect a credential pasted into an
  ordinary source file.
- Re-running the agent on the same pull request is the developer's own choice; this iteration does not
  reconcile a new review with comments left by a previous run, beyond never altering them.

## Dependencies

- The on-prem TFS collection, its project, and the three repositories — sample code, agent, and
  checklist — exist and are reachable from the developer's machine. Which collection, project,
  repositories, backend, and model a run targets are configuration values (FR-005); the target
  environment for this iteration is the organisation's existing on-prem collection and its
  architecture-unit project.
- At least one readable checklist document exists in the checklist repository.
- The configured model backend is reachable and its credential is valid.

## Out of Scope

- Triggering the review from a build pipeline or a branch policy; every run in this iteration is started
  by hand.
- Blocking, gating, voting on, or otherwise influencing whether a pull request can merge.
- Building, running, or testing the code under review, or any other runtime or behavioural assessment of
  it.
- Reconciling repeated reviews of the same pull request — updating, de-duplicating, or resolving comments
  left by an earlier run.
- Authoring or curating checklist content.
- Reviewing anything other than a single named pull request.
- A dashboard, database, or historical record of past reviews.
- Cloud-hosted Azure DevOps Services as a target; this iteration targets the on-prem server.
