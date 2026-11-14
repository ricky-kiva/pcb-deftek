# Validation Criteria: Phase 9 — Audit-Ready PDF Export Engine

## 1. Build & Quality Gates

| Gate | Command | Pass condition |
|---|---|---|
| Restore | `dotnet restore` | Completes; QuestPDF 2026.9.2 restored from NuGet |
| Build | `dotnet build pcb-deftek.slnx` | 0 errors, **0 warnings** |
| Nullable | Build output | No nullable warnings introduced |
| Dependencies | `git diff` on `pcb-deftek.csproj` | QuestPDF is the only new package; version pinned |
| Licensing | Live PDF generation | `LicenseType.Community` configured before generation; non-profit eligibility recorded here; no Evaluation license in production code |
| Hygiene | `git diff` | No absolute user paths, no secrets, no generated PDFs committed |

## 2. Automated Test Gates

Baseline before Phase 9: 729 passing tests (692 unit + 37 BDD), 0 failed, 0 skipped. Phase 9 must keep that set green and add the coverage below.

### 2.1 Required Unit / Integration Coverage

| Area | Required assertions |
|---|---|
| Process progress | Per-process tokens; `Cancel` cancels only the target; cancelling idle/completed is a no-op; `CancelAll` cancels both; `Cancelled` state; `HasActiveProcesses` notifications; fresh token per run; idle message is empty |
| Modal chrome | One status line per process; idle row hides the progress track; Cancel button visible only while running; `Processes` label/icon/automation name; dot visible only with active processes; Export icon present |
| Batch inference wiring | Running/completed/failed progress published with processed/total; dashboard cancel cancels the batch; local cancel marks the modal `Cancelled`; live camera does not publish |
| Filtered evidence query | Evidence count equals `GetMatchingInspectionCountAsync` equals `GetStatisticsAsync().TotalInspections` for all-time, date, class, severity, and combined filters; defect sums equal `TotalDefects`; deterministic newest-first ordering |
| Content factory | Four canonical blocks; values/captions match dashboard projections; filter summaries restricted/unrestricted; defect rows have class/confidence/severity and **no abbreviation**; no evidence yields no document |
| Chart SVG | Valid XML with `viewBox`; no external refs; 0/1/n points; nine bars in order including zeros; label thinning; XML escaping |
| Path & filename | Configured path honored; blank falls back to `KnownFolderPaths.GetReportExportDirectory()` (`Downloads\PCB Deftek\Report`); timestamped filename; `-2`/`-3` collisions; directory creation |
| Export preflight | 0 matches → `NoMatches`, no file, no progress; 100 matches → no prompt; 101+ → confirmation required; decline produces no file/progress |
| Export service | Real PDF generated off the dispatcher; `%PDF-` signature; temp/staging cleanup on success, failure, and cancellation; `Cancelled` state and no completion toast; source images unmodified; concurrent calls serialized |
| Invalid-image cleanup (report) | Missing/corrupt image deletes the record and app-owned captures; user originals untouched; deleted inspection excluded; export continues; environmental unavailability skips deletion |
| Invalid-image cleanup (History) | Undecodable thumbnails remove cards and records; totals/groups/selection refreshed; one toast with the count; no placeholder left behind; environmental unavailability skips deletion |
| Dashboard VM | Async export with immutable filter snapshot; preflight toast/no-match; confirmation above threshold; cancel command; modal auto-open; command guard; `ExportRequested` removed |
| DI | Export service resolves; progress service singleton; `DashboardViewModel` transient; no circular dependencies |

### 2.2 Required BDD Coverage

| Scenario | Assertion |
|---|---|
| Export filtered dashboard data | PDF exists at the configured temp path; notification fired with the path; progress reaches `Completed` |
| Default destination | Blank `ReportExportPath` resolves under the temp-rooted `Downloads/PCB Deftek/Report` |
| No matching inspections | No PDF; no modal; no-match toast fired |
| Large export confirmation | >100 matches prompts; declining produces no file; accepting writes the report |
| Cancel during export | Modal cancel yields `Cancelled`, no PDF, staging removed |
| Invalid evidence image | Record deleted; export completes; remaining evidence rendered |
| Invalid History image | Card removed after hydration; record deleted; count refreshed |

BDD runs against an isolated SQLite database and temp artifact roots; it must never touch the developer's real `%LOCALAPPDATA%` database or Downloads folder.

### 2.3 Full-Suite Gate

- `dotnet test pcb-deftek.slnx` → 100 % pass, 0 skipped, no regression against the 729-test baseline.

## 3. Functional Acceptance Criteria

### 3.1 Export Trigger & Preflight

- [ ] Dashboard "Export" validates the active filter and starts an export when valid.
- [ ] Invalid custom date ranges do not start an export (unchanged Phase 8 behavior).
- [ ] Zero matching inspections → Windows toast; no PDF, no progress, no modal.
- [ ] More than 100 matching inspections → confirmation dialog with the count; declining aborts everything.
- [ ] 100 or fewer matching inspections → export starts without a prompt.
- [ ] Export command is disabled/ignored while an export is running.
- [ ] Export icon appears before the button text.

### 3.2 Background Activity Modal

- [ ] `Alerts` is renamed `Processes`; activity icon replaces the bell; tooltip and automation name updated.
- [ ] A notification dot overlays the Processes icon while any process is running.
- [ ] Each process row has a Cancel button, enabled only while that process is running.
- [ ] Cancel requests cancellation for report generation, inference, and data export through the same mechanism.
- [ ] Each row displays exactly one status line; `Idle` appears once.
- [ ] No progress track is drawn in the idle state; nothing reads as a separator between title and status.
- [ ] `Running`, `Completed`, `Failed`, and `Cancelled` states render correctly.
- [ ] Closing the application during an export cancels it; no PDF or staging artifacts remain.

### 3.3 Report Content

- [ ] Header shows title, generated local timestamp, and filter summary (date range, classes, severities).
- [ ] Four metric blocks appear in fixed order: PCBs Inspected, Defects Detected, Inspection Rate, Defect Distribution.
- [ ] Each block shows title, value, caption, and chart; values use `CurrentCulture` formatting.
- [ ] Report totals equal the dashboard totals for the same filter.
- [ ] Footer shows `Page x of y`.
- [ ] Evidence section starts on a new page and lists valid inspections newest first.
- [ ] Each inspection block shows name, local timestamp, mode, PASS/FAIL, defect count, annotated image, and defect table.
- [ ] Annotated image width never exceeds 40 % of the content width.
- [ ] Defect table has exactly three columns: Class, Confidence, Severity.
- [ ] PASS inspections show image + metadata only.

### 3.4 Charts

- [ ] PCBs/Defects charts follow the adaptive bucket granularity from `DashboardStatistics`.
- [ ] PASS/FAIL uses green `#107C41` and red `#C42B1C`.
- [ ] Distribution shows all nine class abbreviations in taxonomy order with `DefectClassInfo.ColorHex` fills.
- [ ] X labels do not overlap at report width; zero/single-point data renders sensibly.

### 3.5 Evidence Images & Invalid Records

- [ ] Bounding boxes and labels match the defect rows (same color source as the viewer).
- [ ] Source images are read-only; `_annotated.png` sidecars are not required or modified.
- [ ] Missing/unreadable/corrupt image → no image rendered, record deleted, inspection excluded from the report, export continues.
- [ ] History gallery: missing/unreadable/corrupt image → no placeholder card, record deleted, gallery refreshed.
- [ ] Deletion removes the record and app-owned captures; user-owned originals are untouched.
- [ ] Dashboard totals refresh after an export that removed invalid records.
- [ ] Deletion is skipped and logged when the image root is inaccessible (no mass cleanup).

### 3.6 Destination, Notification & Cancellation

- [ ] Configured `ReportExportPath` is used when non-blank.
- [ ] Blank path resolves to `%USERPROFILE%\Downloads\PCB Deftek\Report`.
- [ ] Directory is created when missing.
- [ ] Filename is `PCB-Deftek-Report-yyyyMMdd-HHmmss.pdf` (local time); collisions get `-2`, `-3`, …
- [ ] Toast is shown only after the PDF is fully written.
- [ ] Modal Cancel and application exit both leave no PDF, temp file, or staging folder.
- [ ] No completion or failure toast is shown for an intentionally cancelled export.

## 4. Performance & Stability Gates

Measure on the development machine with the release build; record numbers in validation §9 after implementation.

| Metric | Target |
|---|---|
| Export of 100 inspections (each 1–3 defects) | ≤ 60 s wall clock; progress advances to 100 % |
| Large-export confirmation threshold | Triggered at 101+ matches; never blocks 1–100 |
| Cancellation latency | `Cancelled` state and cleanup within 2 s of the modal request (or after the current image/phase boundary) |
| UI responsiveness during export | Dashboard remains interactive; no dispatcher stall > 100 ms observed |
| Peak memory increase | ≤ 250 MB over pre-export working set; bounded by one staged image at a time |
| PDF size | ≤ 25 MB for 100 inspections at the bounded image dimension |
| Repeated exports | Three consecutive exports succeed; temp staging folders do not accumulate |
| Failure/cancel cleanup | Temp PDF/staging removed in all failure and cancellation paths |
| App-exit cleanup | No orphaned report temp files after closing mid-export |

## 5. Manual Verification Script

1. Launch the app, open Dashboard, leave filters at default, click **Export**.
2. Confirm the progress modal opens, the Cancel button appears while running, and the completion toast fires.
3. Open the PDF from the toast path; verify header, filter summary, four metric cards in one column, evidence images at ≤ 40 % width, three-column defect tables, and page footer.
4. Compare each card's totals against the dashboard values.
5. Apply filters that match nothing; click **Export**; confirm the no-match toast and that no PDF/modal appears.
6. Seed or select a filter with > 100 inspections; click **Export**; confirm the warning, decline once, then accept and export.
7. Start a large export; click **Cancel** in the Background Activity modal; confirm `Cancelled`, no PDF, and no temp/staging files.
8. Start a large export; close the application; confirm no orphaned `.tmp`/staging files.
9. Start a batch detection; confirm the Inference row shows running progress; cancel from both the File Detection button and the dashboard modal.
10. Confirm the Processes button shows the dot only while a process runs; confirm `Idle` appears once per row and no idle separator line.
11. Temporarily rename a referenced source image; export; confirm the record is removed, the report skips it, and the export completes.
12. Open History after a missing/corrupt image; confirm the card is removed, the record is gone, and the count updates.
13. Point the report path at a read-only/invalid location; export; confirm failure toast and no `.pdf`/`.tmp` remains.
14. Confirm source image files' timestamps and contents are unchanged.

## 6. Edge-Case Verification

| Case | First check |
|---|---|
| PDF empty/corrupt | Temp file moved before write completed, or staging cleanup deleted too early |
| Totals mismatch dashboard | Evidence/count predicate drift from `GetStatisticsAsync` |
| Severity filter returns nothing | Severity→class-code mapping in `DefectKnowledgeBase` |
| Charts blank | Empty series path, zero-value bars, or SVG viewBox sizing |
| Labels overlap | Label-thinning step and report content width |
| Image exceeds 40 % width | Evidence block sizing rule / aspect preservation |
| Abbreviation column returns | Stale table projection or model row still carries the abbreviation |
| Record not deleted | Deletion service not invoked, environmental guard skipped, or transaction failed |
| Too many records deleted | Mass cleanup on inaccessible volume; verify parent-directory guard |
| No-match exports a file | Preflight skipped; exporter `NoMatches` path creates a document |
| Confirmation missing above 100 | Count query/predicate mismatch or threshold comparison off by one |
| Toast without file | Notification raised before the atomic move |
| Progress stuck below 100 % | Per-image progress formula with zero images |
| Cancel does nothing | Wrong process token passed, or generation not checking the token |
| Temp folders accumulate | Cleanup missing in `finally`/`Dispose`; cancellation path untested |
| Export runs twice | Command guard or service semaphore missing |
| UI freeze | Annotation/PNG/PDF work moved to the UI thread |
| PDF bloated | `maxDimension` not applied or PNG instead of bounded resize |
| Idle row shows twice | Status message still defaults to the status name; hidable message not bound |

## 7. Merge Readiness & Definition of Done

Phase 9 may merge into `main` only when:

1. All seven task groups in `plan.md` are implemented and checked.
2. `dotnet build pcb-deftek.slnx` succeeds with 0 errors and 0 warnings.
3. `dotnet test pcb-deftek.slnx` passes 100 % (no skipped tests), with no regression against the 729-test baseline.
4. All functional acceptance criteria in §3 are verified; performance gates in §4 are met or explicitly waived with recorded numbers.
5. QuestPDF is the only new dependency, and the Community license is recorded as valid because the project is non-profit.
6. No database migration is introduced; no new settings keys; no hardcoded user paths.
7. The report is generated off the UI thread and never modifies user-owned source images.
8. Invalid-image deletion is proven by tests for both the report and History, including the environmental skip path.
9. All open questions are resolved (license, evidence volume) and none remain in requirements/plan.
10. Clean Git history following the 6-step lifecycle in `AGENTS.md`.
11. PR linked to its issue and passes the 4-step Feature Review and Quality Improvement pipeline.
12. Human review approval is recorded before merge; the PR is not self-merged.

## 8. Failure Triage

| Symptom | First check |
|---|---|
| Build fails on QuestPDF types | Package version/`net10.0-windows` compatibility and `using QuestPDF.*` placement |
| QuestPDF license exception | `LicenseType.Community` configured before the first `Document.Create` |
| Export command does nothing | Filter validation, preflight count, command guard, DI resolution |
| No-match toast not shown | `Dictionary` of result outcomes/`NoMatches` handling in the view model |
| Confirmation dialog skipped | Threshold constant, count query, `IConfirmationService` registration |
| Confirm decline still exports | Export call placed outside the confirm result branch |
| Cancel button missing/disabled | `Running` binding, process identity mismatch, stale token |
| Cancel doesn't stop export | Generation not receiving the DataExport token; token renewed mid-run |
| Idle text duplicated | `DashboardProgressState` message default not cleared; message TextBlock not hidden |
| Idle separator still visible | Progress track not collapsed for `Idle` |
| Dot always/never visible | `HasActiveProcesses` change notifications or binding path |
| History card deleted unexpectedly | Hydration deleting before retry/guard; check environmental skip |
| Report evidence still shows broken image | Staging result ignored or deletion path excluded only the image, not the block |
| File saved to wrong folder | `ReportExportPath` load, blank fallback, `Path.GetFullPath` |
| Toast never appears | Notification service exception swallowed; success path not reached |
| Progress never completes | Exception after composition but before `Completed`; cleanup path |
| Concurrent files / same name | Collision suffix or service semaphore |
| Memory spikes on large export | Staging one image at a time; byte[] retained across pages |
| UI freezes during export | Synchronous annotation/encode/generate on the dispatcher |
| App exit leaves files | `CancelAll` not called, `Dispose` cleanup missing, or process killed before cleanup |
