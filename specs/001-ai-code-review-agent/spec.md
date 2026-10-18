# Feature Specification: AI Code Review Agent for On-Prem TFS Pull Requests

**Feature Branch**: `001-ai-code-review-agent`  
**Created**: 2026-08-26  
**Status**: Draft  
**Input**: User description: "Req.md — Claude Code Build Spec: AI Code Review Agent for On-Prem TFS"

## Overview

Development teams working on the on-prem TFS / Azure DevOps Server collection review pull
requests by hand. Two kinds of problems slip through consistently: a PR description claims
to cover part of a user story that the change does not actually implement (a **false
coverage claim**), and shared code-review checklist rules go unchecked because nobody
re-reads the checklist on every pull request.

This feature delivers an automated reviewer that reads a pull request together with its
linked user story and the team's shared checklists, then posts structured feedback back
onto the pull request. It is a **reviewer aid, not a merge gate** — it reasons over the
changed text and cannot verify runtime behaviour, so a human always decides the merge.

## Clarifications

### Session 2026-08-26

- Q: Behaviour when the same pull request is reviewed again after the author pushes updates? → A: Incremental — one summary discussion per pull request updated in place, inline comments only for findings not already posted
- Q: What state should the discussions the review posts be created in? → A: Active — they behave like a human reviewer's comments and participate in any comment-resolution policy the team has enabled
- Q: Should changed files be filtered before being sent to the AI provider? → A: Yes — a built-in default exclusion list for sensitive and noisy paths, overridable through configuration, with every skipped file named in the report
- Q: What outcome should a run report when the review itself cannot complete (unreachable provider, unusable response)? → A: Success with a clearly-reported degraded outcome, plus a discussion on the pull request saying the review could not run and why — so provider flakiness never gates a merge, while a setup defect still fails loudly
- Q: How much change should one review take on before it stops? → A: A configurable bound — review up to the limit, prioritising the files most relevant to the story and checklists, and name every unreviewed file in the report

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Story-coverage review with false-coverage detection (Priority: P1)

A developer opens a pull request that implements part of a user story and writes a
description listing what the change covers and what is deliberately left for later. The
reviewer wants to know, without reading the story and the change side by side, whether each
acceptance criterion is plausibly implemented, which criteria are simply out of scope for
this change, and — critically — whether the author has claimed to cover something the
change does not actually do.

**Why this priority**: This is the headline value and the reason the feature exists. A
partial pull request is normal and must not be punished for it; a *wrong claim* about what
is covered is the defect worth surfacing. Everything else in the feature is additive to this.

**Independent Test**: Point the review at an existing pull request with a linked story and
run it in preview mode (no posting). It must print a coverage classification for every
acceptance criterion and call out any criterion the description claims as covered but the
change does not implement. Delivers value on its own — a reviewer can read the printed
report and act on it with no posting or automation in place.

**Acceptance Scenarios**:

1. **Given** a pull request linked to a user story with five acceptance criteria, where the
   change implements four of them, **When** the review runs, **Then** those four criteria
   are classified `appears_addressed` with supporting evidence drawn from the change, and
   the fifth is classified `not_addressed`.
2. **Given** the same pull request, whose description claims the fifth criterion is covered,
   **When** the review runs, **Then** the report calls out a **false coverage claim** for
   that criterion as the headline finding.
3. **Given** a pull request whose description names follow-up work as explicitly out of
   scope, **When** the review runs, **Then** none of that out-of-scope work is reported as a
   problem.
4. **Given** a change that conflicts with an acceptance criterion rather than merely omitting
   it, **When** the review runs, **Then** that criterion is classified
   `possible_contradiction` and reported as a finding.
5. **Given** a pull request with no linked work item, **When** the review runs, **Then** the
   review proceeds as a checklist-only review and the report states that no story was linked,
   without failing.
6. **Given** any pull request, **When** the review runs, **Then** no criterion classified
   `not_addressed` produces a comment or causes a failure.

---

### User Story 2 - Review against the team's shared checklists (Priority: P2)

An architecture or quality lead maintains code-review rules as markdown files in a shared
repository. They want every reviewed pull request measured against the current contents of
that repository, and they want to add or edit a checklist file without asking anyone to
change the review's configuration or code.

**Why this priority**: Checklist enforcement is the second half of the review value and the
part that scales across teams, but it is useful only once the review flow from Story 1
exists.

**Independent Test**: Place two checklist files in the shared repository, run a review, and
confirm violations are reported and each cites the checklist file it came from. Then add a
third file and re-run with no configuration change; rules from the new file take effect.

**Acceptance Scenarios**:

1. **Given** a shared checklist location containing several matching checklist files, **When**
   the review runs, **Then** all of them are read and combined into a single checklist, and
   every reported violation names the source checklist file.
2. **Given** a new checklist file added to that location, **When** the next review runs,
   **Then** its rules are applied with no configuration or code change.
3. **Given** a change that violates a checklist rule, **When** the review runs, **Then**
   exactly that violation is reported, with the offending file and line where the change
   identifies one.
4. **Given** a change that violates no checklist rule, **When** the review runs, **Then** no
   checklist violations are reported — no speculative or stylistic filler findings.
5. **Given** the shared checklist location contains no matching files, **When** the review
   runs, **Then** the review continues as a coverage-only review and the report states that
   no checklists were found.

---

### User Story 3 - Feedback appears automatically on the pull request (Priority: P3)

A team wants the review to happen without anyone remembering to ask for it. When a pull
request is opened from a feature branch into the main integration branch, the review should
run automatically and its findings should appear on the pull request, where the reviewers
already are.

**Why this priority**: Automation multiplies the value of Stories 1 and 2 but is not required
to get value from them — the same review can be requested manually. It also depends on
one-time repository and policy setup outside the review itself.

**Independent Test**: Configure the automatic trigger on the integration branch, open a pull
request from a feature branch, and confirm the feedback appears on that pull request with no
manual step.

**Acceptance Scenarios**:

1. **Given** the automatic trigger is configured on the integration branch, **When** a pull
   request is opened from a feature branch into it, **Then** the review runs and its feedback
   is posted to that pull request.
2. **Given** a completed review with findings, **When** feedback is posted, **Then** the pull
   request receives one summary discussion carrying the overall summary and the full coverage
   map, plus one inline comment anchored to the relevant file and line for each contradiction,
   checklist violation, and code issue that identifies a location.
3. **Given** a finding with no identifiable file and line, **When** feedback is posted,
   **Then** the finding appears in the summary discussion rather than being dropped.
4. **Given** preview mode is enabled, **When** the review runs, **Then** the identical feedback
   is printed for the operator and nothing is posted to the pull request.
5. **Given** the review is configured to be non-blocking, **When** findings including
   contradictions are reported, **Then** the run still reports success and the merge decision
   is left to a human.
6. **Given** the review is configured to be blocking, **When** a contradiction, a false
   coverage claim, or a high-severity code issue is reported, **Then** the run reports failure
   so the configured branch policy can surface it.

---

### User Story 4 - Retarget and re-host without code changes (Priority: P4)

A platform engineer wants to point the review at a different pull request, project,
repository, or checklist source, and to switch the underlying AI provider, purely through
configuration — including running it from a central location shared by many teams later on.

**Why this priority**: Portability protects the investment and enables the wider rollout, but
the first demo works without it.

**Independent Test**: Run two reviews that differ only in configuration — a different target
pull request and a different AI provider — and confirm both work with no change to any
reviewing logic.

**Acceptance Scenarios**:

1. **Given** the review is configured for one AI provider, **When** the configuration names a
   different supported provider and model, **Then** the review produces the same shape of
   feedback with no change to the reviewing logic.
2. **Given** an automatic run, **When** the run supplies the pull-request context and its own
   credential, **Then** those are used in preference to any manually configured value, and no
   separate manual credential is required.
3. **Given** a manual run, **When** only a manual credential is available, **Then** the review
   authenticates with it successfully.
4. **Given** a required configuration value is missing, **When** the review starts, **Then** it
   stops immediately and names every missing value.
5. **Given** any run, **When** its output and logs are inspected, **Then** no credential, token,
   or key appears in them.

---

### Edge Cases

- **No linked work item**: coverage review is skipped, the checklist review still runs, and the
  report says no story was linked. Not a failure.
- **Multiple linked work items**: all linked stories and their acceptance criteria are included.
- **Story fields carry markup**: titles, descriptions, and acceptance criteria arrive as rich text
  with markup and encoded entities; they are reduced to plain text before use, so the review never
  reasons over markup noise.
- **Change set too large for one review pass**: the change is split, the story and the combined
  checklist accompany every part, and findings from all parts are merged into one report with
  duplicates collapsed.
- **Change set exceeds the configured review bound**: the most story- and checklist-relevant portion
  is reviewed, every unreviewed file is named in the report, and the review still completes normally
  rather than degrading or failing.
- **Empty or non-reviewable change set** (no files changed, only binary/non-text files, or every
  changed file excluded): the report says there was nothing to review rather than inventing findings.
- **A pull request changes only excluded files** (for example a certificate rotation): nothing is
  sent to the AI provider, and the report lists the skipped files so the reviewer knows why it is
  silent.
- **Checklist source empty, unreachable, or containing no matching files**: the review degrades to
  a coverage-only review and says so.
- **Malformed AI response**: one retry with a stricter instruction; if it is still malformed, the run
  ends as a degraded review — it says so on the pull request and reports success, rather than posting
  partial or invented feedback or blocking the merge.
- **AI provider or checklist source unreachable**: degraded review — the pull request gets a
  discussion explaining what was unavailable, and the run reports success.
- **Missing configuration, unusable credential, or no permission to post**: setup failure — the run
  reports failure naming the value or permission to correct, and shows the operator any findings it
  could not post.
- **Both automatic and manual credentials present**: the automatic run's own credential wins.
- **Neither credential present**: the review stops at startup with a clear message.
- **Cannot post to the pull request** (missing permission): the failure is reported explicitly,
  naming posting permission as the cause, and the findings are still shown to the operator.
- **Private certificate authority on the TFS host**: the host is trusted through the machine's own
  trust store; certificate verification is never disabled.
- **Finding points at a line outside the reviewed change**: it is reported in the summary rather
  than anchored to an unrelated line.
- **Review runs again after the pull request is updated**: the summary discussion is updated in
  place and only findings not already posted get a new inline comment; nothing already posted is
  duplicated (FR-030).
- **A finding disappears after the author fixes it**: its earlier inline comment stays until a human
  resolves it; the updated summary reflects that the finding is gone.

## Requirements *(mandatory)*

### Functional Requirements

**Inputs and configuration**

- **FR-001**: System MUST read every runtime value — target pull request location, credentials,
  checklist source, AI provider settings, and behaviour switches — from external configuration
  supplied at run time. No such value may be embedded in the deliverable.
- **FR-002**: System MUST accept configuration from an operator-supplied configuration file for
  manual runs and from the automation environment for automatic runs, preferring values supplied by
  the automation when both are present.
- **FR-003**: System MUST validate configuration before doing any work and, when required values
  are missing, MUST stop immediately with a message naming every missing value.
- **FR-004**: System MUST require exactly one of the two credential modes — the automation's own
  credential or an operator-supplied credential — and MUST NOT require the operator credential when
  the automation supplies one.
- **FR-005**: System MUST NOT require an AI credential when the configured provider needs none.
- **FR-006**: System MUST keep credentials out of its console output, its logs, the material sent to
  the AI provider, and the feedback posted to the pull request.
- **FR-007**: System MUST provide a preview mode that produces the complete feedback for the
  operator without posting anything to the pull request, and MUST default to preview mode and to
  non-blocking behaviour.

**Gathering review material**

- **FR-008**: System MUST retrieve the work item(s) linked to the target pull request, and MUST
  continue with a checklist-only review — noting the absence in its report — when none are linked.
- **FR-009**: System MUST retrieve each linked work item's title, description, acceptance criteria,
  type, state, and tags, and MUST reduce rich-text fields to plain text before use.
- **FR-010**: System MUST retrieve the changed content of the pull request's most recent revision.
- **FR-011**: System MUST retrieve the pull request's description for use as the author's declared
  scope.
- **FR-012**: System MUST enumerate the configured shared checklist location on the configured
  branch, read every file matching the configured file pattern, and combine them into one checklist
  in which each section is labelled with the file it came from.
- **FR-013**: System MUST pick up checklist files added to that location with no configuration or
  code change, and MUST ignore folders and non-matching files.
- **FR-014**: System MUST split an oversized change set across multiple review passes, supply the
  story and the combined checklist with every pass, and merge the resulting findings into one report
  with duplicates collapsed.
- **FR-014a**: System MUST exclude changed files matching a built-in default exclusion list from the
  material it sends to the AI provider. The list MUST cover paths that commonly carry secrets or
  credentials (environment files, certificate and key files, credential stores) and paths that are
  machine-generated or otherwise not worth reviewing (dependency lock files, build output, vendored
  or generated code).
- **FR-014b**: System MUST allow an operator to replace or extend that exclusion list through
  configuration, so a team whose checklists genuinely target configuration files can review them.
- **FR-014c**: System MUST name every excluded file, and the reason it was excluded, in its report —
  so a reviewer can always tell that a file was skipped rather than found clean. It MUST NOT silently
  drop a changed file.
- **FR-014d**: System MUST report that there was nothing to review, rather than fail or invent
  findings, when exclusions leave no reviewable content in the change set.
- **FR-014e**: System MUST bound the amount of change it reviews in one run by a configurable limit,
  so review latency and cost stay predictable on an unusually large pull request.
- **FR-014f**: System MUST, when a change set exceeds that limit, review the portion most relevant to
  the linked story and the combined checklist rather than an arbitrary slice, and MUST name every file
  it left unreviewed in its report so no unreviewed file can be mistaken for a clean one.

**Reviewing**

- **FR-015**: System MUST classify each acceptance criterion as exactly one of `appears_addressed`,
  `not_addressed`, or `possible_contradiction`, with supporting evidence.
- **FR-016**: System MUST treat `not_addressed` as neutral information — never a defect, never a
  comment, never a cause of failure — because a pull request routinely implements only part of a
  story.
- **FR-017**: System MUST treat the "covered" list in the pull request description as the author's
  authoritative scope, MUST still verify each covered item against the change, and MUST report any
  claimed-but-absent item as a **false coverage claim**.
- **FR-018**: System MUST leave work the description declares out of scope unflagged.
- **FR-019**: System MUST check the change against every combined checklist item and report only
  real violations, each naming the checklist file it came from.
- **FR-020**: System MUST report code issues found in the change, each with a severity of `high`,
  `medium`, or `low`.
- **FR-021**: System MUST produce an overall plain-language summary of the review.
- **FR-022**: System MUST reject an AI response that does not match the agreed report shape or uses
  a status or severity value outside the permitted sets, MUST retry once with a stricter
  instruction, and — if the second attempt also fails — MUST treat the run as a degraded review per
  FR-029 rather than emit partial or invented feedback.

**Delivering feedback**

- **FR-023**: System MUST post one summary discussion to the pull request containing the overall
  summary and the full coverage map, with any false coverage claim called out prominently.
- **FR-024**: System MUST post one inline comment, anchored to its file and line, for each
  `possible_contradiction`, checklist violation, and code issue that identifies a location.
- **FR-024a**: System MUST create every discussion it posts — the summary discussion and each inline
  comment — in an **active** state, so it appears in the pull request's normal comment flow exactly
  as a human reviewer's comment would and participates in any comment-resolution policy the team has
  enabled. System MUST NOT resolve its own discussions; only a human resolves them.
- **FR-025**: System MUST place findings that identify no usable location in the summary discussion
  instead of dropping them.
- **FR-026**: System MUST, in preview mode, present exactly the feedback it would otherwise post.
- **FR-027**: System MUST report success on a completed review by default, regardless of findings,
  and likewise on a degraded review (FR-029).
- **FR-028**: System MUST, when configured to block, report failure if the review contains any
  contradiction, false coverage claim, or high-severity code issue.
- **FR-029**: System MUST distinguish two kinds of incomplete run and MUST NOT conflate them:
  - **Degraded review** — it reached the pull request but could not produce a full review (checklist
    source unreachable, AI provider unreachable or erroring, AI response unusable after the retry of
    FR-022, change set unreviewable). System MUST report **success**, so a transient outage never
    gates a merge, MUST post a discussion on the pull request stating that the review could not be
    completed and why, and MUST NOT post partial, stale, or invented findings.
  - **Setup failure** — it could not start or could not deliver anything (a missing or invalid
    required configuration value, no usable credential, a target pull request it cannot reach or
    authenticate against, or no permission to post). System MUST report **failure** with a message
    naming the cause and the value or permission to correct, so the defect is visible and gets fixed
    rather than leaving a permanently silent review.
- **FR-029a**: System MUST make the outcome of every run and its cause visible in the run's own
  output for the operator, and MUST NOT present a degraded or failed run as a normal completed
  review. Where it holds findings it cannot post, it MUST still make them available to the operator.
- **FR-030**: System MUST review the pull request again whenever the author updates it, and MUST
  deliver each re-review **incrementally**: exactly one summary discussion per pull request, updated
  in place to reflect the latest review, and inline comments only for findings it has not already
  posted. It MUST NOT re-post a finding matching one it has already posted, and MUST NOT delete or
  resolve its own earlier comments — a human resolves those.
- **FR-031**: System MUST make the origin of its feedback unmistakable, so readers of the pull
  request can tell automated review comments from human ones.

**Operating boundaries**

- **FR-032**: System MUST keep the TFS interaction and the AI-provider interaction as separately
  replaceable parts, so that changing provider or hosting platform touches only the corresponding
  part and no reviewing logic.
- **FR-033**: System MUST support switching AI provider and model through configuration alone.
- **FR-034**: System MUST verify the identity of the TFS host using the machine's trust store and
  MUST NOT offer or use an option to skip that verification.
- **FR-035**: System MUST review whichever pull request its configuration names, in any repository
  and project of the collection, with no change to the deliverable.
- **FR-036**: System MUST keep the operator's populated configuration file out of version control;
  only a redacted example may be distributed.

### Key Entities

- **Pull Request Context**: the collection, project, repository, and pull request identifier the
  review targets, plus the credential used to reach it. Sourced from configuration or from the
  automation environment.
- **User Story (Work Item)**: a linked requirement carrying a title, description, acceptance
  criteria, type, state, and tags. Zero, one, or many per pull request.
- **Acceptance Criterion**: a single testable statement from a user story; the unit of the coverage
  map.
- **Declared Scope**: the "covered" and "not in this PR" lists taken from the pull request
  description — the author's claim, authoritative for intent but verified against the change.
- **Change Set**: the reviewable content of the pull request's latest revision, addressable by file
  and line, after excluded files have been removed.
- **Review Bound**: the configurable limit on how much change one run reviews, and the record of what
  fell outside it.
- **Exclusion List**: the file patterns whose changed content is never sent to the AI provider —
  a built-in default covering secret-bearing and machine-generated paths, replaceable or extendable
  through configuration.
- **Checklist**: the combined set of review rules assembled from every matching file in the shared
  checklist location, each rule traceable to its source file.
- **Review Report**: the single structured result of one review — a coverage map, checklist
  violations, code issues, and a summary.
- **Coverage Entry**: one acceptance criterion plus its classification and supporting evidence.
- **Checklist Violation**: a broken rule, its source checklist file, the problem, and where it
  occurs.
- **Code Issue**: a problem found in the change, its severity, and where it occurs.
- **False Coverage Claim**: a coverage entry the declared scope claims as covered while the change
  does not implement it — the feature's headline finding.
- **Posted Feedback**: the summary discussion and inline comments the review leaves on the pull
  request, or their previewed equivalent. Recognisable as the review's own, so a re-review can update
  its summary in place and tell already-posted findings from new ones.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On the reference pull request (a five-criterion story whose change implements four of
  them, with a description claiming the fifth is covered), the review classifies the four
  implemented criteria as addressed, reports the fifth as a false coverage claim, raises the
  matching checklist rule as a violation, and flags none of the explicitly out-of-scope follow-up
  work — in 3 out of 3 consecutive runs.
- **SC-002**: A reviewer opening a pull request finds the automated feedback already on it within 5
  minutes of the pull request being opened, for any change set up to the configured review bound —
  and within that same 5 minutes for a change set that exceeds it.
- **SC-003**: A quality lead can add a new checklist file to the shared location and see its rules
  applied on the next review with zero configuration changes and zero code changes.
- **SC-004**: No credential, token, or key appears anywhere in a complete run's output, logs, posted
  feedback, or version-controlled files — verified by inspecting one full automatic run and one full
  manual run.
- **SC-005**: Retargeting the review at a different pull request, project, or repository, and
  switching to a different AI provider, are each achieved by configuration change alone — 0 files of
  reviewing logic modified.
- **SC-006**: Across 5 reviews of partial pull requests, unaddressed acceptance criteria produce 0
  comments and 0 failures.
- **SC-007**: Across 5 reviews, every reported checklist violation names a rule that genuinely
  exists in the shared checklists and cites the correct source file — 100% traceable.
- **SC-008**: Across induced failures — unreachable AI provider, unusable AI response after the
  retry, unreachable checklist source, missing configuration value, unusable credential, unreachable
  pull request, missing posting permission — 100% end with a message naming the cause and the value
  or permission to correct, 0 present themselves as a normal completed review, and 0 post partial or
  invented findings. Degraded reviews block 0 merges; setup failures report failure in 100% of cases.
- **SC-009**: With default settings, the review itself fails 0 validation builds and casts 0 votes
  on a pull request; the merge decision rests with a human in 100% of cases. Where a team has
  enabled a comment-resolution policy, the review's active discussions participate in it — a human
  reading and resolving each one is that human decision, not an automated block.
- **SC-010**: Given a pull request that changes a secret-bearing file (an environment file, a
  certificate, a key file) alongside ordinary code, 0 bytes of that file's content reach the AI
  provider, and the report names it as skipped — verified for every path in the default exclusion
  list.
- **SC-011**: On a pull request exceeding the review bound, 100% of unreviewed files are named in the
  report; 0 unreviewed files are presented as reviewed-and-clean.

## Assumptions

- The reference demo target is the sample .NET API pull request, story #101 "Add items to shopping
  cart", and the shared checklist repository described in the source brief; those artefacts exist
  and are reachable from the machine or build agent running the review.
- On this on-prem server, pull requests cannot trigger a build directly from pipeline
  configuration; the automatic trigger is a build-validation branch policy on the integration
  branch. Configuring that policy, enabling the build's access to its own credential, and granting
  the build identity permission to contribute to pull requests are one-time administrative steps
  outside this feature's deliverable.
- The shared checklist default location is the repository root, the default file pattern matches
  markdown files, and the default checklist branch is the one named in the source brief; operators
  override any of these through configuration when their repository differs.
- Acceptance criteria are enumerable from the work item's acceptance-criteria field; where a story
  puts them only in the description, the review reads them from there.
- Inline comments anchor to lines of the post-change file, since that is what the change set
  addresses.
- The same credential that reads the target repository can also read the shared checklist repository
  within the same collection.
- The review reasons over changed text only. It does not build, run, or test the code, so its
  findings are indicative and a human confirms them — this is why non-blocking is the default.
- Because the review posts **active** discussions (FR-024a), a team that also enables the
  "all comments resolved before merge" branch policy will need someone to read and resolve the
  review's comments before merging — including the always-posted summary discussion, even on a clean
  review. That is the intended behaviour: the comments are meant to be read, not auto-dismissed. A
  team that wants the review fully out of the merge path should leave that policy off.
- Reviews are English-language; checklist files and story fields are assumed to be in English.
- The AI provider's judgement is the main variable in review accuracy; the report shape is
  contractually enforced, the judgement inside it is not.
- Reviewing a pull request sends story text, checklist text, the pull request description, and the
  changed text of non-excluded files to the configured AI provider. Where that provider is external,
  the organisation accepts that exposure, or configures a self-hosted or internal provider instead.
- The default review bound is set so that a typical feature pull request is reviewed in full and only
  unusually large changes (bulk renames, generated migrations, vendored dependency updates) hit it;
  operators raise it where their pull requests are routinely larger and the extra cost is acceptable.
- The default exclusion list is a safety net against a secret committed by accident, not a substitute
  for secret scanning. It matches on file path and name only — it does not detect a credential pasted
  into an ordinary source file.

## Out of Scope

- Approving, rejecting, completing, or otherwise voting on pull requests; the review comments only.
- Editing code, proposing patches, or committing fixes.
- Building, running, or testing the code under review, or measuring real test coverage.
- Creating, updating, or transitioning work items.
- Authoring or curating the shared checklist content; the feature consumes it.
- Reviewing anything other than a pull request — whole-branch audits, scheduled repository sweeps,
  or commit-level review outside a pull request.
- A dashboard, database, or historical reporting of past reviews.
- Cloud-hosted Azure DevOps Services as a target; this feature targets the on-prem server.
- Moving the review into a separate central repository for wider rollout — anticipated, but a
  hosting change, not part of this deliverable.
