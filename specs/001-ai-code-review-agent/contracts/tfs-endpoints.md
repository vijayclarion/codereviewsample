# Contract: TFS REST Surface

**Feature**: `001-ai-code-review-agent` | **Date**: 2026-08-26

Every TFS call the agent makes, and the response fields it depends on. `tfs_client.py` is the only
module that issues these (FR-032); everything else receives parsed data.

Shorthand: `{C}` = `TFS_BASE_URL`, `{P}` = `TFS_PROJECT`, `{R}` = `TFS_REPO`, `{id}` = `TFS_PR_ID`,
`{v}` = `TFS_API_VERSION` (default `6.0`). Every URL carries `api-version={v}`.

---

## Reads

### 1. Linked work items

```
GET {C}/{P}/_apis/git/repositories/{R}/pullRequests/{id}/workitems
```
Depends on: `value[].id`. An empty `value` is valid → checklist-only review (FR-008).

### 2. Work item detail

```
GET {C}/{P}/_apis/wit/workitems/{wid}?$expand=all
```
Depends on: `fields["System.Title"]`, `fields["System.Description"]`,
`fields["Microsoft.VSTS.Common.AcceptanceCriteria"]`, `fields["System.WorkItemType"]`,
`fields["System.State"]`, `fields["System.Tags"]`.

All text fields arrive as **HTML** and are stripped to plain text before use (FR-009). A missing
acceptance-criteria field is normal, not an error — criteria are then read from the description.

### 3. Pull request detail

```
GET {C}/{P}/_apis/git/repositories/{R}/pullRequests/{id}
```
Depends on: `title`, `description`, `sourceRefName`, `targetRefName`, `lastMergeTargetCommit.commitId`
(the R3 fallback base), `status`.

### 4. Iterations

```
GET {C}/{P}/_apis/git/repositories/{R}/pullRequests/{id}/iterations
```
Depends on: the **last** `value[]` entry — its `id`, `sourceRefCommit.commitId`, and
`commonRefCommit.commitId`.

`commonRefCommit` is the diff base. If it is absent, fall back to `lastMergeTargetCommit` from call 3,
then to per-file degradation (R3).

### 5. Iteration changes

```
GET {C}/{P}/_apis/git/repositories/{R}/pullRequests/{id}/iterations/{iid}/changes
```
Depends on: `changeEntries[].item.path`, `changeEntries[].changeType`,
`changeEntries[].item.isFolder`. Folders are dropped.

Returns change *entries*, **not** patch text — which is why the agent computes diffs itself (R3).

### 6. File content at a commit *(two per reviewed file)*

```
GET {C}/{P}/_apis/git/repositories/{R}/items
      ?path={filePath}
      &versionDescriptor.version={commitSha}
      &versionDescriptor.versionType=commit
      &includeContent=true
```
Depends on: `content` (text). A `404` at the base commit means the file was added — diff against empty.
A NUL byte in the content marks the file binary and it is skipped with a stated reason.

### 7. Checklist enumeration

```
GET {C}/{P}/_apis/git/repositories/{checklistRepo}/items
      ?scopePath={CHECKLIST_PATH}
      &recursionLevel=Full
      &versionDescriptor.version={CHECKLIST_BRANCH}
      &versionDescriptor.versionType=branch
```
Depends on: `value[].path`, `value[].isFolder`, `value[].gitObjectType`. Keep blobs whose filename
matches `CHECKLIST_FILE_PATTERN`; drop folders (FR-013).

Then **one content call per matching file**, using call 6's shape against the checklist repo. The
brief's Option A (`includeContent=true` on the recursive listing) is not relied on, because content in a
recursive listing is inconsistently populated across on-prem versions — enumerate-then-fetch works
everywhere for the handful of files involved.

Any failure here is a **degraded review**, not a setup failure: the coverage review still runs (FR-029).

### 8. Existing threads *(for incremental re-review)*

```
GET {C}/{P}/_apis/git/repositories/{R}/pullRequests/{id}/threads
```
Depends on: `value[].id`, `value[].comments[].id`, `value[].comments[].content`,
`value[].status`, `value[].isDeleted`. Deleted threads are ignored.

The agent's own threads are identified by the `ai-review:` marker in the first comment's content, not by
author id — the author differs between a manual PAT run and a pipeline run (R5).

---

## Writes *(skipped entirely in preview mode)*

### 9. Create a thread

```
POST {C}/{P}/_apis/git/repositories/{R}/pullRequests/{id}/threads
```

Summary thread:
```json
{ "comments": [{ "parentCommentId": 0, "content": "<markdown>", "commentType": "text" }],
  "status": "active" }
```

Inline thread (FR-024):
```json
{ "comments": [{ "parentCommentId": 0, "content": "<markdown>", "commentType": "text" }],
  "status": "active",
  "threadContext": {
    "filePath": "/src/eShop.Api/Controllers/CartController.cs",
    "rightFileStart": { "line": 42, "offset": 1 },
    "rightFileEnd":   { "line": 42, "offset": 1 }
  } }
```

`status` is always `active` (FR-024a). `filePath` is repo-relative with a leading `/`. Right-side lines
only (R4).

### 10. Update the summary comment in place

```
PATCH {C}/{P}/_apis/git/repositories/{R}/pullRequests/{id}/threads/{tid}/comments/{cid}
{ "content": "<markdown>" }
```
Keeps one summary discussion per PR across re-reviews (FR-030).

**⚠ verify** the build service identity may patch its own comment on the target server. If not,
`POST .../threads/{tid}/comments` to append in the same thread instead — decided once at implementation
time, not attempted at runtime (R5).

---

## Error mapping

| Response | Classification | Message names |
|---|---|---|
| `401`, `403` on a read | Setup failure (exit 2) | The auth mode in use and the permission needed |
| `403` on a write | Setup failure (exit 2) | **Contribute to pull requests** for the build service identity |
| `404` on the PR, repo, or project | Setup failure (exit 2) | Which of `TFS_REPO` / `TFS_PROJECT` / `TFS_PR_ID` to check |
| `400` naming api-version | Setup failure (exit 2) | `TFS_API_VERSION`, with `6.0 → 5.0 → 4.1` to try |
| `404` on a checklist path | Degraded (exit 0) | `CHECKLIST_PATH` and `CHECKLIST_BRANCH` as searched |
| `404` on a file at the base commit | Not an error | Treated as an added file |
| `429`, `502`, `503`, `504`, connection error | Retry ×3, backoff 1/2/4 s, honour `Retry-After` | Then: read → degraded; write → setup failure |
| Other `4xx` | No retry; setup failure | Status and the endpoint (never the body) |
| HTML response body where JSON expected | Setup failure (exit 2) | Almost always an auth redirect — names the auth mode |

That last row earns its place: an on-prem collection answers an unauthenticated request with a sign-in
**page**, so the naive failure is a JSON decode error rather than a 401. Detecting it and reporting
"authentication failed" is the difference between a one-minute fix and an hour.

Requests carry `Accept: application/json` and a `User-Agent` identifying the agent and version. Auth
headers are built per request and never logged (R8).

The item calls (6, 7, and the checklist content fetches) also send `$format=json`. Without it, an
on-prem collection asked for an item with `includeContent=true` may answer with the **raw file bytes**
rather than a JSON envelope, which turns a routine content fetch into a JSON decode error.
