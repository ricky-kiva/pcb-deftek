# Requirements & Context: Phase 9 — Audit-Ready PDF Export Engine

## 1. Executive Summary & Context

Phase 8 shipped the Defect Statistics Dashboard with a deliberate, non-functional export seam and a Background Activity modal that only reports progress. Phase 9 turns that seam into the product's audit deliverable: a QuestPDF-generated report containing the four dashboard metric cards (single column) followed by annotated PCB inspection images with their defect metadata, generated asynchronously, written to the configured report directory, and announced with a Windows toast notification.

The phase also completes the Background Activity surface: every running process can be cancelled, the modal and toolbar are revised to communicate process state cleanly, large exports are confirmed before they start, unreadable inspection records are removed from both the report and History, and an export with no matching inspections produces a notification instead of an empty document.

The report must be a faithful, reproducible snapshot of the dashboard's active filter: it reuses the Phase 8 aggregate query and image corpus rather than re-implementing metric or filter semantics.

### Current State (verified in code)

| Concern | Existing behavior |
|---|---|
| Export trigger | `Presentation/Views/DashboardView.xaml` "Export" button (text only) → `DashboardViewModel.RequestExportCommand` |
| Export seam | `DashboardViewModel.ExportRequested` (`EventHandler<DashboardFilter>`) is raised with an immutable filter snapshot; **no subscriber exists** |
| Background Activity modal | `Alerts` button opens `NotificationModalOverlay`; rows show a 4 px `ProgressBar`, `Status`, and `Message` text per process |
| Duplicate idle state | `DashboardProgressState` defaults `Message` to the status name, so an idle row renders `Idle` twice; the 4 px bar also sits between the title and status |
| Cancellation | `IDashboardProgressService` exposes progress reporting only; there is no cancellation token, cancel command, or cancelled state |
| Aggregates | `IDashboardRepository.GetStatisticsAsync(DashboardFilter)` → `DashboardStatistics` (adaptive buckets, class counts, totals) |
| Inspection evidence | `IInspectionRepository.GetByIdAsync(Guid)` loads `InspectionRecord` plus all `DetectedDefect` rows; images are referenced by `ImagePath` |
| Annotation | `IImageAnnotationService.AnnotateImage(path, defects, maxDimension)` returns a frozen WPF `BitmapSource`, or `null` when the file is missing/corrupt |
| History invalid image | `HistoryThumbnailService` returns `null`; `HistoryItemViewModel` sets `IsImageUnavailable` and the card renders a placeholder; the record stays in the database |
| Deletion | `IInspectionDeletionService` deletes records transactionally and removes only app-owned capture files; user-owned originals are never touched |
| Confirmations | `IConfirmationService.ConfirmAsync(title, message, confirmText)` presents a Yes/No dialog that defaults to cancel |
| Report path | `KnownFolderPaths.GetReportExportDirectory()` → `%USERPROFILE%\Downloads\PCB Deftek\Report`; `UserSettings.ReportExportPath` defaults to it |
| Progress | `IDashboardProgressService.ReportDataExport(status, percentage, message)`; the dashboard modal binds both inference and data-export states |
| Notifications | `INotificationService.ShowToastAsync(title, message)` |
| DB schema | `Inspections`, `Defects`, `AppSettings`, `SchemaMigrations`; no Phase 9 schema need |
| PDF dependency | **QuestPDF is not referenced** in `pcb-deftek.csproj` |

## 2. Scope

### 2.1 In Scope

1. **Report contract and content model**
   - Immutable report model carrying: report title, generated local timestamp, resolved filter summary, metric summaries, chart series, and evidence entries.
   - Deterministic ordering and formatting so two exports of the same filter render the same content (apart from the timestamp and file name).

2. **PDF document composition (QuestPDF)**
   - A4 portrait, single-column metric section, evidence section, page header/footer with page numbering.
   - Four metric blocks in fixed canonical order, each with title, value, caption and chart: PCBs Inspected, Defects Detected, Inspection Rate (dual PASS/FAIL), Defect Distribution (all nine classes).
   - Evidence blocks for every valid inspection matching the filter: annotated image (max 40 % of the content width) and a defect table without an abbreviation column.

3. **Filtered evidence retrieval**
   - A dashboard-scoped query returning the inspections selected by the same date/class/severity predicate used by `GetStatisticsAsync`, newest first, with defects loaded.
   - A matching count query used before starting an export.

4. **Image annotation, validation and staging**
   - Annotation from the inspection's raw `ImagePath` via `IImageAnnotationService`, at a bounded pixel dimension.
   - PNG staging on disk for deterministic QuestPDF page composition and bounded memory.
   - Missing/unreadable/corrupt images are not rendered; the owning inspection record is deleted from the database (same behavior on the History page).

5. **Export orchestration**
   - Resolve the destination directory, create it on demand, write atomically, report progress, notify on success/failure, clean up all temporary artifacts.
   - Default destination is `Downloads\PCB Deftek\Report`.
   - No matching inspections → no PDF; show a Windows toast instead.
   - More than 100 matching inspections → confirm with `IConfirmationService` before starting.
   - Cancellation through the Background Activity modal and on application exit.

6. **Dashboard integration**
   - Replace the Phase 8 event seam with a real async export command backed by `IDashboardExportService`.
   - Prevent concurrent exports; auto-open the progress modal when an export starts.

7. **Background Activity revisions**
   - Per-process Cancel buttons for report generation, inference, and data export.
   - Rename `Alerts` to `Processes` with an activity icon and an ongoing-process notification dot.
   - Export button gains an export icon before its text.
   - One status line per process; no duplicate `Idle`; no separator-like progress track in the idle state.

8. **Invalid-record cleanup on History**
   - When a gallery thumbnail cannot be decoded because the image is missing or corrupt, remove the record using the existing deletion service and refresh the gallery.

9. **Verification**
   - Unit tests for process cancellation, modal chrome, content projection, SVG charts, path/filename policy, staging cleanup, invalid-record cleanup, progress and failure behavior.
   - SQLite integration tests for the filtered evidence/count queries and parity with dashboard totals.
   - A real PDF smoke test into a temp directory.
   - Reqnroll BDD scenarios for success, no-match, large-export confirmation/cancel, modal cancellation, and invalid-record cleanup, using isolated database and temp paths.

### 2.2 Out of Scope (Deferred)

- CSV/Excel export, scheduled or batch exports, e-mail delivery.
- Digital signatures, PDF/A conformance, encryption, or tamper-evident hashing.
- Report templates, localization, branding assets/logo (official branding is a Future Enhancement).
- Exporting the annotated images as separate files beside the PDF.
- An evidence-page cap; large exports are warned and cancellable instead.
- Chart drill-down or interactive elements inside the PDF.
- Any change to the defect taxonomy, knowledge base content, or dashboard visual design beyond the revisions listed above.

## 3. Decisions & Rationale

| # | Decision | Rationale |
|---|---|---|
| D1 | Use `QuestPDF` **2026.9.2** under `LicenseType.Community`. | License resolved: the project is non-profit, which qualifies for Community. QuestPDF is the dependency named in `tech-stack.md`. |
| D2 | QuestPDF is the only new NuGet package; no ScottPlot or WPF-rendering bridge. | Charts are simple line/bar series emitted as SVG strings; a second chart package is unnecessary. |
| D3 | Split the engine into: pure content factory → pure SVG chart renderer → QuestPDF document builder → orchestrating export service. | Keeps document layout testable without PDF bytes, chart geometry testable without QuestPDF, and orchestration testable with fakes. |
| D4 | The exporter takes only a `DashboardFilter` snapshot and re-queries statistics + evidence itself. | The report is reproducible from its filter, independent of transient dashboard view state, and cannot drift from the dashboard's metric definitions. |
| D5 | Add `IDashboardRepository.GetMatchingInspectionsAsync(DashboardFilter)` returning `InspectionRecord` aggregates (with defects), newest first, and `GetMatchingInspectionCountAsync(DashboardFilter)` for preflight. | Severity is not a SQL column and is mapped through `DefectKnowledgeBase`; reusing the dashboard's predicate helpers guarantees filter parity and avoids N+1 lookups. |
| D6 | Annotate from the raw `ImagePath` with `IImageAnnotationService`; never read or modify the app-owned `_annotated.png` sidecar, and never write to the source image. | One annotation implementation and color source (`DefectClassInfo.ColorHex`) for live view, viewer, and report; user-owned files stay untouched. |
| D7 | Encode annotated images to PNG in a temporary staging folder, one image at a time, then compose the PDF from staged files; delete the staging folder in `finally`. | Bounds memory for large exports, makes progress determinate, and lets QuestPDF read images lazily. |
| D8 | Include every valid inspection matching the filter, newest first, with no truncation. If the matching count exceeds **100**, ask for confirmation before starting; the operator may cancel from the Background Activity modal. | 100 is the measured performance-gate corpus; audit reports must not silently omit evidence. |
| D9 | Destination directory: `UserSettings.ReportExportPath`; empty/whitespace falls back to `KnownFolderPaths.GetReportExportDirectory()` — `%USERPROFILE%\Downloads\PCB Deftek\Report`. File name `PCB-Deftek-Report-yyyyMMdd-HHmmss.pdf` (local time), with `-2`, `-3`, … suffixes on collision. | Matches the latest path agreement and Phase 7 decision D8; timestamped names avoid overwriting audit artifacts. |
| D10 | Generate into a sibling `.tmp` file and move it into place only after successful generation; delete the temp file and staging folder on failure or cancellation. | No partial or corrupt PDFs and no misleading success toast. |
| D11 | Replace the Phase 8 `ExportRequested` event with an injected `IDashboardExportService`; delete the event and its event-specific tests. | Phase 8's seam existed because the engine did not; keeping it now would add an indirection with no consumer value. |
| D12 | `IDashboardProgressService` owns one cancellation token per process (`Inference`, `DataExport`), exposes `Cancel(process)`, `CancelAll()`, and `HasActiveProcesses`; add `DashboardProgressStatus.Cancelled`. Report generation consumes the `DataExport` token. | One cancellation model covers report generation, inference, and data export without per-feature plumbing. |
| D13 | Export progress: 0 % preparing, 10 % aggregates loaded, 15 % evidence list resolved, 15→95 % per staged image, 100 % completed. Progress flows through `ReportDataExport`; the modal opens automatically when an export starts. | Uses the existing progress surface; auto-open gives immediate feedback without adding new UI. |
| D14 | Failures are logged (Serilog), reported as `Failed` with a user-safe message, and surfaced by a toast; no exception escapes to the dispatcher. Cancellation reports `Cancelled`, cleans up, and shows no success toast. | Consistent with the app's centralized error policy. |
| D15 | Report card order is fixed (PCBs, Defects, Inspection Rate, Distribution) regardless of the dashboard's drag order or 1/2-column layout. | An audit document needs a stable, predictable structure. |
| D16 | Report renders defect metadata from `DefectKnowledgeBase` (display name, severity) and confidences as whole percentages. The defect table has three columns: **Class, Confidence, Severity** — no abbreviation column. | Single source of truth for taxonomy; removes redundant information. |
| D17 | Annotated evidence images use at most **40 %** of the report content width (page width minus margins), left-aligned; aspect ratio preserved. | Keeps the image as evidence without dominating the page. |
| D18 | Missing/unreadable/corrupt images are not rendered. The owning inspection record is deleted through `IInspectionDeletionService`; the inspection is excluded from the evidence section. The same deletion behavior applies when the History gallery cannot decode a thumbnail, where the card is removed and a single toast reports how many records were cleaned up. | Explicit product decision: unrenderable records are repaired by cleanup instead of leaving broken entries. User-owned image files are never modified. Deletion is attempted only after a decode/missing check and one retry; environmental failures (parent directory inaccessible, e.g. disconnected volume) are logged and skipped for that run to avoid mass deletion. |
| D19 | No matching inspections → **no PDF**. The operator receives a Windows toast (`No matching inspections found`); progress is not started and no modal is opened. | An empty audit report has no value; the user needs actionable feedback. |
| D20 | Matching count > 100 → confirmation via `IConfirmationService` with the count and a warning that the export may take a while; declining aborts before any file or progress work. | Protects against accidental multi-minute exports; threshold matches the measured corpus. |
| D21 | Modal chrome: one status line per process (message shown only when it adds information), progress track hidden while `Idle`, no separator between title and status; `Alerts` renamed `Processes` with a new activity icon and a notification dot while any process runs; Export gains its `IconExport` icon. | Removes duplicated `Idle` text and the incidental separator-line reading; surfaces ongoing activity at a glance. |
| D22 | Application exit cancels all processes (`CancelAll`) and the export service's `Dispose` removes temp/staging artifacts with a bounded best-effort wait. | Prevents orphaned temp files and half-written reports when the app closes mid-export. |
| D23 | No database migration, no new settings keys; `UserSettings.ReportExportPath` keeps the `KnownFolderPaths.GetReportExportDirectory()` default. | The feature writes files only; the setting already resolves correctly. |
| D24 | Tests use real SQLite (existing migration runner) and real QuestPDF generation into temp folders; only notifications, confirmations, and time are stubbed. | Proves the deliverable (a readable PDF) rather than asserting mock echo. |

## 4. Report Content Contract

Page setup: A4 portrait, 18 mm margins, default font size 9–10 pt, footer on every page: `PCB Deftek · Generated {yyyy-MM-dd HH:mm}` left, `Page {current} of {total}` right.

### 4.1 Header (first page)

- Title: `Defect Inspection Report`.
- Subtitle: `PCB Deftek`.
- Generated timestamp (local).
- Filter summary block:
  - Date range: `All time` or `{local start:yyyy-MM-dd} – {local end:yyyy-MM-dd}` (inclusive).
  - Defect classes: `All nine classes` or the selected display names in taxonomy order.
  - Severities: `All severities` or the selected severity labels.

### 4.2 Metric section (single column, fixed order)

Each block contains the card title, primary value, caption, and chart. Values use `CurrentCulture` thousands separators, matching the dashboard.

| Order | Block | Primary value | Chart |
|---|---|---|---|
| 1 | PCBs Inspected | `Statistics.TotalInspections` | Single-series line over adaptive buckets |
| 2 | Defects Detected | `Statistics.TotalDefects` | Single-series line over the same buckets |
| 3 | Inspection Rate | `{Pass} PASS / {Fail} FAIL` | Dual line: PASS (green `#107C41`), FAIL (red `#C42B1C`) |
| 4 | Defect Distribution | total matching defects | Vertical bars for all nine classes in `ClassId` order, abbreviated labels, `DefectClassInfo.ColorHex` fills |

- Caption: `{granularity} · {bucketCount} buckets` for the first three blocks; `All nine defect classes` for distribution.
- X-axis labels are thinned by a deterministic step so labels never overlap; the SVG uses a fixed viewBox and scales to the available width.
- Charts are rendered only when the report is generated (never for a no-match request).

### 4.3 Evidence section

Starts on a new page. For each valid matching inspection (newest first):

- Heading: inspection `Name`.
- Metadata line: local timestamp, `Mode` (`Single file` / `Batch folder` / `Live camera`), `PASS`/`FAIL`, defect count.
- Annotated image at **maximum 40 % of the content width**, left-aligned, aspect ratio preserved.
- Defect table with columns: **Class**, **Confidence**, **Severity**. One row per defect in descending confidence, then class order.
- PASS inspections (zero defects) show the image and the metadata line only.
- Inspections whose image is missing or cannot be decoded are removed from the report and the database (see D18); they are not listed with a placeholder.
- Metric totals remain those of the filtered aggregate captured at export start; invalid records are removed only from the evidence section. If any record was removed, the dashboard reloads its statistics after the export completes.

### 4.4 No matching inspections

The report is not generated. The dashboard shows a Windows toast: `No matching inspections found. Adjust the filters and try again.` No progress entry is started and no modal is opened.

## 5. Background Activity Contract

### 5.1 Process rows

- Two rows exist: `Inference` and `Data export`; each row carries its own Cancel button, visible/enabled only while that process is `Running`.
- Report generation is the `Data export` process; no third row is added.
- The `Inference` row is fed by batch detection; live camera inference is continuous and is not treated as a cancellable background process.
- Each row displays exactly one status line. The status is `Idle`, `Running`, `Completed`, `Failed`, or `Cancelled`; the optional message appears only when it adds information and never repeats the status word.
- The 4 px progress track is rendered only when the process is not `Idle`; the idle row must not read as a separator between the title and the status.
- Cancelling sets the process to `Cancelled`, cancels its token, and leaves the row visible with the cancelled status.

### 5.2 Toolbar

- `Processes` button (renamed from `Alerts`) uses a new activity icon and keeps its tooltip/automation name aligned with the new label.
- A small notification dot overlays the Processes icon whenever `HasActiveProcesses` is true (any process `Running`).
- The `Export` button renders the `IconExport` glyph before its text.

### 5.3 Export lifecycle

1. `RequestExportCommand` validates the filter (`DashboardFilterViewModel.TryCreateFilter`); invalid custom date ranges do nothing (existing behavior).
2. Preflight: matching count = 0 → toast, no report, no progress. Count > 100 → confirmation; declining aborts.
3. Export command is disabled while an export runs; a second invocation is ignored.
4. Progress modal opens; `DataExport` enters `Running` using the service's DataExport token.
5. Exporter loads statistics, resolves matching inspections, stages annotated images (invalid records are deleted and excluded), composes the PDF off the UI thread.
6. On success: atomic move, `Completed` at 100 % with the saved path, toast `Report exported` + path, staging cleanup.
7. On cancellation (modal Cancel or application exit): `Cancelled`, temp/staging cleanup, no completion toast.
8. On failure: cleanup, `Failed` with a user-safe message, toast `Report export failed`, Serilog error with exception.

## 6. Technical Constraints

- PDF generation, image annotation, PNG encoding, and file I/O run off the UI thread; no synchronous work on the dispatcher.
- WPF `BitmapSource` values crossing threads must be frozen (annotation service already guarantees this).
- No hardcoded absolute user paths; all destinations derive from settings or `KnownFolderPaths`.
- No writes outside the resolved report directory and the OS temp directory; user-owned source images are read-only.
- Record deletion goes through `IInspectionDeletionService`; only database rows and app-owned capture files are removed.
- Nullable reference types enabled; 0 warnings.
- QuestPDF types stay inside `Infrastructure/Export`; ViewModels depend only on `IDashboardExportService` and `IDashboardProgressService`.
- The export service reports intermediate progress through `IProgress<DashboardExportProgress>` and holds no Presentation types; the view model forwards those updates to `IDashboardProgressService.ReportDataExport`.

## 7. Edge Cases

| Case | Expected behavior |
|---|---|
| No inspections match | No PDF; toast; no progress/modal |
| Matching count exactly 100 | No warning; export starts |
| Matching count 101+ | Confirmation required; declining produces no file/progress |
| 1 bucket / 1 class / one point | Chart axes remain valid; no divide-by-zero or inverted coordinates |
| All-time range with inspections years apart | Adaptive monthly/weekly buckets from `DashboardStatistics` are preserved |
| Severity filter active | Evidence/count queries and metric query select identical inspections (parity test) |
| PASS inspections with class/severity filters | Excluded from totals per Phase 8 D5; absent from evidence |
| Image missing/unreadable/corrupt (report) | Image not rendered; record deleted; inspection excluded; export continues |
| Image missing/unreadable/corrupt (History) | No placeholder; record deleted; card removed; gallery counts refreshed |
| Parent directory inaccessible (e.g. disconnected volume) | Deletion skipped and logged; no mass cleanup of unrelated records |
| Report directory missing | Created; export succeeds |
| Report directory unwritable | `Failed` state + toast; no partial file; staging cleaned |
| File name collision within the same second | `-2`, `-3`, … suffix; previous files untouched |
| Cancel from modal | `Cancelled`; no PDF; staging/temp removed |
| Application closed mid-export | Export cancelled; best-effort cleanup; no PDF |
| Thousands of matching inspections | Confirmation warning; bounded memory via one-image staging |
| Very large source image | Annotation `maxDimension` bounds memory and PDF size |
| Empty/whitespace `ReportExportPath` | Falls back to `Downloads\PCB Deftek\Report` |
| Theme/DPI irrelevant | Report colors are fixed hex values, independent of app theme |

## 8. Open Questions

None. Resolved decisions:

1. **QuestPDF license** — Community is acceptable; the project is non-profit.
2. **Evidence volume** — exports above 100 inspections require confirmation and can be cancelled from the Background Activity modal; no evidence cap is applied.

All remaining behavior is specified in §3–§7; nothing is deferred to an unresolved question.
