# Implementation Plan: Phase 9 — Audit-Ready PDF Export Engine

## Overview

Replace the dashboard's non-functional `ExportRequested` seam with a real PDF export engine: a QuestPDF document containing the four Phase 8 metric cards in a single column followed by annotated inspection images and defect metadata, generated off the UI thread, written atomically to `Downloads\PCB Deftek\Report` (or the configured path), reported through the existing dashboard progress service, and announced with a Windows toast.

Task list target: this plan (project convention `specs/<date>-<feature>/{plan,requirements,validation}.md`). Create the GitHub issue/branch/PR per `AGENTS.md` §4 when implementation starts.

## Architecture & Dependency Graph

```
QuestPDF 2026.9.2 (new package, license configured once)
        │
DashboardFilter (immutable, Phase 8) ─────────────────────────────┐
        │                                                          │
        ├── IDashboardRepository.GetStatisticsAsync (existing)      │
        └── IDashboardRepository.GetMatchingInspectionsAsync (new)  │
        │                                                          ▼
        │                                     DashboardPdfExportService (Infrastructure/Export)
        │                                       ├─ DashboardReportContentFactory (pure)
        │                                       ├─ DashboardReportChartSvgRenderer (pure)
        │                                       ├─ DashboardReportDocument (QuestPDF)
        │                                       ├─ IImageAnnotationService (existing)
        │                                       ├─ IUserSettingsService (path)
        │                                       ├─ IDashboardProgressService (progress)
        │                                       ├─ INotificationService (toast)
        │                                       └─ temp staging folder (PNG)
        │                                                          │
DashboardViewModel ── RequestExportCommand (async) ── IDashboardExportService
        │
        └── IDashboardProgressService ── notification modal (existing)
```

Ordering follows the dependency graph: package + contracts → evidence query → pure content/chart projection → PDF layout → orchestration/integration → verification.

## Task Breakdown

### Task Group 1: Dependency & Report Contracts

#### Task 1.1: QuestPDF Dependency Gate & License Configuration

- **Description:** Verify and add QuestPDF per `AGENTS.md` §2 and `tech-stack.md`, then configure its license once per process.
- **Acceptance criteria:**
  - [ ] `socket` inspection of `QuestPDF` 2026.9.2 is recorded as safe (CLI available: `socket 1.14.1`).
  - [ ] Official QuestPDF docs confirm the API used in Tasks 4.1–4.2 (`Document.Create`, `Image(byte[]/string)`, `Svg`, page numbers).
  - [ ] `QuestPDF` 2026.9.2 is referenced in `pcb-deftek.csproj`; no other package is added.
  - [ ] License is set exactly once before generation (`LicenseType.Community` pending eligibility confirmation) and the guard is idempotent for tests.
- **Verification:**
  - [ ] `dotnet restore` and `dotnet build pcb-deftek.slnx` succeed with 0 warnings.
  - [ ] A throwaway generation smoke check produces a valid `%PDF` file (deleted afterwards).
- **Dependencies:** None
- **Files:**
  - `pcb-deftek.csproj`
  - `App.xaml.cs`
  - `Infrastructure/Export/QuestPdfConfiguration.cs`
- **Estimated scope:** S (3 files)

#### Task 1.2: Report Content Contracts

- **Description:** Define the immutable report model consumed by the document builder: identity (title, generated timestamp), filter summary, metric blocks with chart series, and evidence entries with inspection metadata and defect rows. Define the export result (file path) and a typed exception for export failures.
- **Acceptance criteria:**
  - [ ] Report model contains no WPF or QuestPDF types.
  - [ ] Evidence entry carries: inspection id/name, local timestamp, `DetectionMode`, PASS/FAIL, image path, and defect rows (abbreviation, display name, confidence, severity).
  - [ ] Metric block carries kind, title, display value, caption, and either time-series, PASS/FAIL, or class-count data.
  - [ ] Failure exception derives from the existing `Common/Exceptions` hierarchy.
- **Verification:**
  - [ ] `dotnet build pcb-deftek.slnx` succeeds with 0 warnings.
- **Dependencies:** None
- **Files:**
  - `Infrastructure/Export/DashboardReportModel.cs`
  - `Infrastructure/Export/DashboardReportExportException.cs`
- **Estimated scope:** S (2 files)

#### Task 1.3: Export Service Contract

- **Description:** Define `IDashboardExportService` with an async export method taking `DashboardFilter` and `CancellationToken`, returning the generated file path. The interface must not expose QuestPDF types.
- **Acceptance criteria:**
  - [ ] Interface is nullable-annotated and documents progress/notification side effects.
  - [ ] Method returns the absolute path of the created PDF.
  - [ ] DI can register the interface without touching `Presentation`.
- **Verification:**
  - [ ] `dotnet build pcb-deftek.slnx` succeeds with 0 warnings.
- **Dependencies:** Task 1.2
- **Files:**
  - `Infrastructure/Export/IDashboardExportService.cs`
- **Estimated scope:** XS (1 file)

---

### Checkpoint 1: Contracts Stable
- [ ] QuestPDF builds on `net10.0-windows` with 0 warnings.
- [ ] Report/export contracts compile and contain no presentation types.
- [ ] License configuration is verified by a smoke generation.
- [ ] Review contracts with human before building the query/layout.

---

### Task Group 2: Filtered Evidence Query

#### Task 2.1: `GetMatchingInspectionsAsync`

- **Description:** Add a dashboard repository query returning every inspection selected by the dashboard filter (date bounds, class set, severity→class mapping), newest first, with defects loaded. Reuse the existing predicate/parameter helpers so the selection matches `GetStatisticsAsync`.
- **Acceptance criteria:**
  - [ ] Date bounds are inclusive UTC; empty class/severity sets add no restriction.
  - [ ] Severity selection maps through `DefectKnowledgeBase` exactly as `GetStatisticsAsync` does.
  - [ ] An inspection matches only when it has at least one defect in both active sets (Phase 8 D5).
  - [ ] Results include `InspectionRecord.Id/Name/CreatedAtUtc/UpdatedAtUtc/Mode/ImagePath/Origin` and all defects.
  - [ ] Ordering is `CreatedAtUtc DESC` with a deterministic tiebreaker.
- **Verification:**
  - [ ] `dotnet test tests/pcb-deftek.Tests/ --filter FullyQualifiedName~DashboardRepositoryTests`
- **Dependencies:** Task 1.3
- **Files:**
  - `Infrastructure/Persistence/Repositories/IDashboardRepository.cs`
  - `Infrastructure/Persistence/Repositories/SqliteDashboardRepository.cs`
  - `tests/pcb-deftek.Tests/DashboardRepositoryTests.cs`
- **Estimated scope:** M (3 files)

#### Task 2.2: Query Parity & Edge Tests

- **Description:** Extend the real-SQLite dashboard repository tests to prove evidence selection matches aggregate selection.
- **Acceptance criteria:**
  - [ ] For each filter combination (all time, range, classes, severities, both), `GetMatchingInspectionsAsync().Count == GetStatisticsAsync().TotalInspections`.
  - [ ] PASS inspections are excluded when class/severity filters are active.
  - [ ] Defect counts across evidence entries equal `TotalDefects`.
  - [ ] Empty result returns an empty list, not null.
- **Verification:**
  - [ ] `dotnet test tests/pcb-deftek.Tests/ --filter FullyQualifiedName~DashboardRepositoryTests`
- **Dependencies:** Task 2.1
- **Files:**
  - `tests/pcb-deftek.Tests/DashboardRepositoryTests.cs`
- **Estimated scope:** S (1 file)

---

### Checkpoint 2: Evidence Corpus Correct
- [ ] Evidence selection matches dashboard totals for every filter combination.
- [ ] No N+1 queries; one connection/command per export query.
- [ ] Existing dashboard repository tests remain green.

---

### Task Group 3: Report Content & Charts

#### Task 3.1: Content Factory

- **Description:** Build the report model from `DashboardStatistics`, the matching inspections, and the filter. Apply fixed card order, `CurrentCulture` formatting, filter summaries, mode labels, PASS/FAIL labels, and defect ordering (confidence desc, then class order).
- **Acceptance criteria:**
  - [ ] Four metric blocks in canonical order, independent of dashboard card order/layout.
  - [ ] Values and captions match `DashboardViewModel` projections for the same `DashboardStatistics` (parity test).
  - [ ] Filter summary renders `All time`/`All nine classes`/`All severities` when unrestricted, otherwise local dates, taxonomy-ordered names, and severity labels.
  - [ ] Defect rows resolve names/severities from `DefectKnowledgeBase`.
  - [ ] No inspections produces a valid model with the empty-evidence marker.
- **Verification:**
  - [ ] `dotnet test tests/pcb-deftek.Tests/ --filter FullyQualifiedName~DashboardReportContentFactoryTests`
- **Dependencies:** Tasks 1.2, 2.2
- **Files:**
  - `Infrastructure/Export/DashboardReportContentFactory.cs`
  - `tests/pcb-deftek.Tests/DashboardReportContentFactoryTests.cs`
- **Estimated scope:** S (2 files)

#### Task 3.2: Chart SVG Renderer

- **Description:** Render three chart kinds as self-contained SVG strings sized to the available width: single-series line, dual PASS/FAIL line, and nine-class vertical bars. Use fixed hex colors, deterministic label thinning, and non-degenerate axes for 0/1/n points.
- **Acceptance criteria:**
  - [ ] Output is valid XML with a `viewBox`, no scripts, no external resources.
  - [ ] Label step never overlaps labels at the report width; the rule is unit-tested.
  - [ ] PASS uses `#107C41`, FAIL uses `#C42B1C`, bar fills use `DefectClassInfo.ColorHex`.
  - [ ] All nine bars appear in class order, including zero values.
  - [ ] Empty/single-point series render without division by zero or inverted coordinates.
  - [ ] Values are XML-escaped (labels/class names cannot break the document).
- **Verification:**
  - [ ] `dotnet test tests/pcb-deftek.Tests/ --filter FullyQualifiedName~DashboardReportChartSvgRendererTests`
- **Dependencies:** Task 3.1
- **Files:**
  - `Infrastructure/Export/DashboardReportChartSvgRenderer.cs`
  - `tests/pcb-deftek.Tests/DashboardReportChartSvgRendererTests.cs`
- **Estimated scope:** M (2 files)

---

### Checkpoint 3: Content & Charts Correct
- [ ] Content parity with dashboard projections is proven by tests.
- [ ] SVG charts parse as XML and render at report width.
- [ ] No QuestPDF dependency in content/chart code.

---

### Task Group 4: PDF Document Composition

#### Task 4.1: Document Shell, Header, Metric Section

- **Description:** Implement the QuestPDF document: A4 portrait, margins, header/filter summary, four metric blocks with SVG charts, and footer page numbering.
- **Acceptance criteria:**
  - [ ] Header renders title, generated timestamp, and filter summary.
  - [ ] Four blocks render in fixed canonical order, each with title/value/caption/chart.
  - [ ] Footer shows `Page x of y`.
  - [ ] Empty statistics render zero values and empty axes, not exceptions.
  - [ ] Document composition has no UI-thread dependency.
- **Verification:**
  - [ ] `dotnet test tests/pcb-deftek.Tests/ --filter FullyQualifiedName~DashboardReportDocumentTests`
  - [ ] Generated PDF starts with `%PDF-` and is non-trivial in size.
- **Dependencies:** Tasks 3.1, 3.2
- **Files:**
  - `Infrastructure/Export/DashboardReportDocument.cs`
  - `tests/pcb-deftek.Tests/DashboardReportDocumentTests.cs`
- **Estimated scope:** M (2 files)

#### Task 4.2: Evidence Blocks & Missing-Image Fallback

- **Description:** Compose evidence pages: inspection heading, metadata line, annotated image, defect table, and the `Image unavailable` fallback when staging failed or the source is missing.
- **Acceptance criteria:**
  - [ ] Evidence section starts on a new page and lists newest-first.
  - [ ] Each block shows name, local timestamp, mode, PASS/FAIL, and defect count.
  - [ ] Defect table rows match the model (abbrev, display name, confidence %, severity).
  - [ ] Missing image renders the fallback block plus the defect table; generation continues.
  - [ ] Zero-defect inspections show image + metadata only.
  - [ ] Empty evidence renders the documented note.
- **Verification:**
  - [ ] `dotnet test tests/pcb-deftek.Tests/ --filter FullyQualifiedName~DashboardReportDocumentTests`
- **Dependencies:** Task 4.1
- **Files:**
  - `Infrastructure/Export/DashboardReportDocument.cs`
  - `tests/pcb-deftek.Tests/DashboardReportDocumentTests.cs`
- **Estimated scope:** M (2 files)

#### Task 4.3: Real PDF Smoke Test

- **Description:** Generate a document with mixed PASS/FAIL data, all nine classes, missing image, and multi-page evidence into a temp directory; assert the file signature, non-zero size, no staged leftovers, and cleanup.
- **Acceptance criteria:**
  - [ ] Test writes and deletes only under its own temp root.
  - [ ] PDF signature is `%PDF-` and size exceeds a minimal threshold.
  - [ ] Generation succeeds with 0 defects and with 9 classes present.
  - [ ] No file handle leaks (temp folder deletes cleanly).
- **Verification:**
  - [ ] `dotnet test tests/pcb-deftek.Tests/ --filter FullyQualifiedName~DashboardPdfExportServiceTests`
- **Dependencies:** Task 4.2
- **Files:**
  - `tests/pcb-deftek.Tests/DashboardPdfExportServiceTests.cs`
- **Estimated scope:** S (1 file)

---

### Checkpoint 4: Report Renders Correctly
- [ ] A real PDF opens and contains all sections.
- [ ] Missing-image and empty-result paths are exercised.
- [ ] Generation is measured once for a 100-inspection corpus.

---

### Task Group 5: Export Orchestration & Dashboard Integration

#### Task 5.1: `DashboardPdfExportService`

- **Description:** Implement the orchestrator: resolve/validate destination, query statistics and evidence, stage annotated PNGs one at a time with progress, compose the PDF off the UI thread, move atomically, toast on success/failure, and clean up in `finally`.
- **Acceptance criteria:**
  - [ ] Destination comes from `IUserSettingsService.ReportExportPath`; blank falls back to `KnownFolderPaths.GetReportExportDirectory()`.
  - [ ] Missing directory is created; filename policy and collision suffixes are unit-tested.
  - [ ] Annotation uses `IImageAnnotationService` with a bounded `maxDimension`; source images are only read.
  - [ ] PNG encoding works off the UI thread with frozen bitmaps.
  - [ ] Progress: 0/10/15/15–95/100 as specified; `Completed` only after the final move.
  - [ ] Failure/cancellation deletes the temp PDF and staging folder; no toast on cancellation.
  - [ ] Concurrent calls are serialized or rejected; no interleaved writes.
  - [ ] Exceptions are wrapped in the typed export exception after logging.
- **Verification:**
  - [ ] `dotnet test tests/pcb-deftek.Tests/ --filter FullyQualifiedName~DashboardPdfExportServiceTests`
- **Dependencies:** Tasks 2.1, 4.3
- **Files:**
  - `Infrastructure/Export/DashboardPdfExportService.cs`
  - `tests/pcb-deftek.Tests/DashboardPdfExportServiceTests.cs`
- **Estimated scope:** M (2 files)

#### Task 5.2: Dashboard ViewModel Cutover

- **Description:** Inject `IDashboardExportService`, delete `ExportRequested`, make `RequestExportCommand` async with a running guard, auto-open the progress modal, and surface failures through the existing progress/notification paths.
- **Acceptance criteria:**
  - [ ] `ExportRequested` and its event-based tests are removed.
  - [ ] Command validates the filter, is disabled/ignored while exporting, and resumes afterward.
  - [ ] Export passes an immutable filter snapshot and a cancellation token tied to VM disposal/navigation.
  - [ ] Modal opens on start and shows `DataExport` progress state.
  - [ ] Success/failure does not throw on the dispatcher; failures set a visible state.
- **Verification:**
  - [ ] `dotnet test tests/pcb-deftek.Tests/ --filter FullyQualifiedName~DashboardViewModelTests`
- **Dependencies:** Task 5.1
- **Files:**
  - `Presentation/ViewModels/DashboardViewModel.cs`
  - `tests/pcb-deftek.Tests/DashboardViewModelTests.cs`
- **Estimated scope:** M (2 files)

#### Task 5.3: DI Registration & Resolution

- **Description:** Register the export service and configuration dependency; assert lifetimes and resolution in the real provider.
- **Acceptance criteria:**
  - [ ] `IDashboardExportService` resolves with all dependencies.
  - [ ] Registration uses the existing lifetime convention (singleton for stateless services).
  - [ ] `DashboardViewModel` still resolves as transient.
- **Verification:**
  - [ ] `dotnet test tests/pcb-deftek.Tests/ --filter FullyQualifiedName~DependencyInjectionTests`
- **Dependencies:** Tasks 5.1, 5.2
- **Files:**
  - `App.xaml.cs`
  - `tests/pcb-deftek.Tests/DependencyInjectionTests.cs`
- **Estimated scope:** S (2 files)

---

### Checkpoint 5: Export Works End-to-End
- [ ] Dashboard Export button produces a PDF respecting the active filter.
- [ ] Progress modal reflects running/completed/failed states.
- [ ] Default and configured destinations both work.
- [ ] Review with human before BDD.

---

### Task Group 6: BDD, Documentation & Final Gates

#### Task 6.1: Export BDD Scenarios

- **Description:** Add Reqnroll coverage for the primary export flow using isolated SQLite, temp report directory, and a stub notification service.
- **Acceptance criteria:**
  - [ ] Scenario: filter → export → file exists at the configured path and notification fired.
  - [ ] Scenario: blank report path resolves to `Downloads/PCB Deftek/Report` (temp-rooted resolver).
  - [ ] Scenario: no matches still generates a report with zeros.
  - [ ] BDD never writes to the developer's real Downloads folder or database.
- **Verification:**
  - [ ] `dotnet test tests/pcb-deftek.Bdd/ --filter "FullyQualifiedName~DashboardExportFeature"`
  - [ ] Full suite passes: `dotnet test pcb-deftek.slnx`
- **Dependencies:** Task Group 5
- **Files:**
  - `tests/pcb-deftek.Bdd/Features/AuditPdfExport.feature`
  - `tests/pcb-deftek.Bdd/StepDefinitions/DashboardExportStepDefinitions.cs`
- **Estimated scope:** M (2 files)

#### Task 6.2: Documentation & Plan Status

- **Description:** Record completion, deviations, and verification results; align roadmap/mission/tech-stack wording if implementation reveals a changed decision.
- **Acceptance criteria:**
  - [ ] Plan checkboxes reflect actual state; deferred items remain in requirements.
  - [ ] Validation records automated and manual results, including the license decision.
  - [ ] No unchecked task is silently skipped.
- **Verification:**
  - [ ] `git diff` shows only status/documentation edits in this specification.
- **Dependencies:** Task 6.1
- **Files:**
  - `specs/2026-11-14-audit-ready-pdf-export-engine/plan.md`
  - `specs/2026-11-14-audit-ready-pdf-export-engine/validation.md`
- **Estimated scope:** XS (2 files)

---

### Checkpoint 6: Merge Readiness
- [ ] All six task groups complete.
- [ ] Solution builds with 0 warnings.
- [ ] All unit, integration, and BDD tests pass (`dotnet test pcb-deftek.slnx`).
- [ ] PR linked to its issue and passes the 4-step Feature Review and Quality Improvement pipeline.
- [ ] QuestPDF license eligibility confirmed and recorded.
- [ ] Human review approved; PR not merged without approval.

---

## Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| QuestPDF license not eligible for Community | High | Confirm before merge; changing `LicenseType` is the only code impact. Do not ship under Evaluation. |
| Large exports (hundreds/thousands of images) | High | D8 keeps all evidence; one-image staging bounds memory; cancellation plumbing; validation §4 measures 100 inspections and records the curve. |
| Partial/corrupt PDF after failure | High | Temp file + atomic move + `finally` cleanup; failure-path tests. |
| Evidence and aggregates diverge | High | Shared predicate helpers + parity tests (`Count == TotalInspections`, defect sums). |
| QuestPDF layout exceptions on extreme aspect ratios | Medium | `FitArea`/`FitWidth` with bounded image dimensions; missing-image fallback; smoke tests with mixed data. |
| WPF encoding threaded incorrectly | Medium | Annotation returns frozen bitmaps; PNG encoding on worker; test generation off the dispatcher. |
| Report values drift from dashboard formatting | Medium | Content factory parity test against `DashboardViewModel` projections for identical statistics. |
| SVG labels overlap or break XML | Medium | Deterministic thinning + escaping tests; fixed viewBox. |
| Progress updates after modal closed/disposed | Low | Existing `DashboardProgressService` marshals to UI context; no new dispatch model. |
| Constructor change breaks existing tests/BDD | Low | Update fakes and DI tests in the same task; full suite gate. |
| Culture differences in labels/numbers | Low | Capture `CurrentCulture` explicitly; fixed-culture tests. |
| Temp staging disk usage | Medium | Delete in `finally`; one image at a time; max dimension bounds each PNG. |

## Open Questions

1. **QuestPDF license eligibility** (Community vs Professional/Enterprise) — must be confirmed by the user before merge.
2. **Evidence volume** — D8 exports every matching inspection. Revisit only if validation §4 shows unacceptable export time on realistic data; no cap is planned now.
