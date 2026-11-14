# Requirements & Context: Phase 9 — Audit-Ready PDF Export Engine

## 1. Executive Summary & Context

Phase 8 shipped the Defect Statistics Dashboard with a deliberate, non-functional export seam. Phase 9 turns that seam into the product's audit deliverable: a QuestPDF-generated report containing the four dashboard metric cards (single column) followed by annotated PCB inspection images with their defect metadata, generated asynchronously, written to the configured report directory, and announced with a Windows toast notification.

The report must be a faithful, reproducible snapshot of the dashboard's active filter: it reuses the Phase 8 aggregate query and image corpus rather than re-implementing metric or filter semantics.

### Current State (verified in code)

| Concern | Existing behavior |
|---|---|
| Export trigger | `Presentation/Views/DashboardView.xaml` "Export" button → `DashboardViewModel.RequestExportCommand` |
| Export seam | `DashboardViewModel.ExportRequested` (`EventHandler<DashboardFilter>`) is raised with an immutable filter snapshot; **no subscriber exists** |
| Aggregates | `IDashboardRepository.GetStatisticsAsync(DashboardFilter)` → `DashboardStatistics` (adaptive buckets, class counts, totals) |
| Inspection evidence | `IInspectionRepository.GetByIdAsync(Guid)` loads `InspectionRecord` plus all `DetectedDefect` rows; images are referenced by `ImagePath` |
| Annotation | `IImageAnnotationService.AnnotateImage(path, defects, maxDimension)` returns a frozen WPF `BitmapSource`, or `null` when the file is missing/corrupt |
| Report path | `Common/Storage/KnownFolderPaths.GetReportExportDirectory()` → `%USERPROFILE%\Downloads\PCB Deftek\Report`; `IUserSettingsService.LoadSettingsAsync` already resolves an empty `ReportExportPath` to it |
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
   - Evidence blocks for every inspection matching the filter: annotated image and diagnostic metadata.

3. **Filtered evidence retrieval**
   - A dashboard-scoped query returning the inspections selected by the same date/class/severity predicate used by `GetStatisticsAsync`, newest first, with defects loaded.

4. **Image annotation and staging**
   - Annotation from the inspection's raw `ImagePath` via `IImageAnnotationService`, at a bounded pixel dimension.
   - PNG staging on disk for deterministic QuestPDF page composition and bounded memory.

5. **Export orchestration**
   - Resolve the destination directory, create it on demand, write atomically, report progress, notify on success/failure, clean up all temporary artifacts.
   - Default destination is `Downloads\PCB Deftek\Report`.

6. **Dashboard integration**
   - Replace the Phase 8 event seam with a real async export command backed by `IDashboardExportService`.
   - Prevent concurrent exports; auto-open the progress modal when an export starts.

7. **Verification**
   - Unit tests for content projection, SVG charts, path/filename policy, staging cleanup, progress and failure behavior.
   - SQLite integration test for the filtered evidence query and its parity with dashboard totals.
   - A real PDF smoke test into a temp directory.
   - One Reqnroll BDD scenario covering export from the dashboard with isolated database and temp export paths.

### 2.2 Out of Scope (Deferred)

- CSV/Excel export, scheduled or batch exports, e-mail delivery.
- Digital signatures, PDF/A conformance, encryption, or tamper-evident hashing.
- Report templates, localization, branding assets/logo (official branding is a Future Enhancement).
- Exporting the annotated images as separate files beside the PDF.
- A cancel button in the progress modal; cancellation plumbing is internal only.
- Large-export warning dialog or evidence-page cap (evaluate in Phase 10 using measured results).
- Chart drill-down or interactive elements inside the PDF.
- Any change to the defect taxonomy, knowledge base content, or dashboard visual design.

## 3. Decisions & Rationale

| # | Decision | Rationale |
|---|---|---|
| D1 | Add `QuestPDF` **2026.9.2** as the only new NuGet package; do not add ScottPlot or a WPF-rendering bridge. | QuestPDF is the dependency named in `tech-stack.md`; charts are simple line/bar series that can be emitted as SVG strings, so a second chart package is unnecessary. |
| D2 | Configure `QuestPDF.Settings.License = LicenseType.Community` once per process before any generation (startup + idempotent guard used by tests). | QuestPDF requires explicit license configuration; the project currently qualifies for Community only if the organization is under the revenue threshold. **License eligibility is an approval gate** (see Open Questions). |
| D3 | Split the engine into: pure content factory → pure SVG chart renderer → QuestPDF document builder → orchestrating export service. | Keeps document layout testable without PDF bytes, chart geometry testable without QuestPDF, and orchestration testable with fakes. |
| D4 | The exporter takes only a `DashboardFilter` snapshot and re-queries statistics + evidence itself. | The report is reproducible from its filter, independent of transient dashboard view state, and cannot drift from the dashboard's metric definitions. |
| D5 | Add `IDashboardRepository.GetMatchingInspectionsAsync(DashboardFilter)` returning `InspectionRecord` aggregates (with defects), newest first. | Severity is not a SQL column and is mapped through `DefectKnowledgeBase`; reusing the dashboard's predicate helpers is the only way to guarantee filter parity. It also avoids N+1 lookups. |
| D6 | Annotate from the raw `ImagePath` with `IImageAnnotationService`; never read or modify the app-owned `_annotated.png` sidecar, and never write to the source image. | One annotation implementation and color source (`DefectClassInfo.ColorHex`) for live view, viewer, and report; user-owned files stay untouched. |
| D7 | Encode annotated images to PNG in a temporary staging folder, one image at a time, then compose the PDF from staged files; delete the staging folder in `finally`. | Bounds memory for large exports, makes progress determinate, and lets QuestPDF read images lazily. |
| D8 | Include **every** inspection matching the filter, newest first, with no truncation; state the count in the report. | Audit reports must not silently omit evidence. Performance is measured, not assumed (see validation §4). |
| D9 | Destination directory: `UserSettings.ReportExportPath`; empty/whitespace falls back to `KnownFolderPaths.GetReportExportDirectory()` — `%USERPROFILE%\Downloads\PCB Deftek\Report`. File name `PCB-Deftek-Report-yyyyMMdd-HHmmss.pdf` (local time), with `-2`, `-3`, … suffixes on collision. | Matches the latest path agreement and Phase 7 decision D8; timestamped names avoid overwriting audit artifacts. |
| D10 | Generate into a sibling `.tmp` file and move it into place only after successful generation; delete the temp file and staging folder on failure or cancellation. | No partial or corrupt PDFs and no misleading success toast. |
| D11 | Replace the Phase 8 `ExportRequested` event with an injected `IDashboardExportService`; delete the event and its event-specific tests. | Phase 8's seam existed because the engine did not; keeping it now would add an indirection with no consumer value. Clean cutover per project policy. |
| D12 | Export progress: 0 % preparing, 10 % aggregates loaded, 15 % evidence list resolved, 15→95 % per staged image, 100 % completed. Progress flows through `IDashboardProgressService.ReportDataExport`; the modal opens automatically when export starts. | Uses the existing progress surface; auto-open gives immediate feedback without adding new UI. |
| D13 | Failures are logged (Serilog), reported as `DashboardProgressStatus.Failed` with a message, and surfaced by a toast; no exception escapes to the dispatcher. | Consistent with the app's centralized error policy; the user learns the export failed without a crash dialog. |
| D14 | Report card order is fixed (PCBs, Defects, Inspection Rate, Distribution) regardless of the dashboard's drag order or 1/2-column layout. | An audit document needs a stable, predictable structure; layout preference is a screen concern. |
| D15 | Report renders defect metadata from `DefectKnowledgeBase` (display name, abbreviation, severity) and confidences as whole percentages. | Single source of truth for taxonomy; no duplicated severity strings. |
| D16 | No database migration, no new settings keys, no changes to `UserSettings` persistence semantics; only the `UserSettings.ReportExportPath` **record default** is aligned to `KnownFolderPaths.GetReportExportDirectory()`. | The setting already resolves correctly at the service boundary; the domain default now matches the agreed destination for direct construction paths. |
| D17 | Tests use real SQLite (existing migration runner) and real QuestPDF generation into temp folders; only notifications and time are stubbed. | Proves the deliverable (a readable PDF) rather than asserting mock echo. |

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
- Empty series render axes and a `No data` annotation rather than throwing or producing a degenerate chart.

### 4.3 Evidence section

Starts on a new page. For each matching inspection (newest first):

- Heading: inspection `Name`.
- Metadata line: local timestamp, `Mode` (`Single file` / `Batch folder` / `Live camera`), `PASS`/`FAIL`, defect count.
- Annotated image, full column width, aspect ratio preserved.
- Defect table: abbreviation, class display name, confidence %, severity. One row per defect in descending confidence, then class order.
- PASS inspections (zero defects) show the image and the metadata line only.
- Missing, unreadable, or corrupt image: a bordered block stating `Image unavailable` plus the stored path; the defect table is still rendered. The export continues.
- Each inspection block avoids splitting the heading from its image where QuestPDF `EnsureSpace` permits; images may still flow to the next page.

### 4.4 Empty result

When no inspection matches the filter, the report still generates: metric blocks render zeros/empty axes and the evidence section states `No inspections match the selected filters.`

## 5. Export Lifecycle Contract

1. `RequestExportCommand` validates the filter (`DashboardFilterViewModel.TryCreateFilter`); invalid custom date ranges do nothing (existing behavior).
2. Export command is disabled while an export runs; a second invocation is ignored.
3. Progress modal opens; `DataExport` enters `Running`.
4. Exporter loads statistics, resolves matching inspections, stages annotated images (progress per image), composes the PDF off the UI thread.
5. On success: atomic move, `Completed` at 100 % with the saved path, toast `Report exported` + path, staging cleanup.
6. On failure: temp/staging cleanup, `Failed` with a user-safe message, toast `Report export failed`, Serilog error with exception.
7. On cancellation (app shutdown/navigation disposal): cleanup only; no toast.

## 6. Technical Constraints

- PDF generation, image annotation, PNG encoding, and file I/O run off the UI thread; no synchronous work on the dispatcher.
- WPF `BitmapSource` values crossing threads must be frozen (annotation service already guarantees this).
- No hardcoded absolute user paths; all destinations derive from settings or `KnownFolderPaths`.
- No writes outside the resolved report directory and the OS temp directory; user-owned source images are read-only.
- Nullable reference types enabled; 0 warnings.
- QuestPDF types stay inside `Infrastructure/Export`; ViewModels depend only on `IDashboardExportService`.

## 7. Edge Cases

| Case | Expected behavior |
|---|---|
| No inspections match | Summary PDF with zeros and the empty evidence note |
| 1 bucket / 1 class / one point | Chart axes remain valid; no divide-by-zero or inverted coordinates |
| All-time range with inspections years apart | Adaptive monthly/weekly buckets from `DashboardStatistics` are preserved exactly |
| Severity filter active | Evidence query and metric query select identical inspections (parity test) |
| PASS inspections with class/severity filters | Excluded from totals per Phase 8 D5; absent from evidence too |
| Image path missing/unreadable | `Image unavailable` block; export succeeds |
| Report directory missing | Created; export succeeds |
| Report directory unwritable | `Failed` state + toast; no partial file; staging cleaned |
| File name collision within the same second | `-2`, `-3`, … suffix; previous files untouched |
| Export cancelled mid-stage | No PDF, no staging folder, progress not `Completed` |
| Thousands of matching inspections | Export completes within the performance gate or is reported as a risk; memory stays bounded by one staged image at a time |
| Very large source image | Annotation `maxDimension` bounds memory and PDF size |
| Empty/whitespace `ReportExportPath` | Falls back to `Downloads\PCB Deftek\Report` |
| Theme/DPI irrelevant | Report colors are fixed hex values, independent of app theme |

## 8. Open Questions

1. **QuestPDF license eligibility** — Community is permitted only for individuals, non-profits, FOSS projects, and organizations under $1M USD annual gross revenue. The chosen `LicenseType` must be confirmed before merge; if ineligible, a Professional/Enterprise license is required (`LicenseType` change only, no code redesign).
2. **Evidence volume** — D8 exports every matching inspection. If measured exports exceed the validation performance gate on realistic data, decide in Phase 10 whether to warn before large exports or cap evidence pages. No cap is implemented now.
