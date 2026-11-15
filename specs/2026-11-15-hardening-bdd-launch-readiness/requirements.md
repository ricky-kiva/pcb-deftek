# Requirements & Context: Phase 10 — Hardening, BDD Verification & Launch Readiness

## 1. Executive Summary & Context

Phases 1–9 delivered every essential feature in `mission.md` §5.1. Phase 9 closed with 834 passing tests (790 unit, 44 BDD), 0 warnings, a real PDF export engine, and 10 Reqnroll features covering the main flows. What is still missing is the evidence and machinery that turns "works on the developer's machine" into "verifiable and distributable":

- **No CI.** `.github/workflows` does not exist; nothing enforces the 0-warning build or the test suite between merges.
- **Partial BDD for the workflows named in `roadmap.md`.** Dashboard drag-and-drop is only exercised through a view-model command, not the drop path; the viewer's focused→default transition is untested; export failure notification is untested.
- **No repeatable performance measurement.** Phase 9 measured export performance with a throwaway harness that was deleted. Live-camera memory footprint and UI responsiveness under continuous inference have never been measured with a retained tool.
- **One known launch-blocking startup defect.** A database migration failure is rethrown from `async void App.OnStartup`; the dispatcher handler marks the exception handled, so the process can stay alive with no window.
- **No release identity or launch artifact.** There is no version metadata, changelog, packaging command, or launch checklist; the README is a 12-byte stub.

Phase 10 closes exactly those gaps. It adds no user-facing feature and no new dependency.

### Current State (verified in code and repository)

| Concern | Existing behavior | Evidence |
|---|---|---|
| CI/CD | None. No workflow files anywhere in the repository. | Repository listing; `find` for `*.yml`/`*.yaml` returns nothing |
| Build gate | 0-warning build recorded for Phase 9, enforced only by the developer | `specs/2026-11-14-audit-ready-pdf-export-engine/validation.md` §1, §9 |
| Test baseline | 834 passing (790 unit + 44 BDD), 0 skipped | Phase 9 `validation.md` §2.2 |
| BDD features | 10 features / 44 scenarios, isolated temp databases and temp artifact roots | `tests/pcb-deftek.Bdd/Features/` |
| Batch ETA | Covered: `Ingest multiple PCB images and verify progress with inline ETA` asserts an estimated completion time was calculated | `FileBatchDetection.feature` |
| Camera auto-hide | Covered: `Stationary cursor auto-hides configuration controls while preserving inspection status` | `LiveCameraInspection.feature` |
| Viewer sidebar | Default state and focused state covered; no scenario returns to default | `HistoryAndDefectViewer.feature` |
| Dashboard drag-and-drop | View-model `MoveCardToAsync` covered indirectly by a "move second card left" scenario; `DragDrop` payload creation and `OnCardDrop` resolution untested | `DashboardView.xaml.cs` (`CardDragFormat`, `OnCardDrop`), `DefectStatisticsDashboard.feature` |
| Export notifications | Success (`Report exported`) and no-match covered; failure branch (`Report export failed`) untested | `AuditPdfExport.feature`, `DashboardPdfExportService.cs:274` |
| Live telemetry | `LiveInspectionTelemetry` exposes inference/avg latency, rolling FPS, device | `Domain/Models/LiveInspectionTelemetry.cs` |
| Memory/perf tooling | None retained; no memory metric in the app | Repository listing |
| Startup failure | Migration failure rethrown from `async void OnStartup`; dispatcher handler sets `Handled = true` | `App.xaml.cs` (`OnStartup`, `OnDispatcherUnhandledException`) |
| Notifications | Toast failures are caught and logged; never fail the caller | `WindowsNotificationService.cs` |
| Logging | Serilog rolling files, 30 retained, `%LOCALAPPDATA%\PcbDeftek\pcb-deftek\logs` | `Infrastructure/Logging/LoggingBootstrapper.cs` |
| Versioning | No `Version`/`FileVersion` in `pcb-deftek.csproj`; no tags; no changelog | `pcb-deftek.csproj`, `git tag` empty |
| Packaging | No installer/MSIX/publish profile; no application icon | Repository listing; `mission.md` §5.2 |
| Documentation | `README.md` contains only `# pcb-deftek` | `README.md` |

## 2. Scope

### 2.1 In Scope

1. **CI/CD automation** — one GitHub Actions workflow that restores, builds with warnings-as-errors, runs unit and BDD tests separately, and publishes results; repository documentation of the commands.
2. **BDD closure for roadmap-named workflows** — dashboard drag-and-drop through the drop path, viewer focus reset, export failure notification. Existing scenarios satisfy batch ETA, camera auto-hide, and export success notifications and are verified, not rewritten.
3. **Performance verification** — a retained, opt-in harness that drives the real live-inspection pipeline (real ONNX model, fake camera) and reports latency/FPS, memory footprint, and WPF dispatcher responsiveness; a recorded 10-minute baseline.
4. **Startup hardening** — a testable startup coordinator so migration failure aborts startup cleanly (log, one dialog, exit code 1, no window) and routing failure remains non-fatal.
5. **Release identity and launch readiness** — version metadata, a capability-level changelog, and a launch-readiness checklist with evidence and a Go/No-Go verdict for a portable Windows build.

### 2.2 Out of Scope (Deferred)

- New product features, UI redesign, new defect classes, or changes to the detection model.
- Database schema changes or new settings keys.
- Installer/MSIX packaging, code signing, application icon, splash branding, company identity, and GitHub Release publication (`mission.md` §5.2).
- Coverage tooling or additional test packages; no new NuGet dependency is added in this phase.
- Performance optimization of inference. Phase 10 measures and records; improvement requires its own phase.
- Multi-instance enforcement (see D15).
- Localization, telemetry/analytics, auto-update.

## 3. Decisions & Rationale

| # | Decision | Rationale |
|---|---|---|
| D1 | Phase 10 adds verification, CI, one startup-safety behavior, and release metadata only. No product behavior otherwise changes. | The roadmap defines Phase 10 as hardening and launch readiness; feature work belongs to a new phase. |
| D2 | CI is GitHub Actions on `windows-latest`, in one job, with restore → Release build (`-warnaserror`) → unit tests → BDD tests → artifact upload. | The repository is hosted on GitHub; Windows is mandatory for WPF, DirectML, and OpenCvSharp; one job keeps setup cost low while separating test projects makes failures attributable. |
| D3 | No `global.json`. The SDK is pinned by the workflow's `dotnet-version: 10.0.x`. | Avoids a project-level persistent configuration change (which `tech-stack.md` §5 requires asking for) while keeping CI deterministic. |
| D4 | No coverage package or coverage gate in Phase 10. `.trx` results are the CI evidence. | Adding `coverlet` would be a new dependency requiring `socket` verification for no launch requirement. Coverage thresholds are a separate decision. |
| D5 | CI runs the same projects the developer runs. No test category is excluded or shimmed. | All hardware access already goes through interfaces; BDD injects `FakeCameraService`, a stub detector, and temp roots, and the ONNX model is tracked in git. If CI fails, that is a real defect to fix, not to filter out. |
| D6 | Warnings are errors in CI but the repository gains no `TreatWarningsAsErrors` property. | The gate is visible and adjustable in one place; local developer builds keep the fast inner loop, and CI enforces the recorded 0-warning bar. |
| D7 | Performance tooling is a separate console project (`tests/pcb-deftek.Perf`), not an xUnit test. | Measured numbers are machine-dependent and must not gate builds; a console tool can run a 10-minute soak and emit a report. It still builds in CI. |
| D8 | The harness drives `LiveInspectionEngine` with the real ONNX model on the CPU provider and a fake camera emitting the packaged test PCB images. | Exercises the production pipeline and annotation path without webcam hardware or DirectML availability. GPU performance is out of scope for the gate. |
| D9 | Performance gates are relative, not absolute: dispatcher p99 ≤ 100 ms (matching the Phase 9 UI-responsiveness gate), no throughput decay > 25 % between the first and last steady-state minutes, steady-state working-set growth ≤ 50 MB between minute 3 and minute 10, engine stops within 2 s. Absolute FPS/latency are recorded, not gated. | Shared CI/developer machines vary widely; trend-based gates detect leaks and regressions without producing false failures. |
| D10 | Dashboard drag-and-drop logic is extracted into `Presentation/Models/DashboardCardDragData` with an internal `TryApplyCardDrop` seam on the view. | Keeps the mouse path and the tests on the same code; the view remains a thin adapter, consistent with MVVM purity. |
| D11 | The viewer's focused→default transition is covered via `InspectionViewerViewModel.SelectMarker(null)`, the call the empty-canvas click already makes. | Tests the existing production path; no new command or view behavior is introduced. |
| D12 | The export failure scenario uses an unwritable destination and asserts `Failed` progress, the failure toast title, and the absence of PDF/temp/staging artifacts. | Covers the roadmap's export-notification requirement on the branch that matters for trust: failure must be visible and clean. |
| D13 | Startup sequencing moves into `Infrastructure/Startup/StartupCoordinator` returning a result; `App.OnStartup` owns logging, the dialog, and `Shutdown(1)`. | Makes the failure branch unit-testable without WPF dialogs, and guarantees no window-less process survives a fatal migration error. |
| D14 | Startup routing failure remains non-fatal (warning + default view), matching Phase 4 behavior. | Routing is a preference lookup; failing to route must not prevent inspection work. |
| D15 | Multi-instance execution is not blocked in Phase 10; the behavior and its database risk are recorded in the launch checklist. | Process-wide enforcement is a product decision with UX consequences; it is surfaced, not silently invented. |
| D16 | Version `0.10.0` is recorded in project metadata; changelog uses the same version and date. No git tag is created until the phase merges with human approval. | A build must be identifiable before a tag exists; tags are release artifacts and require the merge decision. |
| D17 | Changelog granularity is capability-level, derived from the phase specifications. Known limitations (no installer, unsigned build, no icon) are listed. | A consumer-facing changelog is not a commit log; it must state what the build can and cannot do. |
| D18 | Launch scope is a portable framework-dependent `win-x64` publish, smoke-tested on the development machine. Distribution gaps are documented with an explicit Go/No-Go. | Matches `mission.md` §5.2: installer, signing, branding, and public release remain future enhancements. |
| D19 | No new NuGet package is added in Phase 10. The harness references the main project only. | Keeps the dependency surface frozen for launch and avoids `socket`/`ctx7` gates for non-essential tooling. |
| D20 | All new BDD scenarios reuse the established isolated-database, temp-root, and stub-service pattern. | Prevents tests from touching the developer's real database, Downloads folder, or settings. |

## 4. CI Contract

| Concern | Requirement |
|---|---|
| Triggers | `push` to `main`, `pull_request` to `main`, `workflow_dispatch` |
| Runner | `windows-latest`, job timeout ≤ 20 minutes |
| SDK | `actions/setup-dotnet` with `10.0.x`; no `global.json` |
| Cache | NuGet packages keyed by `hashFiles('**/*.csproj')` |
| Restore | `dotnet restore pcb-deftek.slnx` |
| Build | `dotnet build pcb-deftek.slnx -c Release --no-restore -warnaserror` — 0 warnings required |
| Unit tests | `dotnet test tests/pcb-deftek.Tests/pcb-deftek.Tests.csproj -c Release --no-build --no-restore --logger trx` |
| BDD tests | `dotnet test tests/pcb-deftek.Bdd/pcb-deftek.Bdd.csproj -c Release --no-build --no-restore --logger trx` |
| Artifacts | `.trx` files uploaded with `if: always()` |
| Permissions | `contents: read`; no secrets |
| Concurrency | One run per ref; superseded runs cancelled |
| Exclusions | None. No test trait, scenario, or project is skipped |

## 5. Performance Measurement Contract

### 5.1 Workload

- Fake `ICameraService` emits the packaged test images (`tests/pcb-deftek.Tests/TestData/Model/*.jpg`) in rotation at the requested capture rate (default 30 fps), with backpressure so frames cannot queue unboundedly.
- The production pipeline runs unmodified: `LiveInspectionEngine` → `OpenCvVisionPreprocessor` → `PcbDefectDetector` → `OnnxModelRunner` (real `best.onnx`, CPU provider) → `ImageAnnotationService`.
- A dedicated STA thread hosts a WPF `Dispatcher` running a 50 ms `DispatcherTimer`; callback lateness is recorded while inference runs.
- Default run: 10 minutes; `--duration 1` is the smoke setting.

### 5.2 Reported Metrics

| Group | Metrics |
|---|---|
| Throughput | Emitted frames/s, processed frames, coalesced/dropped frames, per-minute FPS |
| Latency | Inference latency p50/p95/p99/max from `LiveInspectionTelemetry` |
| Memory | Managed heap, working set, private bytes, Gen0/1/2 collection counts (1 s sampling) |
| Responsiveness | Dispatcher callback lateness p50/p95/p99/max |
| Stability | Unhandled exceptions, engine stop time, surviving threads |
| Context | OS build, CPU, RAM, GPU presence, .NET runtime version, commit SHA |

### 5.3 Gates

| Gate | Threshold | Rationale |
|---|---|---|
| Dispatcher p99 lateness | ≤ 100 ms | Same bar as Phase 9's UI-responsiveness gate |
| Dispatcher max lateness | ≤ 500 ms | Tolerates one GC/OS outlier without hiding a freeze |
| Throughput decay | ≤ 25 % between first and last steady-state minute | Detects leaks/slowdown during continuous inference |
| Steady-state working-set growth | ≤ 50 MB between minute 3 and minute 10 | Plateau expected; growth indicates a leak |
| Post-run managed heap | Within 25 % of post-warm-up baseline after a forced collection | Detects retained managed graphs |
| Engine shutdown | `StopAsync` completes ≤ 2 s, no surviving capture/inference threads | Prevents zombie processing after navigation |
| Harness hygiene | Writes only under `--out`; no repository or `%LOCALAPPDATA%` writes | Keeps the tool safe to run anywhere |

Warm-up (first 60 seconds) is excluded from leak verdicts but included in stability checks.

## 6. BDD Coverage Contract

| Roadmap-named workflow | Existing coverage | Phase 10 action |
|---|---|---|
| Batch ETA | `FileBatchDetection.feature` — inline ETA computed, progress reaches 100 %, records persisted | Verify only; no change |
| Camera auto-hide | `LiveCameraInspection.feature` — controls hide after 3 s stationary, status and telemetry remain, mouse movement restores | Verify only; no change |
| Viewer sidebar state transitions | Default and focused states covered | Add focused → default transition |
| Dashboard drag-and-drop | Card reorder + persistence covered through the view-model command | Add drop-path payload/resolution unit tests and a BDD scenario through `TryApplyCardDrop` |
| Export notifications | Success and no-match covered | Add failure notification (unwritable destination) |

New scenarios must assert observable behavior (order persisted, sidebar state, toast title, file absence), never mock interactions.

## 7. Launch Readiness Contract

The deliverable is `launch-checklist.md`, produced with the `shipping-and-launch` skill, covering:

1. **Build & reproducibility** — clean-clone restore/build/test; 0 warnings; Release publish command; artifact contents and size.
2. **Runtime prerequisites** — Windows 10/11 x64, .NET 10 desktop runtime for the framework-dependent build, optional webcam, optional DirectML GPU.
3. **Functional smoke matrix** — every essential feature in `mission.md` §5.1: dashboard, filters, PDF export, live detection (with and without a camera), batch detection, history, viewer, settings, database backup/export, notifications.
4. **Data safety** — user-owned source images never modified or deleted; app-owned captures only; database and export locations; migration from a prior schema; backup restore guidance.
5. **Error handling & diagnostics** — log location and retention, startup failure behavior, crash dialog, no silent catches on critical paths.
6. **Security & dependencies** — `dotnet list package --vulnerable` result, secrets/absolute-path scan, no network calls or telemetry.
7. **Known gaps** — no installer/MSIX, unsigned binaries, no application icon, no auto-update, multi-instance behavior, GitHub Release publication deferred.
8. **Verdict** — per-category pass/fail with evidence and one overall Go/No-Go with stated conditions.

A "Go" requires every critical item to pass: clean-clone build, full suite green, portable build starts and completes the smoke matrix, export produces a valid PDF, deletion policy verified, logs present, no vulnerable dependency, and no known data-loss defect.

## 8. Technical Constraints

- Windows-only WPF (`net10.0-windows`), nullable enabled, 0 warnings in Release CI.
- No new NuGet packages; no database migration; no new settings keys.
- CI must not require a webcam, an interactive desktop session, or a DirectML GPU.
- Performance harness must not be part of `dotnet test`, must not write into the repository, and must dispose OpenCV `Mat` instances.
- Startup coordinator must not reference WPF types; `App` remains the only owner of dialogs and shutdown.
- BDD and unit tests isolate SQLite files, capture directories, report directories, and settings under per-test temp roots.
- No production behavior change other than the startup failure path (D13/D14).
- Release metadata is the only project-file change.

## 9. Edge Cases

| Case | Expected behavior |
|---|---|
| CI runner has no webcam | BDD uses `FakeCameraService`; no test opens a device |
| CI runner has no GPU | CPU execution provider; DirectML is optional and never asserted in CI |
| Model file missing from CI output | Build copies `Assets/Models/best.onnx` (`PreserveNewest`); a missing model fails inference tests loudly |
| Reqnroll-generated code emits a warning | Fix at source or scope `NoWarn` to generated files only; the solution-wide gate stays on |
| Two CI runs on the same branch | Older run is cancelled by the concurrency group |
| Dashboard drop with foreign payload | No reorder, no persistence |
| Drop on the same card | No-op |
| Viewer focus cleared with no image | Default sidebar state remains consistent; delete availability unchanged |
| Export destination path occupied by a file | `Failed` state, failure toast, no artifacts |
| Migration fails at startup | Fatal log, one dialog, exit code 1, no window |
| Routing fails at startup | Warning log, default view, inspection usable |
| Perf run interrupted | Report not written; temp files only under `--out`; exit code non-zero |
| Machine under load during perf run | Trend gates may fail; rerun required before recording a waived gate |
| Portable publish on a machine without .NET 10 runtime | Framework-dependent build fails fast with a runtime message; documented prerequisite |
| Existing `app.db` from an earlier phase | Migration runner upgrades it; checklist verifies on a copy, never the live file |

## 10. Open Questions

None. Decisions requiring explicit human approval before implementation are listed in `plan.md` ("Approvals Required"): CI runner usage, version `0.10.0` and changelog granularity, launch scope limited to a portable publish, and the startup failure behavior change.
