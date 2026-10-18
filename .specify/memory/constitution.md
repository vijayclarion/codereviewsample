<!--
SYNC IMPACT REPORT
==================
Version change: (unversioned template) -> 1.0.0
Bump rationale: MAJOR. First ratification. The file was an unfilled template with every
principle a [PLACEHOLDER]; this establishes the initial governance baseline, so 1.0.0
rather than a MINOR addition to an existing constitution.

Principles established (all seven were already enforced as plan-local gates in
specs/001-ai-code-review-agent/plan.md; this ratifies them as project principles):
  I.   Configuration Is External          <- gate "No hard-coded configuration" (FR-001)
  II.  Secrets Never Surface              <- gate "No secret in logs, prompt, or output" (FR-006)
  III. Transport Security Is Not Optional <- gate "TLS verification never disabled" (FR-034)
  IV.  Replaceable Boundaries             <- gates "TFS REST in one module" + "Provider format
                                             in one module" (FR-032, FR-033), merged
  V.   Partial Work Is Not A Defect       <- gate "not_addressed inert" (FR-016)
  VI.  The Human Decides The Merge        <- gate "Non-blocking default" (FR-027)
  VII. Silence Must Never Read As Success <- NEW; derived from FR-014c, FR-014f, FR-022,
                                             FR-029, SC-008, SC-011, all already implemented

Added sections: Scope And Non-Scope; Engineering Constraints; Development Workflow And
Quality Gates; Governance.
Removed sections: none (all template placeholders replaced).

Templates and artifacts:
  .specify/templates/plan-template.md   - no change needed; its Constitution Check section is
                                          a generic "[Gates determined based on constitution
                                          file]" placeholder that resolves against this file
  .specify/templates/spec-template.md   - no change needed; contains no constitution references
  .specify/templates/tasks-template.md  - no change needed; contains no constitution references
  specs/001-ai-code-review-agent/plan.md - UPDATED: Constitution Check section replaced; it
                                          previously recorded "PASS, vacuously - no ratified
                                          principles", which is now false
  CLAUDE.md                             - UPDATED: added a pointer to this file

Deferred items: none. No TODO placeholders remain.
-->

# AI Code Review Agent Constitution

## Scope And Non-Scope

This constitution governs the **AI code review agent** in `review_agent/`, its tests in `tests/`,
its pipeline definition, and its specifications under `specs/`.

It does **not** govern `src/eShop.Api/` or `demo/`. Those are the deliberately-flawed demo target
the agent reviews: `CartController.AddItem` omits the stock-limit check on purpose, and
`demo/pr-description.md` falsely claims that check is covered. Holding the demo to a correctness
principle would destroy the acceptance scenario it exists to provide. Stating this boundary
explicitly is not an exemption — it is the difference between a fixture and a defect.

## Core Principles

### I. Configuration Is External

Every runtime value — collection URL, project, repository, pull request id, credentials,
checklist location, provider, model, and every behaviour switch — MUST be read from the
environment at run time. No such value may appear in source.

`config.py` MUST be the only module that reads the environment. Every other module receives a
frozen configuration object.

**Rationale**: the agent is pointed at a different pull request on every run and is intended to
serve many teams from one deployment. A single hard-coded value converts a configuration change
into a code change, and one module owning the environment is what makes "nothing is hard-coded"
a property you can test rather than a claim you repeat.

### II. Secrets Never Surface

Credentials MUST NOT appear in console output, logs, the material sent to the AI provider, the
feedback posted to a pull request, or version control.

Every secret loaded MUST be registered with the redaction registry, and all process output MUST
pass through the single `emit` path. Authorization headers MUST be constructed per request and
never formatted into a message. Error text MUST NOT include a query string.

**Rationale**: the realistic leak is never a deliberate print of a token. It is an exception
message, a repr of a request object, or a debug dump that happens to carry a header. Filtering at
the one point where text leaves the process catches all three; asking developers to be careful
catches none of them reliably.

### III. Transport Security Is Not Optional

TLS verification MUST NOT be disabled, weakened, or made configurable. `verify=False`,
`CERT_NONE`, unverified SSL contexts, and warning suppression are forbidden. Host identity MUST be
established through the machine's own trust store.

**Rationale**: an on-prem collection behind a private certificate authority fails to verify out of
the box, and the one-character fix is the wrong one. Making the shortcut unavailable — and failing
the build if it appears — is the only version of this rule that survives a deadline.

### IV. Replaceable Boundaries

Two boundaries MUST stay isolated so each can be replaced without touching reviewing logic:

- **TFS REST** appears only in `tfs_client.py`. No other module may import an HTTP client for it.
- **AI provider wire format** appears only in `llm_adapter.py`. Everything downstream is
  provider-blind and receives raw model text.

Adding a provider MUST require no change outside `llm_adapter.py`. Changing hosting platform MUST
require no change outside `tfs_client.py`.

**Rationale**: the agent is expected to outlive both its current provider and its current TFS
version. A boundary that is merely intended leaks within a release; a boundary asserted by an
import test does not.

### V. Partial Work Is Not A Defect

An acceptance criterion the diff does not address MUST be reported as neutral information. It MUST
NOT produce a comment, MUST NOT contribute to a failing outcome, and MUST NOT be described as a
problem.

Work the pull request description declares out of scope MUST NOT be flagged.

Only three things are loud: a diff that **contradicts** a criterion, a **false coverage claim**
(the description claims an item is covered and the diff does not implement it), and a genuine
high-severity code issue.

**Rationale**: a pull request routinely implements part of a story. An agent that treats every
unimplemented criterion as a finding is noise on the majority of pull requests, and reviewers
stop reading it — at which point the real findings are lost too. This principle is the reason the
tool is usable, not a softening of it.

### VI. The Human Decides The Merge

The agent is a reviewer aid, not a merge gate. By default a completed review MUST report success
regardless of what it found, and a review that could not complete MUST also report success.

The agent MUST NOT vote on, approve, reject, complete, or abandon a pull request, MUST NOT resolve
or delete a comment thread, and MUST NOT modify code. Blocking behaviour MUST remain opt-in
configuration, never a default.

**Rationale**: the agent reasons over diff text and cannot verify runtime behaviour, so its
findings are indicative. A tool that blocks merges on indicative findings will be switched off.
Separately, a provider outage must never gate anyone's work — availability of the reviewer is not
a property of the code under review.

### VII. Silence Must Never Read As Success

Absence of a finding MUST NOT be presentable as evidence of correctness. Specifically:

- A file excluded from review MUST be named in the report, with the reason.
- A file left unreviewed because a bound was reached MUST be named, with its size.
- A review that could not complete MUST say so on the pull request and state that no conclusion
  may be drawn from its silence.
- A run that fails MUST distinguish **degraded** (could not review; reports success, never gates a
  merge) from **setup failure** (will never work until a setting changes; reports failure).
- Malformed provider output MUST get exactly one stricter retry and then degrade. Partial or
  invented findings MUST NOT be emitted.

**Rationale**: the feature's value is largely negative — "what did the review not find". That makes
an unreported gap actively dangerous: it converts an absence of evidence into false confidence,
which is worse than an obvious hole. Distinguishing the two failure kinds is the same idea applied
to outcomes: a red build says "fix your configuration", a green degraded build says "nobody
reviewed this", and conflating them means one of those messages never gets heard.

## Engineering Constraints

- **Language**: Python 3.11+, no features beyond 3.11.
- **Runtime dependencies**: minimal and justified. Currently `requests`, `python-dotenv`,
  `truststore`. Adding one requires a recorded decision naming what breaks without it — the target
  is a locked-down self-hosted build agent where transitive dependency trees fail to install.
- **Provider SDKs**: not used. One HTTP path, no version drift.
- **State**: the agent is stateless. What it needs to know about its own prior output it reads back
  from the pull request. No database, no local state file.
- **Platform**: must run unchanged on a Windows self-hosted build agent and a developer
  workstation. Console output must survive a non-UTF-8 console.

## Development Workflow And Quality Gates

**A principle without a test is a wish.** Each of the seven principles above MUST have at least one
automated test that fails when the principle is violated. Amending a principle includes amending
its test. Current mapping:

| Principle | Enforced by |
|---|---|
| I. Configuration Is External | `tests/contract/test_import_boundaries.py` |
| II. Secrets Never Surface | `tests/contract/test_no_secrets_leak.py` |
| III. Transport Security Is Not Optional | `tests/contract/test_tls_never_disabled.py` |
| IV. Replaceable Boundaries | `tests/contract/test_import_boundaries.py` |
| V. Partial Work Is Not A Defect | `tests/unit/test_routing_not_addressed.py` |
| VI. The Human Decides The Merge | `tests/contract/test_preview_makes_no_writes.py`, `tests/integration/test_us3_posting.py` |
| VII. Silence Must Never Read As Success | `tests/integration/test_bound_transparency.py`, `tests/contract/test_excluded_files_never_sent.py`, `tests/integration/test_us1_review.py` |

Additional workflow rules:

- The test suite MUST NOT require network access. External surfaces are faked.
- A design decision that was tried and failed MUST be recorded with the reason, not silently
  replaced. A document that prescribes a known-broken approach invites its reintroduction.
- Specification, plan, and task artifacts MUST agree with the implementation. Where they drift, the
  artifact is corrected; drift is not left as a note.

## Governance

This constitution takes precedence over convenience, habit, and schedule pressure. Where a
principle and a deadline conflict, the principle holds or the principle is formally amended — it is
not quietly set aside for one release.

**Amendment procedure**: propose the change with its rationale, state which principle text is
added, altered, or removed, update the enforcing test in the same change, and update this file's
version and Sync Impact Report. Amendments to Principles II, III, and VI additionally require
explicit sign-off from the repository owner, because each protects against a harm that is invisible
at the moment it is caused: a leaked credential, a silently unverified connection, and an
automated gate on human judgement.

**Versioning policy**: semantic versioning.
- **MAJOR** — a principle is removed or redefined incompatibly.
- **MINOR** — a principle or governed section is added, or guidance materially expanded.
- **PATCH** — clarification, wording, or a correction that changes no obligation.

**Compliance review**: the enforcing tests run on every change, so routine compliance is
continuous rather than periodic. Any pull request touching `review_agent/` MUST keep them green;
a change that requires weakening one of these tests is an amendment, not a fix, and MUST be
treated as such. `/speckit-analyze` reports constitution conflicts as CRITICAL.

**Runtime guidance**: `CLAUDE.md` points to the active plan and to this file. Feature-level design
lives in `specs/001-ai-code-review-agent/`; this document governs it rather than repeating it.

**Version**: 1.0.0 | **Ratified**: 2026-08-26 | **Last Amended**: 2026-08-26
