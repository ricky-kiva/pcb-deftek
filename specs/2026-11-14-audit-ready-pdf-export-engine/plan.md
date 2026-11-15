# Implementation Plan: Phase 9 — Audit-Ready PDF Export Engine

## Overview

Replace the dashboard's non-functional `ExportRequested` seam with a real PDF export engine: a QuestPDF document containing the four Phase 8 metric cards in a single column followed by annotated inspection images and defect metadata, generated off the UI thread, written atomically to `Downloads\PCB Deftek\Report` (or the configured path), reported through the existing dashboard progress service, and announced with a Windows toast.

The same phase completes the Background Activity surface (per-process cancellation, `Processes` rename with activity dot, export icon, cleaner idle rows), adds large-export confirmation, removes unreadable inspection records from both the report and History, and replaces the empty report with a no-match notification.

Task list target: this plan (project convention `specs/<date>-<feature>/{plan,requirements,validation}.md`). Create the GitHub issue/branch/PR per `AGENTS.md` §4 when implementation starts.

## Architecture & Dependency Graph

```
QuestPDF 2026.9.2 (Community license, configured once)
        │
DashboardFilter (immutable, Phase 8) ──────────────────────────────┐
        │                                                           │
        ├── IDashboardRepository.GetStatisticsAsync (existing)       │
        ├── IDashboardRepository.GetMatchingInspectionsAsync (new)   │
        └── IDashboardRepository.GetMatchingInspectionCountAsync (new)
        │                                                           ▼
        │                                      DashboardPdfExportService (Infrastructure/Export)
        │                                        ├─ DashboardReportContentFactory (pure)
        │                                        ├─ DashboardReportChartSvgRenderer (pure)
        │                                        ├─ DashboardReportDocument (QuestPDF)
        │                                        ├─ IImageAnnotationService (existing)
        │                                        ├─ IInspectionDeletionService (existing, invalid records)
        │                                        ├─ IUserSettingsService (path)
        │                                        ├─ IProgress<DashboardExportProgress> (progress)
        │                                        ├─ INotificationService (toast)
        │                                        └─ temp staging folder (PNG)
        │
DashboardViewModel ── RequestExportCommand / CancelProcessCommand
        ├── IDashboardExportService
        ├── IConfirmationService (large export)
        └── IDashboardProgressService ── Background Activity modal (revised)

FileDetectionViewModel ── batch inference ── IDashboardProgressService (Inference channel)
HistoryViewModel ── thumbnail failure ── IInspectionDeletionService
App.OnExit ── CancelAll()
```

Ordering follows the dependency graph: package + contracts → process control/chrome → evidence query → pure content/chart projection → PDF layout → orchestration/cleanup/integration → verification.

## Task Breakdown

### Task Group 1: Dependency & Report Contracts

#### Task 1.1: QuestPDF Dependency Gate & License Configuration

- **Description:** Verify and add QuestPDF per `AGENTS.md` §2 and `tech-stack.md`, then configure the Community license once per process.
- **Acceptance criteria:**
  - [x] `socket` inspection of `QuestPDF` 2026.9.2 is recorded as safe (deep score 89, no alerts; `socket 1.14.1`).
  - [x] Official QuestPDF docs confirm the API used in Tasks 5.1–5.2 (`Document.Create`, `Image(byte[]/string)`, `Svg`, page numbers).
  - [x] `QuestPDF` 2026.9.2 is referenced in `pcb-deftek.csproj`; no other package is added.
  - [x] `QuestPDF.Settings.License = LicenseType.Community` is set before any generation; the guard is idempotent for tests. The non-profit eligibility is recorded in `validation.md`.
- **Verification:**
  - [x] `dotnet restore` and `dotnet build pcb-deftek.slnx` succeed with 0 warnings.
  - [x] A throwaway generation smoke check produces a valid `%PDF` file (deleted afterwards).
- **Dependencies:** None
- **Files:**
  - `pcb-deftek.csproj`
  - `App.xaml.cs`
  - `Infrastructure/Export/QuestPdfConfiguration.cs`
- **Estimated scope:** S (3 files)

#### Task 1.2: Report Content Contracts

- **Description:** Define the immutable report model consumed by the document builder: identity (title, generated timestamp), filter summary, metric blocks with chart series, and evidence entries with inspection metadata and defect rows (class, confidence, severity — no abbreviation). Define `DashboardExportResult` (exported path or no-match outcome) and reuse the existing `ExportException` instead of duplicating a PDF-specific type.
- **Acceptance criteria:**
  - [x] Report model contains no WPF or QuestPDF types.
  - [x] Evidence entry carries: inspection id/name, local timestamp, `DetectionMode`, PASS/FAIL, staged image path, and defect rows (class display name, confidence, severity).
  - [x] Metric block carries kind, title, display value, caption, and either time-series, PASS/FAIL, or class-count data.
  - [x] `DashboardExportProgress(Percentage, Message)` carries intermediate progress without any Presentation type.
  - [x] Export result distinguishes `Exported` (with path) from `NoMatches`.
  - [x] Failure exception derives from the existing `Common/Exceptions` hierarchy (`ExportException`).
- **Verification:**
  - [x] `dotnet test tests/pcb-deftek.Tests/ --filter FullyQualifiedName~DashboardExportContractTests`
  - [x] `dotnet build pcb-deftek.slnx` succeeds with 0 warnings.
- **Dependencies:** None
- **Files:**
  - `Infrastructure/Export/DashboardReportModel.cs`
  - `Infrastructure/Export/DashboardExportResult.cs`
  - `Infrastructure/Export/DashboardExportProgress.cs`
- **Estimated scope:** S (3 files)

#### Task 1.3: Export Service Contract

- **Description:** Define `IDashboardExportService` with an export method taking `DashboardFilter` and `CancellationToken`, plus a preflight count method used by the dashboard for no-match and large-export decisions. The interface must not expose QuestPDF types.
- **Acceptance criteria:**
  - [x] `CountMatchingInspectionsAsync(filter, token)` returns the number of inspections the export would include.
  - [x] `ExportAsync(filter, IProgress<DashboardExportProgress>? progress, token)` returns a `DashboardExportResult`, never a path for a no-match request.
  - [x] Interface is nullable-annotated and documents notification/cancellation side effects.
  - [x] The interface never exposes Presentation types; progress flows through `IProgress<DashboardExportProgress>`.
- **Verification:**
  - [x] `dotnet build pcb-deftek.slnx` succeeds with 0 warnings.
- **Dependencies:** Task 1.2
- **Files:**
  - `Infrastructure/Export/IDashboardExportService.cs`
- **Estimated scope:** XS (1 file)

---

### Checkpoint 1: Contracts Stable
- [x] QuestPDF builds on `net10.0-windows` with 0 warnings.
- [x] Report/export contracts compile and contain no presentation types.
- [x] Community license configuration is verified by a smoke generation.
- [ ] Review contracts with human before building process control and layout.

---

### Task Group 2: Background Activity Process Control & Dashboard Chrome

#### Task 2.1: Per-Process Cancellation Surface

- **Description:** Extend `IDashboardProgressService` with a `DashboardProcess` identity (`Inference`, `DataExport`), one cancellation token per process, `Cancel(process)`, `CancelAll()`, `HasActiveProcesses`, and a `Cancelled` status. Stop defaulting the message to the status name so idle rows do not render `Idle` twice.
- **Acceptance criteria:**
  - [x] Each process exposes a token that stays stable for one run and is renewed for the next run.
  - [x] `Cancel(process)` cancels that token and transitions a `Running` state to `Cancelled`; cancelling an idle/completed process is a no-op.
  - [x] `CancelAll()` cancels both tokens; used on application exit.
  - [x] `HasActiveProcesses` is true only when at least one process is `Running`, and raises `PropertyChanged`.
  - [x] `DashboardProgressState` defaults `Message` to empty (not the status name), including the static `Idle` state; `Idle` remains percentage 0.
  - [x] `Reset()` returns both processes to a fresh idle state and replaces their tokens.
  - [x] All updates remain marshalled through the captured UI context.
- **Verification:**
  - [x] `dotnet test tests/pcb-deftek.Tests/ --filter FullyQualifiedName~DashboardProgressServiceTests`
- **Dependencies:** Task 1.3
- **Files:**
  - `Presentation/Services/IDashboardProgressService.cs`
  - `Presentation/Services/DashboardProgressService.cs`
  - `tests/pcb-deftek.Tests/DashboardProgressServiceTests.cs`
- **Estimated scope:** M (3 files)

#### Task 2.2: Modal & Toolbar Revisions

- **Description:** Revise the Background Activity modal and dashboard toolbar: per-process Cancel button, one status line with message only when it adds information, progress track hidden while `Idle`, no separator-like element between title and status, `Alerts` → `Processes` with a new activity icon and running-process dot, and the export icon before the Export text.
- **Acceptance criteria:**
  - [x] Each process row renders its title and exactly one status line; an idle row shows `Idle` once and no progress track.
  - [x] Cancel button is visible/enabled only while its process is `Running` and invokes the matching cancel command.
  - [x] `Processes` label replaces `Alerts`; `IconProcesses` (activity/pulse geometry) replaces the bell; tooltip and automation name are updated.
  - [x] The notification dot overlays the Processes icon only when `HasActiveProcesses`.
  - [x] The Export button shows the `IconExport` glyph before the text.
  - [x] No separator line is drawn between a process title and its status.
- **Verification:**
  - [x] `dotnet test tests/pcb-deftek.Tests/ --filter FullyQualifiedName~DashboardViewCompositionTests`
  - [x] Render check: idle, running, completed, failed, and cancelled states produce no layout exceptions.
- **Dependencies:** Task 2.1
- **Files:**
  - `Presentation/Views/DashboardView.xaml`
  - `Presentation/Styles/Icons.xaml`
  - `tests/pcb-deftek.Tests/DashboardViewCompositionTests.cs`
- **Estimated scope:** M (3 files)

#### Task 2.3: Dashboard Process Commands

- **Description:** Add `CancelProcessCommand(DashboardProcess)` and expose `HasActiveProcesses` from the view model for the toolbar dot; wire both to the progress service.
- **Acceptance criteria:**
  - [x] Cancelling a running process transitions it to `Cancelled` and does not throw.
  - [x] Cancelling an idle/completed process is ignored.
  - [x] `HasActiveProcesses` raises change notifications when any process starts or stops.
- **Verification:**
  - [x] `dotnet test tests/pcb-deftek.Tests/ --filter FullyQualifiedName~DashboardViewModelTests`
- **Dependencies:** Task 2.1
- **Files:**
  - `Presentation/ViewModels/DashboardViewModel.cs`
  - `tests/pcb-deftek.Tests/DashboardViewModelTests.cs`
- **Estimated scope:** S (2 files)

#### Task 2.4: Batch Inference Reporting & Cancellation

- **Description:** Feed the Background Activity `Inference` row from batch detection: `FileDetectionViewModel` reports running/completed/failed progress, and bidirectional cancellation links the batch action and the modal Cancel button. Live camera continuous inference is not treated as a background process.
- **Acceptance criteria:**
  - [x] Starting a batch publishes `Inference` running with processed/total progress.
  - [x] Batch completion/failure publishes the matching terminal state.
  - [x] Cancelling from the dashboard cancels the batch; cancelling from the existing File Detection button also marks the modal process `Cancelled`.
  - [x] Live camera inference does not publish background-process state.
  - [x] Repeated batches start from a fresh token/state.
- **Verification:**
  - [x] `dotnet test tests/pcb-deftek.Tests/ --filter FullyQualifiedName~FileDetectionViewModelTests`
  - [x] `dotnet test tests/pcb-deftek.Tests/ --filter FullyQualifiedName~DashboardProgressServiceTests`
- **Dependencies:** Task 2.1
- **Files:**
  - `Presentation/ViewModels/FileDetectionViewModel.cs`
  - `tests/pcb-deftek.Tests/FileDetectionViewModelTests.cs`
- **Estimated scope:** M (2 files)

#### Task 2.5: Application-Exit Cancellation

- **Description:** Cancel all processes when the application exits so an in-flight report export stops and its temp/staging artifacts can be cleaned up before the provider is disposed.
- **Acceptance criteria:**
  - [x] `App.OnExit` resolves `IDashboardProgressService` and calls `CancelAll()` before disposing the service provider.
  - [x] Shutdown does not throw when the service is unavailable (startup failure path).
  - [x] Cancellation is observable by an in-flight export token.
- **Verification:**
  - [x] `dotnet test tests/pcb-deftek.Tests/ --filter FullyQualifiedName~DashboardProgressServiceTests`
  - [ ] Manual: start a large export and close the app; no PDF or staging folder remains. Export cancellation and artifact cleanup are automated; the close-app manual check remains for validation.
- **Dependencies:** Task 2.1
- **Files:**
  - `App.xaml.cs`
- **Estimated scope:** XS (1 file)

---

### Checkpoint 2: Process Control Complete
- [x] Every process row can be cancelled and reports a terminal state.
- [x] Modal chrome matches the revision (single status, no idle separator, Processes + dot, export icon).
- [x] Batch inference and export both surface through the Background Activity modal.
- [x] App exit cancels in-flight work.

---

### Task Group 3: Filtered Evidence Query

#### Task 3.1: `GetMatchingInspectionsAsync`

- **Description:** Add a dashboard repository query returning every inspection selected by the dashboard filter (date bounds, class set, severity→class mapping), newest first, with defects loaded. Reuse the existing predicate/parameter helpers so the selection matches `GetStatisticsAsync`.
- **Acceptance criteria:**
  - [x] Date bounds are inclusive UTC; empty class/severity sets add no restriction.
  - [x] Severity selection maps through `DefectKnowledgeBase` exactly as `GetStatisticsAsync` does.
  - [x] An inspection matches only when it has at least one defect in both active sets (Phase 8 D5).
  - [x] Results include `InspectionRecord.Id/Name/CreatedAtUtc/UpdatedAtUtc/Mode/ImagePath/Origin` and all defects.
  - [x] Ordering is `CreatedAtUtc DESC` with a deterministic tiebreaker.
- **Verification:**
  - [x] `dotnet test tests/pcb-deftek.Tests/ --filter FullyQualifiedName~DashboardRepositoryTests`
- **Dependencies:** Task 1.3
- **Files:**
  - `Infrastructure/Persistence/Repositories/IDashboardRepository.cs`
  - `Infrastructure/Persistence/Repositories/SqliteDashboardRepository.cs`
  - `tests/pcb-deftek.Tests/DashboardRepositoryTests.cs`
- **Estimated scope:** M (3 files)

#### Task 3.2: Matching Count & Parity Tests

- **Description:** Add `GetMatchingInspectionCountAsync` using the same predicate composition, then extend the real-SQLite tests to prove evidence, count, and aggregates agree.
- **Acceptance criteria:**
  - [x] For each filter combination (all time, range, classes, severities, both), count equals `GetMatchingInspectionsAsync().Count` and `GetStatisticsAsync().TotalInspections`.
  - [x] PASS inspections are excluded when class/severity filters are active.
  - [x] Defect counts across evidence entries equal `TotalDefects`.
  - [x] Empty result returns 0 / an empty list, never null.
- **Verification:**
  - [x] `dotnet test tests/pcb-deftek.Tests/ --filter FullyQualifiedName~DashboardRepositoryTests`
- **Dependencies:** Task 3.1
- **Files:**
  - `Infrastructure/Persistence/Repositories/IDashboardRepository.cs`
  - `Infrastructure/Persistence/Repositories/SqliteDashboardRepository.cs`
  - `tests/pcb-deftek.Tests/DashboardRepositoryTests.cs`
- **Estimated scope:** S (3 files)

---

### Checkpoint 3: Evidence Corpus Correct
- [x] Evidence count equals dashboard totals for every filter combination.
- [x] No N+1 queries; one command per evidence/count query.
- [x] Existing dashboard repository tests remain green.

---

### Task Group 4: Report Content & Charts

#### Task 4.1: Content Factory

- **Description:** Build the report model from `DashboardStatistics`, the valid matching inspections, and the filter. Apply fixed card order, `CurrentCulture` formatting, filter summaries, mode labels, PASS/FAIL labels, and defect ordering (confidence desc, then class order). Defect rows carry class name, confidence, and severity only.
- **Acceptance criteria:**
  - [x] Four metric blocks in canonical order, independent of dashboard card order/layout.
  - [x] Values and captions match `DashboardViewModel` projections for the same `DashboardStatistics` (parity test).
  - [x] Filter summary renders `All time`/`All nine classes`/`All severities` when unrestricted, otherwise local dates, taxonomy-ordered names, and severity labels.
  - [x] Defect rows resolve names/severities from `DefectKnowledgeBase`; no abbreviation field.
  - [x] No evidence entries yields a model that the orchestrator refuses to export (no document is generated).
- **Verification:**
  - [x] `dotnet test tests/pcb-deftek.Tests/ --filter FullyQualifiedName~DashboardReportContentFactoryTests`
- **Dependencies:** Tasks 1.2, 3.2
- **Files:**
  - `Infrastructure/Export/DashboardReportContentFactory.cs`
  - `tests/pcb-deftek.Tests/DashboardReportContentFactoryTests.cs`
- **Estimated scope:** S (2 files)

#### Task 4.2: Chart SVG Renderer

- **Description:** Render three chart kinds as self-contained SVG strings sized to the available width: single-series line, dual PASS/FAIL line, and nine-class vertical bars. Use fixed hex colors, deterministic label thinning, and non-degenerate axes for 0/1/n points.
- **Acceptance criteria:**
  - [x] Output is valid XML with a `viewBox`, no scripts, no external resources.
  - [x] Label step never overlaps labels at the report width; the rule is unit-tested.
  - [x] PASS uses `#107C41`, FAIL uses `#C42B1C`, bar fills use `DefectClassInfo.ColorHex`.
  - [x] All nine bars appear in class order, including zero values.
  - [x] Empty/single-point series render without division by zero or inverted coordinates.
  - [x] Values are XML-escaped (labels/class names cannot break the document).
- **Verification:**
  - [x] `dotnet test tests/pcb-deftek.Tests/ --filter FullyQualifiedName~DashboardReportChartSvgRendererTests`
- **Dependencies:** Task 4.1
- **Files:**
  - `Infrastructure/Export/DashboardReportChartSvgRenderer.cs`
  - `tests/pcb-deftek.Tests/DashboardReportChartSvgRendererTests.cs`
- **Estimated scope:** M (2 files)

---

### Checkpoint 4: Content & Charts Correct
- [x] Content parity with dashboard projections is proven by tests.
- [x] SVG charts parse as XML and render at report width.
- [x] No QuestPDF dependency in content/chart code.

---

### Task Group 5: PDF Document Composition

#### Task 5.1: Document Shell, Header, Metric Section

- **Description:** Implement the QuestPDF document: A4 portrait, margins, header/filter summary, four metric blocks with SVG charts, and footer page numbering.
- **Acceptance criteria:**
  - [x] Header renders title, generated timestamp, and filter summary.
  - [x] Four blocks render in fixed canonical order, each with title/value/caption/chart.
  - [x] Footer shows `Page x of y`.
  - [x] Metric charts render correctly for zero/single/many buckets.
  - [x] Document composition has no UI-thread dependency.
- **Verification:**
  - [x] `dotnet test tests/pcb-deftek.Tests/ --filter FullyQualifiedName~DashboardReportDocumentTests`
  - [x] Generated PDF starts with `%PDF-` and is non-trivial in size.
- **Dependencies:** Tasks 4.1, 4.2
- **Files:**
  - `Infrastructure/Export/DashboardReportDocument.cs`
  - `tests/pcb-deftek.Tests/DashboardReportDocumentTests.cs`
- **Estimated scope:** M (2 files)

#### Task 5.2: Evidence Blocks

- **Description:** Compose evidence pages: inspection heading, metadata line, annotated image at maximum 40 % of the content width, and a three-column defect table (Class, Confidence, Severity).
- **Acceptance criteria:**
  - [x] Evidence section starts on a new page and lists newest-first.
  - [x] Each block shows name, local timestamp, mode, PASS/FAIL, and defect count.
  - [x] The image never exceeds 40 % of the content width and preserves aspect ratio.
  - [x] The defect table has exactly three columns: Class, Confidence, Severity (no abbreviation).
  - [x] Zero-defect inspections show image + metadata only.
  - [x] Evidence blocks receive only valid, staged images; no `Image unavailable` placeholder exists.
- **Verification:**
  - [x] `dotnet test tests/pcb-deftek.Tests/ --filter FullyQualifiedName~DashboardReportDocumentTests`
- **Dependencies:** Task 5.1
- **Files:**
  - `Infrastructure/Export/DashboardReportDocument.cs`
  - `tests/pcb-deftek.Tests/DashboardReportDocumentTests.cs`
- **Estimated scope:** M (2 files)

#### Task 5.3: Real PDF Smoke Test

- **Description:** Generate a document with mixed PASS/FAIL data, all nine classes, and multi-page evidence into a temp directory; assert the file signature, non-zero size, no staged leftovers, and cleanup.
- **Acceptance criteria:**
  - [x] Test writes and deletes only under its own temp root.
  - [x] PDF signature is `%PDF-` and size exceeds a minimal threshold.
  - [x] Generation succeeds with 0 defects and with 9 classes present.
  - [x] No file handle leaks (temp folder deletes cleanly).
- **Verification:**
  - [x] `dotnet test tests/pcb-deftek.Tests/ --filter FullyQualifiedName~DashboardReportPdfSmokeTests`
- **Dependencies:** Task 5.2
- **Files:**
  - `tests/pcb-deftek.Tests/DashboardReportPdfSmokeTests.cs`
- **Estimated scope:** S (1 file)

---

### Checkpoint 5: Report Renders Correctly
- [x] A real PDF contains all sections. A 100-inspection export was generated and inspected (35 pages, `%PDF-`, header/metrics/evidence/footer); automated composition tests also assert the same sections.
- [x] Evidence image width and table columns match the revision.
- [x] Generation was measured on a 100-inspection corpus: 2.4–2.7 s, 0.87 MB PDF, +80.5 MB working set, no staging leftovers (see `validation.md` §9).

---

### Task Group 6: Export Orchestration, Cleanup & Integration

#### Task 6.1: `DashboardPdfExportService`

- **Description:** Implement the orchestrator: resolve/validate destination, query statistics and evidence, stage annotated PNGs one at a time with progress, compose the PDF off the UI thread, move atomically, toast on success/failure, honour the cancellation token, and clean up in `finally`. `Dispose` cancels and removes temp artifacts.
- **Acceptance criteria:**
  - [x] Destination comes from `IUserSettingsService.ReportExportPath`; blank falls back to `KnownFolderPaths.GetReportExportDirectory()`.
  - [x] Missing directory is created; filename policy and collision suffixes are unit-tested.
  - [x] Annotation uses `IImageAnnotationService` with a bounded `maxDimension`; source images are only read.
  - [x] PNG encoding works off the UI thread with frozen bitmaps.
  - [x] Zero matching inspections returns `NoMatches` without creating a file or starting progress.
  - [x] Progress: 0/10/15/15–95/100 reported through the supplied `IProgress<DashboardExportProgress>`; `Completed` only after the final move.
  - [x] The export service has no reference to Presentation progress types or the view model.
  - [x] Cancellation reports `Cancelled`, deletes the temp PDF/staging folder, and shows no success toast.
  - [x] Failure deletes temp artifacts, logs, and raises the typed export exception.
  - [x] Success and failure toasts dispatch through `INotificationService`; cancellation sends no toast.
  - [x] Concurrent calls are serialized or rejected; no interleaved writes.
- **Verification:**
  - [x] `dotnet test tests/pcb-deftek.Tests/ --filter FullyQualifiedName~DashboardPdfExportServiceTests`
- **Dependencies:** Tasks 3.1, 5.3
- **Files:**
  - `Infrastructure/Export/DashboardPdfExportService.cs`
  - `tests/pcb-deftek.Tests/DashboardPdfExportServiceTests.cs`
- **Estimated scope:** M (2 files)

#### Task 6.2: Invalid-Image Cleanup in Report Export

- **Description:** When an evidence image is missing or cannot be decoded, delete that inspection through `IInspectionDeletionService` and exclude it from the report. Guard against environmental failures so a disconnected volume cannot trigger mass deletion.
- **Acceptance criteria:**
  - [x] A missing/corrupt image deletes the inspection record and its app-owned captures; user-owned files are untouched.
  - [x] Deleted inspections are absent from the evidence section; the export continues.
  - [x] Deletion is retried once before committing; when the parent directory is inaccessible, deletion is skipped and logged.
  - [x] Deletion failures are logged and do not abort the export.
  - [x] Report totals remain the filtered aggregate from export start (documented in requirements).
- **Verification:**
  - [x] `dotnet test tests/pcb-deftek.Tests/ --filter FullyQualifiedName~DashboardPdfExportServiceTests`
- **Dependencies:** Task 6.1
- **Files:**
  - `Infrastructure/Export/DashboardPdfExportService.cs`
  - `tests/pcb-deftek.Tests/DashboardPdfExportServiceTests.cs`
- **Estimated scope:** M (2 files)

#### Task 6.3: History Invalid-Image Cleanup

- **Description:** After gallery thumbnail hydration, remove records whose image is missing or undecodable using the deletion service, refresh counts/groups, and inform the operator.
- **Acceptance criteria:**
  - [x] Cards whose thumbnail cannot be decoded are removed from the gallery.
  - [x] The corresponding records are deleted from SQLite; app-owned captures are cleaned; user originals are untouched.
  - [x] After cleanup the gallery reloads page 1 so page-number paging cannot skip records; total count, groups, selection, and empty/no-match states remain consistent.
  - [x] No placeholder is shown for records that are removed in the same hydration pass.
  - [x] A single toast reports how many unreadable inspections were removed; failures are logged.
  - [x] Deletion is retried once before committing; it is skipped when the parent directory is inaccessible (environmental failure).
- **Verification:**
  - [x] `dotnet test tests/pcb-deftek.Tests/ --filter FullyQualifiedName~HistoryViewModelTests`
  - [x] `dotnet test tests/pcb-deftek.Tests/ --filter FullyQualifiedName~HistoryItemViewModelTests`
- **Dependencies:** None (uses the existing deletion service)
- **Files:**
  - `Presentation/ViewModels/HistoryViewModel.cs`
  - `tests/pcb-deftek.Tests/HistoryViewModelTests.cs`
- **Estimated scope:** M (2 files)

#### Task 6.4: Dashboard ViewModel Integration & Preflight

- **Description:** Inject `IDashboardExportService` and `IConfirmationService`, delete `ExportRequested`, make `RequestExportCommand` async with a running guard, run preflight (no-match toast, >100 confirmation), auto-open the modal, pass the progress service's DataExport token, and report cancellation/failure.
- **Acceptance criteria:**
  - [x] `ExportRequested` and its event-based tests are removed.
  - [x] Zero matches → toast only; no modal, no progress, no export call.
  - [x] More than 100 matches (`LargeExportInspectionThreshold = 100`) → confirmation with the count; declining aborts.
  - [x] 100 or fewer matches → export starts without a prompt.
  - [x] Command is disabled/ignored while exporting and resumes afterward.
  - [x] Modal opens on start; the DataExport token is cancelled by the modal Cancel button.
  - [x] A `Progress<DashboardExportProgress>` forwards exporter percentages/messages to `IDashboardProgressService.ReportDataExport`; terminal statuses are `Completed`, `Cancelled`, or `Failed`.
  - [x] If the export removed invalid records, dashboard statistics reload after completion so displayed totals match the database.
  - [x] Cancellation ends without a success/error toast; failure sets a visible state.
  - [x] Success/failure never throw on the dispatcher.
- **Verification:**
  - [x] `dotnet test tests/pcb-deftek.Tests/ --filter FullyQualifiedName~DashboardViewModelTests`
- **Dependencies:** Tasks 6.1, 2.2, 2.3
- **Files:**
  - `Presentation/ViewModels/DashboardViewModel.cs`
  - `tests/pcb-deftek.Tests/DashboardViewModelTests.cs`
- **Estimated scope:** M (2 files)

#### Task 6.5: DI Registration & Resolution

- **Description:** Register the export service and assert lifetimes/resolution in the real provider, including the new `IDashboardProgressService` members and view-model dependencies.
- **Acceptance criteria:**
  - [x] `IDashboardExportService` resolves with all dependencies.
  - [x] `IDashboardProgressService` remains a singleton; `DashboardViewModel` remains transient.
  - [x] No circular dependency between export, progress, deletion, and settings services.
- **Verification:**
  - [x] `dotnet test tests/pcb-deftek.Tests/ --filter FullyQualifiedName~DependencyInjectionTests`
- **Dependencies:** Tasks 6.1, 6.4
- **Files:**
  - `App.xaml.cs`
  - `tests/pcb-deftek.Tests/DependencyInjectionTests.cs`
- **Estimated scope:** S (2 files)

---

### Checkpoint 6: Export Works End-to-End
- [x] Dashboard Export passes the active filter to the exporter and the exporter writes a PDF (automated VM/service tests; real-SQLite end-to-end is Group 7).
- [x] No-match, large-export confirmation, and modal cancellation paths work.
- [x] Invalid-image records are removed from report and History.
- [x] Progress/modal states reflect running/completed/cancelled/failed.
- [x] BDD implementation was explicitly authorized by the user in this task; the final PR review remains Checkpoint 7.

---

### Task Group 7: BDD, Documentation & Final Gates

#### Task 7.1: Export BDD Scenarios

- **Description:** Add Reqnroll coverage for the export flows using isolated SQLite, temp report directory, stub notification and confirmation services.
- **Acceptance criteria:**
  - [x] Scenario: filter → export → file exists at the configured path and notification fired.
  - [x] Scenario: blank report path resolves to `Downloads/PCB Deftek/Report` (temp-rooted resolver).
  - [x] Scenario: no matches → no PDF, no modal, toast fired.
  - [x] Scenario: >100 matches → confirmation prompt; decline produces no file.
  - [x] Scenario: cancel from the Background Activity modal → no PDF, `Cancelled` state.
  - [x] Scenario: invalid evidence image → record deleted, export still succeeds.
  - [x] Scenario: History card with a missing image → card removed and record deleted after hydration.
  - [x] BDD never writes to the developer's real Downloads folder or database.
- **Verification:**
  - [x] `dotnet test tests/pcb-deftek.Bdd/ --filter "FullyQualifiedName~AuditPdfExportFeature"` → 7 passed.
  - [x] Full suite passes: `dotnet test pcb-deftek.slnx` → 834 passed (790 unit + 44 BDD).
- **Dependencies:** Task Group 6
- **Files:**
  - `tests/pcb-deftek.Bdd/Features/AuditPdfExport.feature`
  - `tests/pcb-deftek.Bdd/StepDefinitions/DashboardExportStepDefinitions.cs`
- **Estimated scope:** M (2 files)

#### Task 7.2: Documentation & Plan Status

- **Description:** Record completion, deviations, and verification results; keep requirements/validation aligned with the delivered behavior.
- **Acceptance criteria:**
  - [x] Plan checkboxes reflect actual state; deferred items remain in requirements.
  - [x] Validation records automated and manual results, including the non-profit license decision and performance measurements.
  - [x] No unchecked task is silently skipped.
- **Verification:**
  - [x] `git diff` shows only status/documentation edits in this specification.
- **Dependencies:** Task 7.1
- **Files:**
  - `specs/2026-11-14-audit-ready-pdf-export-engine/plan.md`
  - `specs/2026-11-14-audit-ready-pdf-export-engine/validation.md`
- **Estimated scope:** XS (2 files)

---

### Checkpoint 7: Merge Readiness
- [x] All seven task groups complete.
- [x] Solution builds with 0 warnings.
- [x] All unit, integration, and BDD tests pass (`dotnet test pcb-deftek.slnx` → 834 passed).
- [x] PR [#118](https://github.com/ricky-kiva/pcb-deftek/pull/118) is linked to issue #117 and passes the 4-step Feature Review and Quality Improvement pipeline locally.
- [x] Non-profit QuestPDF Community license recorded.
- [ ] Human review approved; PR not merged without approval.

---

## Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Automatic deletion of records with transient/unreadable images | High | One retry before deletion; skip deletion when the parent directory is inaccessible; route through `IInspectionDeletionService`; unit tests for both delete and skip paths. |
| Mass cleanup on a disconnected volume | High | Environmental-failure guard: if the image root is unavailable, log and skip deletion for the run. |
| Large exports (hundreds/thousands of images) | High | Confirmation above 100 matches; one-image staging bounds memory; modal cancellation; validation §4 measures the corpus. |
| Cancellation during QuestPDF composition | Medium | QuestPDF generation has no token; check the token between phases and before the atomic move; always delete temp/staging artifacts. |
| Partial/corrupt PDF after failure | High | Temp file + atomic move + `finally` cleanup; failure-path tests. |
| Evidence, count, and aggregates diverge | High | Shared predicate helpers + parity tests (`count == evidence.Count == TotalInspections`). |
| Deletion feedback while gallery hydration runs | Medium | Batch deletions after the hydration pass; refresh counts/groups once; no per-card churn. |
| QuestPDF layout exceptions on extreme aspect ratios | Medium | 40 % width with `FitArea`/preserve aspect; bounded staging dimensions; smoke tests with mixed data. |
| WPF encoding threaded incorrectly | Medium | Annotation returns frozen bitmaps; PNG encoding on worker; test generation off the dispatcher. |
| Report values drift from dashboard formatting | Medium | Content factory parity test against `DashboardViewModel` projections for identical statistics. |
| SVG labels overlap or break XML | Medium | Deterministic thinning + escaping tests; fixed viewBox. |
| Continuous camera inference cannot be cancelled | Medium | Live camera is intentionally not a background process; only batch inference is wired to the Inference row; cancel is a no-op outside `Running`. |
| Mass prompts for >100 exports | Low | One confirmation per export, not per inspection. |
| App exit before cleanup finishes | Medium | `CancelAll()` on exit, `Dispose` best-effort cleanup; manual verification in Task 2.5. |
| Constructor changes break tests/BDD | Low | Update fakes, DI tests, and BDD bindings in the same task; full suite gate. |
| Temp staging disk usage | Medium | Delete in `finally`; one image at a time; max dimension bounds each PNG. |

## Open Questions

None. Resolved:

1. **QuestPDF license** — Community, because the project is non-profit.
2. **Evidence volume** — confirm above 100 inspections and allow cancellation; no cap.
