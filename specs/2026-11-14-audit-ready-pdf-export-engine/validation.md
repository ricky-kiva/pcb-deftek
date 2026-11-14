# Validation Criteria: Phase 9 — Audit-Ready PDF Export Engine

## 1. Build & Quality Gates

| Gate | Command | Pass condition |
|---|---|---|
| Restore | `dotnet restore` | Completes; QuestPDF 2026.9.2 restored from NuGet |
| Build | `dotnet build pcb-deftek.slnx` | 0 errors, **0 warnings** |
| Nullable | Build output | No nullable warnings introduced |
| Dependencies | `git diff` on `pcb-deftek.csproj` | QuestPDF is the only new package; version pinned |
| Licensing | Live PDF generation | `QuestPDF.Settings.License` configured before generation; no Evaluation license in production code |
| Hygiene | `git diff` | No absolute user paths, no secrets, no generated PDFs committed |

## 2. Automated Test Gates

Baseline before Phase 9: 729 passing tests (692 unit + 37 BDD), 0 failed, 0 skipped. Phase 9 must keep that set green and add the coverage below.

### 2.1 Required Unit / Integration Coverage

| Area | Required assertions |
|---|---|
| Filtered evidence query | Evidence count equals `DashboardStatistics.TotalInspections` for all-time, date-only, class-only, severity-only, and combined filters; defect count equals `TotalDefects`; PASS excluded under defect filters; deterministic newest-first ordering |
| Content factory | Four canonical blocks; values/captions match dashboard projections; filter summaries for restricted/unrestricted cases; defect rows resolve from `DefectKnowledgeBase`; PASS-only and empty cases |
| Chart SVG | Valid XML with `viewBox`; no external refs; 0/1/n points; nine bars in order including zeros; label thinning; XML escaping |
| Path & filename | Configured path honored; blank falls back to `KnownFolderPaths.GetReportExportDirectory()` (`Downloads\PCB Deftek\Report`); timestamped filename; `-2`/`-3` collisions; directory creation |
| Export service | Real PDF generated off the dispatcher; `%PDF-` signature; temp/staging cleanup on success, failure, and cancellation; no toast on cancellation; failed state message recorded; source images unmodified |
| Dashboard VM | Async export invocation with immutable filter snapshot; concurrent invocation ignored; modal opens; command re-enabled after completion/failure; `ExportRequested` removed |
| DI | Export service resolves with all dependencies; lifetimes asserted; `DashboardViewModel` still transient |

### 2.2 Required BDD Coverage

| Scenario | Assertion |
|---|---|
| Export filtered dashboard data | PDF exists at the configured temp path; notification fired with the path; progress reaches `Completed` |
| Default destination | Blank `ReportExportPath` resolves under the temp-rooted `Downloads/PCB Deftek/Report` |
| No matching inspections | PDF still generated; zeros rendered; no evidence pages beyond the note |

BDD runs against an isolated SQLite database and temp artifact roots; it must never touch the developer's real `%LOCALAPPDATA%` database or Downloads folder.

### 2.3 Full-Suite Gate

- `dotnet test pcb-deftek.slnx` → 100 % pass, 0 skipped, no regression against the 729-test baseline.

## 3. Functional Acceptance Criteria

### 3.1 Export Trigger & Lifecycle

- [ ] Dashboard "Export" validates the active filter and starts an export when valid.
- [ ] Invalid custom date ranges do not start an export (unchanged Phase 8 behavior).
- [ ] Export command is disabled/ignored while an export is running.
- [ ] Progress modal opens automatically; `DataExport` transitions `Idle → Running → Completed` (or `Failed`).
- [ ] Success toast states the saved path; failure toast states failure.
- [ ] No exception reaches the dispatcher, and no UI thread block is observed.

### 3.2 Report Content

- [ ] Header shows title, generated local timestamp, and filter summary (date range, classes, severities).
- [ ] Four metric blocks appear in fixed order: PCBs Inspected, Defects Detected, Inspection Rate, Defect Distribution.
- [ ] Each block shows title, value, caption, and chart; values use `CurrentCulture` formatting.
- [ ] Report totals equal the dashboard totals for the same filter.
- [ ] Footer shows `Page x of y`.
- [ ] Evidence section starts on a new page and lists inspections newest first.
- [ ] Each inspection block shows name, local timestamp, mode, PASS/FAIL, defect count, annotated image, and defect table.
- [ ] PASS inspections show image + metadata only.
- [ ] Empty result generates a PDF with zeros and the documented note.

### 3.3 Charts

- [ ] PCBs/Defects charts follow the adaptive bucket granularity from `DashboardStatistics`.
- [ ] PASS/FAIL uses green `#107C41` and red `#C42B1C`.
- [ ] Distribution shows all nine class abbreviations in taxonomy order with `DefectClassInfo.ColorHex` fills.
- [ ] X labels do not overlap at report width; zero/single-point data renders sensibly.

### 3.4 Evidence Images

- [ ] Bounding boxes and labels match the defect rows (same color source as the viewer).
- [ ] Source images are read-only; `_annotated.png` sidecars are not required or modified.
- [ ] Missing/unreadable image renders `Image unavailable` and the defect table; export succeeds.
- [ ] Image pixel dimensions are bounded by the configured `maxDimension`.

### 3.5 Destination & Notification

- [ ] Configured `ReportExportPath` is used when non-blank.
- [ ] Blank path resolves to `%USERPROFILE%\Downloads\PCB Deftek\Report`.
- [ ] Directory is created when missing.
- [ ] Filename is `PCB-Deftek-Report-yyyyMMdd-HHmmss.pdf` (local time); collisions get `-2`, `-3`, …
- [ ] Toast is shown only after the PDF is fully written.

### 3.6 Failure & Cancellation

- [ ] Unwritable destination → `Failed` state, failure toast, Serilog error, no partial PDF, staging removed.
- [ ] Corrupt/missing evidence image does not abort the export.
- [ ] Cancellation leaves no PDF or temp artifacts and does not emit a completion toast.

## 4. Performance & Stability Gates

Measure on the development machine with the release build; record numbers in validation §9 after implementation.

| Metric | Target |
|---|---|
| Export of 100 inspections (each 1–3 defects) | ≤ 60 s wall clock; progress advances to 100 % |
| UI responsiveness during export | Dashboard remains interactive; no dispatcher stall > 100 ms observed |
| Peak memory increase | ≤ 250 MB over pre-export working set; bounded by one staged image at a time |
| PDF size | ≤ 25 MB for 100 inspections at the bounded image dimension |
| Repeated exports | Three consecutive exports succeed; temp staging folders do not accumulate |
| Failure cleanup | Temp PDF/staging removed in all failure and cancellation paths |

## 5. Manual Verification Script

1. Launch the app, open Dashboard, leave filters at default (all time), click **Export**.
2. Confirm the progress modal opens and advances; confirm the completion toast.
3. Open the PDF from the toast path; verify header, filter summary, four metric cards in one column, page footer, and evidence pages.
4. Compare each card's totals against the dashboard values.
5. Apply Day range + one defect class + one severity; export again; confirm totals and evidence match the filtered dashboard.
6. Clear filters; apply a custom range with start > end; confirm no export starts.
7. Temporarily rename a referenced source image; export; confirm `Image unavailable` appears and the export completes.
8. Change the report path in Settings to a writable custom folder; export; confirm the file lands there.
9. Revert the report path to blank; export; confirm `Downloads\PCB Deftek\Report` is used.
10. Repeat step 1 twice quickly; confirm the second click does not start a parallel export.
11. Point the report path at a read-only/invalid location; export; confirm failure toast and that no `.pdf`/`.tmp` remains.
12. Confirm the source image files' timestamps and contents are unchanged.

## 6. Edge-Case Verification

| Case | First check |
|---|---|
| PDF empty/corrupt | Temp file moved before write completed, or staging cleanup deleted too early |
| Totals mismatch dashboard | Evidence query predicate drift from `GetStatisticsAsync` |
| Severity filter returns nothing | Severity→class-code mapping in `DefectKnowledgeBase` |
| Charts blank | Empty series path, zero-value bars, or SVG viewBox sizing |
| Labels overlap | Label-thinning step and report content width |
| Missing image aborts export | Annotation failure path must catch/log, not rethrow |
| File name collision overwrites | Collision suffix loop / `File.Move` overwrite flag |
| Toast without file | Notification raised before the atomic move |
| Progress stuck below 100 % | Per-image progress formula with zero images |
| Temp folders accumulate | Cleanup in `finally`, including cancellation |
| Export runs twice | Command guard or service semaphore missing |
| UI freeze | Annotation/PNG/PDF work moved to the UI thread |
| PDF bloated | `maxDimension` not applied or PNG instead of bounded resize |

## 7. Merge Readiness & Definition of Done

Phase 9 may merge into `main` only when:

1. All six task groups in `plan.md` are implemented and checked.
2. `dotnet build pcb-deftek.slnx` succeeds with 0 errors and 0 warnings.
3. `dotnet test pcb-deftek.slnx` passes 100 % (no skipped tests), with no regression against the 729-test baseline.
4. All functional acceptance criteria in §3 are verified; performance gates in §4 are met or explicitly waived with recorded numbers.
5. QuestPDF license eligibility is confirmed and the selected `LicenseType` is recorded (Community confirmed, or a commercial license purchased).
6. QuestPDF is the only new dependency; no schema migration, no new settings keys, no hardcoded user paths.
7. The report is generated off the UI thread and never modifies user-owned source images.
8. Clean Git history following the 6-step lifecycle in `AGENTS.md`.
9. PR linked to its issue and passes the 4-step Feature Review and Quality Improvement pipeline.
10. Human review approval is recorded before merge; the PR is not self-merged.

## 8. Failure Triage

| Symptom | First check |
|---|---|
| Build fails on QuestPDF types | Package version/`net10.0-windows` compatibility and `using QuestPDF.*` placement |
| QuestPDF license exception | `LicenseType` configuration executed before the first `Document.Create` |
| Export command does nothing | Filter validation result; command running guard; DI resolution of `IDashboardExportService` |
| PDF exists but is empty | Document container composed with no children, or generation stream closed early |
| Evidence pages missing | `GetMatchingInspectionsAsync` predicate/parameter binding |
| Images reversed order | Repository `ORDER BY CreatedAtUtc DESC` tiebreaker |
| Boxes offset | Annotation receives normalized boxes; source dimensions mismatch/corrupt file |
| File saved to wrong folder | `ReportExportPath` load, blank fallback, `Path.GetFullPath` |
| Toast never appears | Notification service exception swallowed; success path not reached |
| Progress never completes | Exception thrown after composition but before `Completed`; staging cleanup path |
| Concurrent files / same name | Collision suffix or service semaphore |
| Memory spikes on large export | Staging one image at a time; byte[] retained across pages |
| UI freezes during export | Synchronous annotation/encode/generate on the dispatcher |
