# Contract: Checklist document extraction

**Feature**: `002-local-manual-pr-review` | Grounded in the five real documents inspected on 2026-09-15
(research R4, R5).

This contract defines what a quality lead must do for their document to be read, and what the agent
guarantees in return. It is the interface between the checklist repository and the review.

---

## The promise to the quality lead

1. Add a `.xlsx` or `.docx` file to the checklist repository — nothing else (FR-015).
2. If the agent cannot use your document, it says so by name, with a reason, on every run (FR-018).
3. If it uses your document, every finding from it cites your rule identifier and your filename (FR-027).
4. The agent never edits your document.

---

## Spreadsheets (`.xlsx`)

### Sheet qualification

A sheet contributes rules only if a **header row** is found within its first **5 rows**. A header row is one
containing a cell whose normalised text matches a rule-text header. Sheets with no such row contribute
nothing and are listed in the report — this is the normal fate of `Document Control` and `lookups`, which
appear in all three real documents.

### Column roles (case-insensitive, whitespace-normalised)

| Role | Accepted headers | Required |
|---|---|---|
| rule text | `Checklist Item`, `Title`, `Requirement`, `Rule`, `Check` | **yes** |
| detail | `Description`, `Sub-Category`, `Details`, `Guidance` | no |
| identifier | `#`, `Item`, `ID`, `Ref`, `No` | no — derived when absent or empty |
| section | `Category`, `Area`, `Group` | no |
| severity | `Severity`, `Priority`, `Risk` | no |
| ignored | `Comply?`, `Comments`, `Status`, `Owner`, `Evidence` | — |

### Row rules

- A row contributes one `ChecklistItem` when its rule-text cell is non-empty after trimming.
- A row whose rule-text cell is empty is skipped silently — these are spacers and section breaks.
- Identifier: the identifier cell when populated; otherwise derived as
  `<document-stem>:<sheet>:<row>` with `derived_id = true`.
- Merged banner cells above the header row are not rules.

### Verified against the real corpus

| Document | Rule sheet | Header row | Identifier | Expected items |
|---|---|---|---|---|
| `API Integration Checklist v3.0.xlsx` | `API Integration Checklist` | 2 | `#`, populated | ~52 |
| `API Security Checklist_v3.0.xlsx` | `API Security Checklist` | 2 | `#`, populated | ~45 |
| `Non Functional Requirements Checklist v2.0.xlsx` | `Arch Quality Attributes` | 2 | `#` **empty** → derived | ~65 |

The third row of that table is why FR-017a was amended during planning: a mandatory identifier column would
reject a valid, in-use checklist.

---

## Word documents (`.docx`)

### Primary path — rule-identifier markers

Both real documents mark every rule with a stable identifier in a single-row table, and say so in their own
"How to Use This Guide" section.

- **Rule marker**: a table cell matching `^([A-Z]{2,4}(?:-[A-Z]{2,6}){1,2}-\d{1,3})\s+(.{3,})$`
  → `item_id`, `title`. Observed: `SEC-WEB-01  Prevention of Cross-Site Scripting (XSS)`,
  `DEV-SEC-01  Adopt a Secure Coding Standard`.
- **Severity**: an adjacent cell equal to `HIGH`, `MEDIUM`, or `LOW`.
- **Body**: paragraphs and table cells following the marker, until the next marker or the next `Heading1`,
  trimmed to 400 characters.
- **Section**: the enclosing `Heading1` / `Heading2`.
- **Skipped by heading**: `Contents`, `How to Use This Guide`, `Appendix *`.
- **Dropped as commentary**: blocks opening with `ℹ PROVENANCE`, `✖ EXPLOIT SCENARIO`, or `REMEDIATION`
  narrative — the rule is kept, the essay is not (research R6).

### Fallback path — tolerant scan

A document yielding zero rules above is re-read tolerantly: every `Heading2`, `ListParagraph`, and table row
becomes a candidate rule with identifier `<document-stem>:<n>` and `derived_id = true` (FR-017b). A document
that still yields nothing ends as `no_rules` and is named in the report.

### Verified against the real corpus

| Document | Paragraphs | Tables | Identifier style | Path |
|---|---|---|---|---|
| `Najm_Application_Security_Guidelines v1.0.docx` | 1,021 | 125 | `SEC-WEB-01` | primary |
| `Najm_Coding_Standards v1.1.docx` | 1,072 | 136 | `DEV-SEC-01` | primary |

---

## Combination

1. Items from all documents are concatenated in document-name order.
2. Items whose normalised titles match are merged; the survivor lists every source document (FR-020).
3. The rendered block is capped at `CHECKLIST_MAX_CHARS` (default 60,000). Items dropped at the cap are
   named in the report (research R6, Principle VII).
4. **Zero items across all documents is a setup failure** — the run stops (FR-019, research R11).

Measured on the real corpus (2026-09-15): 115,000 characters of raw document text reduce to **48,485
characters across 165 rules** - 96 with identifiers the documents supply, 69 derived. That is inside the
60,000 default and well outside the 40,000 the plan first guessed at.

---

## Failure taxonomy

| Condition | `ChecklistDocument.status` | Run outcome |
|---|---|---|
| Parsed, ≥ 1 rule | `read` | continues |
| Parsed, 0 rules | `no_rules` | continues; named in report |
| Corrupt zip / unreadable XML | `skipped` | continues; named with reason |
| Password-protected | `skipped` | continues; reason states encryption |
| Extension other than `.xlsx` / `.docx` | `skipped` | continues; reason states unsupported format |
| Checklist repo unreachable | — | **setup failure**, exit 2 (FR-014) |
| Every document unusable | — | **setup failure**, exit 2 (FR-019) |
