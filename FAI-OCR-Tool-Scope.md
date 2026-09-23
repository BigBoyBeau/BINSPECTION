# Standalone OCR FAI Tool — Initial Scope & Direction

Status: scoping only, no code written yet. This document captures the direction agreed on before any implementation starts.

## Problem

BINSPECTION requires a live SolidWorks model/drawing to balloon and report against. First Article Inspection is also needed for parts where no SW model exists (purchased parts, vendor/supplier drawings, PDF-only drawings). SolidWorks Inspection has a standalone equivalent for this case that OCRs a drawing PDF directly. This new tool fills the same gap for BINSPECTION's workflow.

## Core workflow (what this tool actually is)

This is **not** an attempt to replicate BINSPECTION's automatic dimension-scan/auto-balloon pipeline against a PDF. Full-page auto-detection of dimensions from a scanned drawing is out of scope.

Instead:

1. User opens a drawing PDF.
2. User manually draws a rectangle over one dimension, note, or GD&T frame at a time.
3. That crop is OCR'd. OCR is expected to do most of the workload, but it will not always be right (symbols, GD&T glyphs, degraded scans) — the result is always hand-editable, never trusted blindly.
4. The (possibly corrected) item becomes a numbered balloon/report row.
5. The finished list feeds a report using the same real QMS `.xltx` template BINSPECTION already fills.

OCR engine: **Windows.Media.Ocr** — built into Windows 10/11, free, fully offline, no API keys or network dependency. Good enough for most text; not expected to reliably read GD&T glyphs or tolerance symbols, which is why manual tolerance entry (below) exists as the deliberate fallback, not an edge case.

## Data model implication (why this isn't a trivial reuse of `Characteristic`)

[BINSPECTION/Models/Characteristics.cs](BINSPECTION/Models/Characteristics.cs) does **not** store a dimension's nominal value or tolerance limits for a normal (non-GD&T) row. It stores `PersistentRefId`, a pointer back to the *live* SolidWorks dimension, and [BINSPECTION/Core/ReportGenerator.cs](BINSPECTION/Core/ReportGenerator.cs) reads the real Nominal/Upper Limit/Lower Limit off that live SW object at report-generation time — SolidWorks does the bilateral/unilateral/limit math internally. The only place actual numbers get cached directly on a `Characteristic` today is the GD&T-only fields (`GdtToleranceValue`, treated as a one-sided zone: Upper = value, Lower = 0).

The new tool has no live geometry to query, so its item record must store **Nominal, Upper Limit, and Lower Limit directly** — there is no `PersistentRefId` equivalent to defer to.

The good news: `ReportGenerator`'s actual SW coupling is minimal (~11 lines out of 976, all just pulling a handful of header fields like Customer/Report # from live SW custom properties — see the `RowField.UpperLimit`/`RowField.LowerLimit` handling around lines 67, 276-279, 354-357). The template-filling logic itself only ever needs three numbers per row (Nominal, Upper Limit, Lower Limit) regardless of where they came from.

## Shared code plan

- Extract the SW-free parts of `Characteristic`/[CharacteristicManager.cs](BINSPECTION/Core/CharacteristicManager.cs) and `ReportGenerator`'s template-filling logic into a shared library referenced by both BINSPECTION and the new app, so both always produce byte-identical report output against the same `.xltx` template.
- Introduce an interface (e.g. `IReportHeaderSource`) around the ~11 lines of direct SW calls in `ReportGenerator`: BINSPECTION's implementation reads live SW custom properties; the new app's implementation reads whatever the user typed in.
- The new app's item record extends/adapts the shared model to carry Nominal/Upper/Lower directly instead of a `PersistentRefId`.

## Tolerance editing

Kept basic for now — matching what SolidWorks/BINSPECTION already models, not inventing a new scheme:

- Bilateral (nominal ± one value)
- Unilateral (nominal + separate plus/minus values)
- Limit (Upper/Lower entered directly)
- Basic (nominal only, no tolerance)
- Reference / None
- GD&T one-sided zone (same convention as `GdtToleranceValue` today: Upper = value, Lower = 0)

OCR attempts to capture nominal + tolerance from the image first; the tolerance menu is the deliberate manual override/fallback when it can't — "OCR does most of the work, editing forces the rest onto the report."

### UI pattern (reuses an existing, proven convention)

Mirrors Method/Classification editing in [BalloonGridService.cs](BINSPECTION/Core/BalloonGridService.cs):

- **Per row**: a tolerance-type dropdown with adjoining numeric field(s) that change based on the selected type, persisting immediately on change — same convention as `UpdateAttributes` (lines 708-728).
- **Apply to Checked**: a bulk action mirroring `UpdateAttributesForRows` (lines 739-777) — writes Type + tolerance value(s) to every checked row at once. Bulk-apply deliberately **never touches Nominal**, since a blanket tolerance callout (e.g. "±.010 unless otherwise specified") commonly applies across dimensions with different nominal values; each row keeps whatever nominal OCR read or the user typed.

## Open / not yet decided

- PDF rendering library for displaying pages and cropping regions (candidates: PDFtoImage/Pdfium — no SW model to lean on here, unlike BINSPECTION).
- New WPF project scaffolding (sibling project in `BINSPECTION.sln` vs. separate solution) — not yet created.
- Shared library extraction — not yet started.
- Balloon numbering/grouping behavior for this tool (whether Group/Ungroup/Re-Number from Balloon Manager carry over) — not yet discussed.

This document reflects scope and direction only; no project files, shared library extraction, or UI code have been created yet.
