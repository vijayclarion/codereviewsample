# Phase 0 Research: AI Code Review Agent for On-Prem TFS

**Feature**: `001-ai-code-review-agent` | **Date**: 2026-08-26

Twelve decisions. Each resolves either a Technical Context unknown or a mechanism the spec requires
but does not prescribe. Items marked **⚠ verify** need a real-environment check during
implementation; the decision states what to do in either case, so none of them blocks starting.

---

## R1 — HTTP layer: raw `requests` for both TFS and every LLM provider

**Decision**: One `requests.Session` per external system. No provider SDKs (`anthropic`, `openai`).

**Rationale**: The brief allows SDKs but prefers minimal dependencies, and the deciding factor is the
self-hosted build agent: three runtime dependencies with no compiled extensions install reliably from
a private index, whereas provider SDKs pull transitive trees (`httpx`, `pydantic`, `tiktoken`) that
drift and can fail on a locked-down agent. `llm_adapter.py` needs one POST and one field lookup per
provider — an SDK buys nothing at that size. It also keeps FR-032/FR-033 honest: a new provider is
one `elif` branch, not a new dependency.

**Alternatives considered**: Provider SDKs (rejected: dependency weight, no benefit at this scale).
`urllib` from the stdlib (rejected: retry, connection pooling, and proxy handling would be hand-built,
and `requests` is needed for TFS anyway).

---

## R2 — Authentication: pipeline Bearer token preferred, PAT fallback

**Decision**: If `SYSTEM_ACCESSTOKEN` is present and non-empty, use
`Authorization: Bearer {token}`. Otherwise use `Authorization: Basic base64(":" + TFS_PAT)`. Never
merge the two. `config.py` fails at startup if both are absent or both are present-and-ambiguous
(both present is allowed — pipeline wins, and the choice is logged as a mode name, never a value).

**Rationale**: Matches the brief and makes the validation build require no stored credential at all.
The empty-string check matters: a pipeline that has *not* enabled "Allow scripts to access the OAuth
token" exports `SYSTEM_ACCESSTOKEN` as an empty string rather than leaving it unset, so a truthiness
check on presence alone would select Bearer with no token and fail with a confusing 203/302 instead of
a clear setup message.

**Alternatives considered**: PAT everywhere (rejected: stores a long-lived human credential in the
pipeline for no reason). Negotiate/NTLM via `requests-negotiate-sspi` (rejected: Windows-only, extra
dependency, and unnecessary — both supported modes work).

---

## R3 — Diff text: computed locally from blobs at two commits ⚠ verify

**Decision**: `GET .../pullRequests/{id}/iterations` for the latest iteration and its
`sourceRefCommit` / `commonRefCommit`. `GET .../iterations/{n}/changes` for the changed-path list.
Then, per non-excluded text file, fetch the blob at the iteration commit and at the common commit and
compute a unified diff with stdlib `difflib.unified_diff`. Build a diff-line → new-file-line map
while emitting, and carry it alongside the diff for comment anchoring.

**Rationale**: This is the plan's largest technical risk and the reason the module exists. The
`changes` endpoint returns change *entries* — path, `changeType`, object ids — not patch text; there
is no on-prem endpoint that reliably returns per-file unified diff across TFS versions. Computing
locally is version-independent, gives exact control over context lines (3) and truncation, and
produces the line map that `threadContext` anchoring needs (FR-024). Cost is two blob fetches per
changed file, which the review bound already caps at 40 files.

**⚠ verify**: that `commonRefCommit` is populated on the target server's iteration payload. If it is
not, fall back to the PR's `lastMergeTargetCommit`; if that is also absent, degrade to sending
whole-file content for added files and mark modified files unreviewable with a stated reason.

**Alternatives considered**: `GET .../diffs/commits?baseVersion=&targetVersion=` (rejected: returns
the same change-entry shape, not patch text). Cloning the repo on the agent and running `git diff`
(rejected: needs credential plumbing into git, a full fetch, and disk on the agent — heavy for a text
diff). Asking the model to reason over full file contents (rejected: destroys the line anchoring and
multiplies token cost).

---

## R4 — Comment anchoring: right-side lines only

**Decision**: Anchor every inline comment with `threadContext.filePath` plus
`rightFileStart`/`rightFileEnd` at the new-file line from R3's map. Findings on deleted lines, or whose
reported line falls outside the map, route to the summary discussion instead (FR-025).

**Rationale**: The spec's Assumptions already fix post-change lines as the anchor. A right-side anchor
is what a reviewer expects on a "this code is wrong" comment, and the model reports findings against
code it can see — the new content. Silently clamping an out-of-range line to a valid one would anchor
a comment to unrelated code, which is worse than putting it in the summary.

**Alternatives considered**: Left-side anchoring for deletions (rejected: the model rarely produces
actionable findings about removed code, and it doubles the mapping work). Clamping out-of-range lines
(rejected: misleading anchors).

---

## R5 — Incremental re-review: state recovered from the PR's own threads

**Decision**: Stateless. On each run, `GET .../pullRequests/{id}/threads`, keep threads whose first
comment contains the marker token `ai-review:` (see `contracts/pr-comment-format.md`), and read the
fingerprints out of them. The single thread marked `ai-review:summary` is the summary; its comment is
updated in place with `PATCH .../threads/{tid}/comments/{cid}`. Findings whose fingerprint already
appears get no new comment. Nothing the agent posted is ever deleted or resolved.

**Rationale**: FR-030 needs to know what was already said, and the pull request is the only place that
survives between builds — a build agent's working directory does not. Reading it back also self-heals:
if a run dies mid-post, the next run posts only what is genuinely missing. Identification keys on the
content marker rather than the comment's author id, because the author differs between a manual PAT run
(a human identity) and a pipeline run (the build service identity), and the same PR may see both.

**⚠ verify**: that the build service identity can `PATCH` its own comment on the target server. If it
cannot, append a new comment to the existing summary thread instead — same thread, so still "one summary
discussion per pull request", with the newest comment last. Decide this once at implementation time and
record it; do not try both at runtime.

**Alternatives considered**: A state file on the agent (rejected: hosted agents are ephemeral, and a
shared path across agents is worse). A tag or property on the PR (rejected: TFS PR properties are a
thinner, less inspectable surface than a comment a human can also read). Deleting and reposting each run
(rejected: destroys human replies, and the clarification chose incremental).

---

## R6 — Finding fingerprint: line-independent

**Decision**: `fp = sha256(f"{kind}|{path}|{normalize(text)}").hexdigest()[:12]`, where `kind` is one of
`coverage` / `checklist` / `code` / `false-coverage`, `path` is the repo-relative path lowercased with
forward slashes, and `normalize` collapses whitespace, lowercases, and strips trailing punctuation. The
line number is **excluded**. The fingerprint is rendered in the comment footer.

**Rationale**: Between iterations, an unrelated edit above a finding shifts its line, so a fingerprint
including the line would re-post the same finding on every push — exactly the noise FR-030 exists to
prevent. Excluding the line accepts a rarer, cheaper error instead: two genuinely distinct instances of
the same issue in one file collapse to one comment. Normalizing text absorbs the model's harmless
re-wordings of an identical finding across runs.

**Alternatives considered**: Include the line (rejected: re-posts on every push). Fuzzy text similarity
(rejected: a similarity threshold is an unpredictable knob, and stability matters more than precision).
Ask the model to emit stable ids (rejected: it cannot see its own prior output, and a model-chosen id is
not stable).

---

## R7 — TLS against a private CA: `truststore`

**Decision**: Add `truststore` and call `truststore.inject_into_ssl()` once at startup, before any
session is built. Never pass `verify=` to `requests`. Documented fallback for an agent where
`truststore` cannot be installed: set `REQUESTS_CA_BUNDLE` to the exported internal CA bundle.

**Rationale**: `requests` validates against `certifi`'s bundle, which by definition cannot contain
Najm's internal CA — so on `https://devops.najm.sa` an out-of-the-box `requests` call fails with
`SSLCertVerificationError`. The two ways out are disabling verification (forbidden by FR-034) or
validating against the OS store, which is what `truststore` does: it swaps in the platform verifier
(Windows CryptoAPI / macOS Security / OpenSSL default paths) with no C extension and no cert wrangling.
Since the CA is already trusted machine-wide for these hosts to work in a browser, this is also the
least-surprise behaviour.

**Alternatives considered**: `verify=False` (rejected: forbidden, and it defeats the point of an internal
PKI). `pip-system-certs` (rejected: patches at import time via a `.pth` hook — more magic, same effect).
`REQUESTS_CA_BUNDLE` only (kept as fallback: works, but is per-machine manual setup that breaks silently
when the CA rotates).

---

## R8 — Secret hygiene: a redaction filter over every output path

**Decision**: `config.py` registers every secret value it loaded (PAT, pipeline token, LLM key) in a
module-level redaction registry. A single `emit()` / `redact()` path wraps all logging and printing, and
replaces any registered value found in a string with `***`. Auth headers are constructed at request time
and never logged. A contract test asserts that each secret's literal value appears nowhere in captured
stdout/stderr for a full fixture run.

**Rationale**: FR-006 is a non-negotiable, and the realistic leak is not a deliberate `print(pat)` — it
is an exception message, a repr of a request object, or a debug dump that happens to contain a header or
a URL with an embedded token. Filtering at the single point where text leaves the process catches all
three, and the test makes the guarantee enforceable rather than aspirational. Registering values (not
variable names) means a secret leaking through an unexpected route is still caught.

**Alternatives considered**: Careful logging by convention (rejected: unenforceable, and one careless
`except Exception as e: print(e)` breaks it). A logging `Filter` only (rejected: misses direct `print`,
which the dry-run path uses heavily).

---

## R9 — Configuration surface: the brief's variables plus five

**Decision**: Implement the brief's table verbatim, and add the five values the clarifications made
necessary. Full table with defaults in `contracts/cli-contract.md`.

| New variable | Default | Why it exists |
|---|---|---|
| `REVIEW_EXCLUDE_PATTERNS` | built-in list (R10) | FR-014b — override the exclusion list |
| `REVIEW_EXCLUDE_MODE` | `extend` | Whether the value extends or replaces the built-in list |
| `REVIEW_MAX_FILES` | `40` | FR-014e — the review bound |
| `REVIEW_MAX_DIFF_LINES` | `3000` | FR-014e — the review bound |
| `LLM_MAX_CHUNK_LINES` | `1200` | FR-014 — when to split into another pass |

Two corrections to the brief's table, both needed for the code to work:

- **`CHECKLIST_REPO_URL` is a git URL, but the REST call needs a repository name or id.** Derive the
  name from the URL's final `_git/{name}` segment; accept an explicit `CHECKLIST_REPO` override for the
  case where name-derivation is wrong.
- **`CHECKLIST_BRANCH` defaults to `main` in the brief, but this collection integrates on `master`**
  (verified: `origin/HEAD → origin/master`). Keep the brief's `main` as the shipped default so the
  documented behaviour matches the brief, but `.env.example` sets `master` explicitly and the
  degraded-review message for "checklist not found" names the branch it looked on — so the failure is
  self-explaining rather than mysterious.

**Rationale**: Every added value corresponds to a specific clarified requirement, so none is speculative
configuration. Defaults are chosen so the demo PR needs none of them set.

---

## R10 — Default exclusion list

**Decision**: Path/name globs, matched case-insensitively against the repo-relative path.

*Secret-bearing*: `.env`, `.env.*`, `*.pem`, `*.key`, `*.pfx`, `*.p12`, `*.cer`, `*.crt`, `*.jks`,
`*.keystore`, `*.snk`, `id_rsa*`, `*secrets*.json`, `*.publishsettings`, `appsettings*.json`
*Machine-generated / noise*: `package-lock.json`, `yarn.lock`, `pnpm-lock.yaml`, `poetry.lock`,
`Pipfile.lock`, `*.min.js`, `*.min.css`, `*.designer.cs`, `*.g.cs`, `*.generated.*`, `**/Migrations/**`,
`**/bin/**`, `**/obj/**`, `**/node_modules/**`, `**/dist/**`, `**/packages/**`
*Binary*: `*.dll`, `*.exe`, `*.pdb`, `*.zip`, `*.png`, `*.jpg`, `*.jpeg`, `*.gif`, `*.ico`, `*.pdf`

**Rationale**: Two distinct jobs in one list — keeping an accidentally-committed credential out of an
outbound request, and not spending tokens on generated text. Path matching is the whole mechanism, which
is why the spec already records that this is a safety net and not secret scanning.

**The debatable entry is `appsettings*.json`**, and it is called out deliberately: it is the most likely
place for a connection string in a .NET repo, and also a file some teams genuinely want reviewed. It is
excluded by default (the safer error) and removable with `REVIEW_EXCLUDE_MODE=replace`. Any team whose
checklist has rules about configuration should remove it.

**Alternatives considered**: Content-based secret detection (rejected: out of scope per the spec, and a
regex-based detector's false negatives would create false confidence). No exclusions (rejected: the
clarification chose filtering).

---

## R11 — Review bound: relevance-ordered, transparently truncated

**Decision**: After exclusions, score each remaining file and take files in descending score until either
`REVIEW_MAX_FILES` or `REVIEW_MAX_DIFF_LINES` is reached. Score = keyword overlap between the file path
and the story title + acceptance criteria (weight 3) + overlap with checklist keywords (weight 2) + a
source-code-extension bonus (weight 1); ties broken by smaller change size first, so the bound covers
more files. Every file not taken is listed in the summary under "not reviewed (bound reached)" with its
change size.

**Rationale**: FR-014f requires the *most relevant* portion rather than an arbitrary slice, and relevance
here means "related to what we are checking against" — the story and the checklist. Smallest-first
tie-breaking maximises the number of files covered, which matters because an unreviewed file is a silent
gap. Listing the remainder is what keeps the truncation honest (SC-011).

**Alternatives considered**: Alphabetical or API-order truncation (rejected: arbitrary). Largest-first
(rejected: one giant generated file would consume the whole bound). Reviewing everything (rejected: the
clarification chose a bound).

---

## R12 — Chunking, retries, and observability

**Decision**:

- **Chunking**: estimate prompt size as `chars / 4`; if a chunk's diff exceeds `LLM_MAX_CHUNK_LINES`,
  split on file boundaries, never mid-file, unless a single file exceeds the limit — then split on hunk
  boundaries. Story, combined checklist, and PR description accompany every chunk (FR-014). Results merge
  by union, deduped on R6 fingerprints.
- **Transient retries**: 3 attempts total on connection errors and HTTP 429/502/503/504, backoff
  1 s → 2 s → 4 s, honouring `Retry-After` when present. Applies to TFS and provider calls alike. `4xx`
  other than 429 never retries. This is separate from, and composes with, FR-022's single
  response-format retry.
- **Observability**: one structured line per phase to stdout — `phase`, `outcome`, `duration_ms`, and
  counts (work items, checklist files, files changed / reviewed / excluded / unreviewed, findings by
  kind, comments posted / suppressed). A run id derived from `{pr_id}.{iteration_id}` ties the lines
  together. Everything passes through R8's redaction. No secret, no request/response bodies, no header
  values.

**Rationale**: These were the three items `/speckit-clarify` deferred to planning, and each has an
obvious right answer that only needed writing down. File-boundary chunking preserves the context the
model needs to judge a file; the counts are exactly what makes SC-002, SC-006, SC-008, and SC-011
verifiable from a build log; the retry set is the standard transient class, and excluding non-429 `4xx`
prevents retrying a genuine permission or version error four times before reporting it.

**Alternatives considered**: Token-accurate counting via `tiktoken` (rejected: a dependency and wrong for
non-OpenAI providers; `chars/4` with a safety margin is adequate for chunk sizing). Unlimited retries
(rejected: turns a provider outage into a build timeout instead of a clean degraded review). Verbose
request logging (rejected: the fastest way to leak a token).

---

## R13 — Matching a declared-scope claim to a criterion: distinctive tokens, not overlap

**Recorded during implementation, after the first approach failed.**

**Decision**: Match each "Covered" line to an acceptance criterion by summing, over their shared tokens,
a weight of `log(1 + N/df)` — where `df` is how many criteria contain that token. Require the winner to
clear an absolute floor (`MIN_DISTINCTIVE_WEIGHT = 1.0`) **and** to beat the runner-up by
`RUNNER_UP_RATIO = 1.4`. Anything else is reported as an unmatched claim.

**Rationale**: the first implementation used plain token overlap as a fraction of the shorter side, with
a 0.5 threshold. It looked reasonable and it did not work. On the reference scenario, "Stock limit
enforced when adding to cart" against criterion 4 "The system must not allow adding more items than are
available in stock" shares exactly one content word after stopword removal — 0.25 against a 0.5
threshold. **The feature's headline finding was silently missed**, and because the coverage map itself was
correct, nothing else looked wrong.

The lesson generalises: a terse description line and a formally-worded acceptance criterion are not
paraphrases of each other, so counting shared words measures the wrong thing. What identifies a criterion
is a *distinctive* term. `stock` occurs in one of five criteria and is decisive alone; `cart` occurs in
three and identifies nothing. IDF weighting encodes exactly that, needs no corpus beyond the criteria
already in hand, and stays stable as the wording changes.

The runner-up margin is the second half of the decision and matters as much: without it, a one-word claim
like "cart" would match whichever criterion happened to score highest, and the headline finding would be
attached to the wrong criterion. Reporting "claimed but not matched" is the honest answer there — a
reviewer can act on it; a confidently mislabelled criterion misleads them.

**Cost accepted**: a claim whose wording shares no distinctive term with its criterion is reported as
unmatched rather than as a false claim. That is visible in the summary and recoverable by a human,
whereas the opposite error is not.

**Alternatives considered**: lowering the overlap threshold to ~0.25 (rejected: the same blunt metric,
now also matching unrelated pairs — it trades a false negative for false positives on the loudest finding
the tool produces). Stemming and synonym expansion (rejected: more machinery, still the wrong metric).
Asking the model to emit the false-coverage list itself (rejected in the original design and still
rejected — it invites the model to contradict its own coverage array, and the point of deriving is that
the same coverage map always yields the same headline). Positional pairing of the Covered list against
the criteria list (rejected: authors do not reliably write them in the same order).

**Where it lives**: `parser.match_claim`, used by `derive_false_coverage_claims` and by `_best_location`
(which borrows a file and line from a checklist violation about the same criterion — it had the same bug,
from the same cause, and got the same fix). Regression-tested in `tests/unit/test_parser.py`
(`test_terse_claims_match_their_criterion`, `test_ambiguous_claim_matches_nothing_rather_than_guessing`)
and end to end in `tests/integration/test_us1_review.py`.

---

## Residual risks

| Risk | Impact | Handling |
|---|---|---|
| Self-hosted build agent's Python version and package-index access are unverified | Blocks F1 entirely | First pipeline task is a probe step: print `python --version` and `pip --version`, then `pip install -r review_agent/requirements.txt`. Fail loudly with a setup message. Fallback: commit vendored wheels under `review_agent/vendor/`. |
| Target server's API version support (`6.0` assumed) | All TFS calls fail | `TFS_API_VERSION` is configurable; `tfs_client` maps a version-rejection response to a `SetupFailure` naming the variable to change. Try `6.0`, then `5.0`, then `4.1`. |
| `commonRefCommit` absent from the iteration payload (R3) | No diff for modified files | Fallback chain defined in R3. |
| Build identity cannot `PATCH` its own comment (R5) | Summary cannot update in place | Fallback to appending in the same thread, defined in R5. |
| Model quality — a false coverage claim missed, or a spurious one raised | SC-001 fails | Prompt states the rules verbatim; the demo scenario is the regression test; the reviewer is a human. Not a bug the design can eliminate. |
| The eShop demo assets do not exist yet | SC-001 unverifiable | Workstream E builds them. |
