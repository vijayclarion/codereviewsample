# Contract: Posted Comment Format

**Feature**: `001-ai-code-review-agent` | **Date**: 2026-08-26

What the agent writes onto a pull request. This is the human-facing surface, and it doubles as the
agent's own state store (R5) — so the marker line is load-bearing, not decoration.

---

## The marker

Every comment the agent posts ends with a footer:

```
---
🤖 *AI code review* · `ai-review:{kind}:{fingerprint}`
```

The summary discussion uses `ai-review:summary` with no fingerprint.

It does three jobs at once:

1. **Attribution (FR-031)** — a reader can tell this from a human's comment at a glance.
2. **Self-identification (R5)** — the next run finds its own threads by searching for `ai-review:`.
3. **Deduplication (FR-030)** — the fingerprint says which finding a comment already covers.

Visible text rather than an HTML comment, because on-prem markdown rendering of `<!-- -->` is not
guaranteed to round-trip, and a state marker that a rendering change can silently eat would break
re-review dedupe without any visible symptom.

---

## Summary discussion

One per pull request, updated in place on each re-review (FR-023, FR-030).

```markdown
## 🤖 AI Code Review

**Reviewed**: PR #1234, iteration 7 · 5 of 6 changed files · story #101

### ⚠️ False coverage claim

The PR description lists **"Stock limit enforced when adding to cart"** as covered, but the diff
contains no stock-limit check in `CartController.AddItem`.

> Criterion 4 — *"The system must not allow adding more items than are in stock"* — status:
> `not_addressed`

### Story coverage — user story #101 "Add items to shopping cart"

| # | Acceptance criterion | Status | Evidence |
|---|---|---|---|
| 1 | Quantity must be a positive integer | ✅ appears addressed | `AddItem` rejects `quantity <= 0` with a 400 |
| 2 | Unknown product returns not-found | ✅ appears addressed | `_catalog.Find` null path returns `NotFound()` |
| 3 | Adding an existing line increments quantity | ✅ appears addressed | Existing-line branch increments rather than appending |
| 4 | Stock limit must not be exceeded | ⬜ not addressed | No stock or inventory check in the diff |
| 5 | Cart total recalculated after add | ✅ appears addressed | `cart.Recalculate()` called before save |

⬜ *Not addressed* is neutral — a pull request routinely implements only part of a story, and these
are not defects.

### Checklist violations (1)

- **Business rules from the story are enforced** — `code-review-checklist.md`
  `AddItem` does not enforce the stock limit that acceptance criterion 4 requires.
  → `src/eShop.Api/Controllers/CartController.cs:42`

### Code issues (0)

None found in the reviewed diff.

### Not sent for review

| File | Reason |
|---|---|
| `src/eShop.Api/appsettings.json` | excluded — matched `appsettings*.json` |

### Scoped out by the author

Work items #102 and #103 are listed as "Not in this PR" and were not reviewed against.

---
*This is an automated reviewer aid, not a merge gate. It reasons over diff text and cannot verify
runtime behaviour — a human decides the merge.*

---
🤖 *AI code review* · `ai-review:summary`
```

Sections with nothing to report still appear, with an explicit "none" — a missing section is
indistinguishable from a section the agent failed to produce, and on a review whose whole value is
"what did it not find", that ambiguity is costly.

### Degraded review

When the review could not complete (FR-029), the summary is replaced by:

```markdown
## 🤖 AI Code Review — could not complete

The review did not run to completion, so **no findings should be inferred from its silence**.

**Reason**: the AI provider was unreachable after 3 attempts (connection timeout).

Nothing about this pull request has been assessed. This build reports success by design — a review
outage does not gate a merge.

---
🤖 *AI code review* · `ai-review:summary`
```

---

## Inline comment

One per located finding (FR-024). Anchored right-side (R4).

```markdown
**⚠️ Possible contradiction with acceptance criterion 4**

The PR description claims stock checking is covered, but `AddItem` adds the requested quantity
without comparing it against available stock. Acceptance criterion 4 requires that the system not
allow adding more items than are in stock.

*Checklist*: "Business rules from the story are enforced" — `code-review-checklist.md`

---
🤖 *AI code review* · `ai-review:checklist:a1b2c3d4e5f6`
```

Headers by kind:

| Kind | Header | `{kind}` in marker |
|---|---|---|
| False coverage claim | `**⚠️ False coverage claim — criterion {n}**` | `false-coverage` |
| `possible_contradiction` | `**⚠️ Possible contradiction with acceptance criterion {n}**` | `coverage` |
| Checklist violation | `**📋 Checklist: {item}**` | `checklist` |
| `code_issue` high | `**🔴 Code issue (high)**` | `code` |
| `code_issue` medium | `**🟡 Code issue (medium)**` | `code` |
| `code_issue` low | `**🔵 Code issue (low)**` | `code` |

---

## Rules

- Every posted thread is created **active** and is never resolved or deleted by the agent (FR-024a).
- Exactly one summary discussion per pull request, ever (FR-023, FR-030).
- A finding whose fingerprint already appears on the PR gets **no** new inline comment (FR-030).
- A finding with no usable file+line goes in the summary, never dropped (FR-025).
- `not_addressed` never produces an inline comment (FR-016).
- No credential ever appears in posted content (FR-006).
- Preview mode prints this exact markdown to stdout and posts nothing (FR-026) — the renderer is the
  same code path in both modes, so the two cannot drift.
