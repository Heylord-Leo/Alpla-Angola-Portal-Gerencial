# Primavera PO Parser — Letter-Suffixed Year Series

Status: **BACKLOG — not implemented.** Recorded 2026-08-21 at the close of the historical
PO/supplier integrity repair campaign
([HISTORICAL-PO-SUPPLIER-INTEGRITY-REPAIR-2026-08.md](HISTORICAL-PO-SUPPLIER-INTEGRITY-REPAIR-2026-08.md)).
No application code was changed when this item was filed.

## Problem

The current deterministic grammar in `PrimaveraPoReference` (v2.229.12) accepts:

```
\b(ECF11|ECF10|ECF)\b[\s.:]*(20\d{2})\s*[/\-]\s*(\d{1,6})\b
```

The year token `20\d{2}` fails legitimate Primavera references that use a **letter-suffixed
year-series token**.

Confirmed real example (REQ-23/07/2026-146, human-reviewed PO PDF heading
"Encomenda Mat Escritório/Diversos ECF10 2026A/11"):

```
ECF10 2026A/11
```

Expected behavior:

- display = `ECF10 2026A/11`
- canonical = `ECF10-2026A-11`

Because the parser cannot read the `2026A` series, that PO was originally registered
family-dropped as `2026A/11` and required a manual historical correction
(`[HIST-PO-REQ-146]`). The series is genuine Primavera output — `ECF10 2026A/13` is stored on
REQ-134, and `FT 2026A9/55` / `FP.2026A/257` exist as document numbers.

## Proposed grammar

```
\b(ECF11|ECF10|ECF)\b[\s.:]*(20\d{2}[A-Z]?)\s*[/\-]\s*(\d{1,6})\b
```

The year token only widens (`[A-Z]?`), so every currently-accepted reference parses
identically.

## Requirements for the future implementation

- Preserve all current ECF / ECF10 / ECF11 parsing and canonical behavior byte-for-byte for
  suffix-less years.
- `2026` and `2026A` must remain **canonically distinct**; no normalization may strip the
  suffix.
- Duplicate detection must operate on the canonical forms — verify that
  `ECF10 2026/11` ≠ `ECF10 2026A/11` (canonicals `ECF10-2026-11` vs `ECF10-2026A-11`) never
  collide, in both duplicate-guard directions.
- Add parser unit tests (`PrimaveraPoReferenceTests`) for the letter-suffixed series,
  including the real `ECF10 2026A/11` case and the repository-documented `ECF10 2026A/13`
  (REQ-134) as fixtures.
- Add duplicate/collision tests covering same-company and cross-company behavior for
  suffixed vs unsuffixed canonicals.
- Add an OCR prompt hint mentioning the letter-suffixed year series so extraction surfaces
  the full reference.

## Out of scope

Do not implement as part of the historical repair campaign — data corrections were handled
manually and are complete; this item is purely a forward-looking application change for a
future release cycle.
