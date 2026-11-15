# Implementation Plan: Phase 10 — Hardening, BDD Verification & Launch Readiness

## Overview

Phase 10 is the closure phase of the roadmap. It adds no product capability; it makes the capability shipped in Phases 1–9 verifiable, repeatable, and distributable. Four roadmap outcomes drive it:

1. End-to-end Reqnroll BDD verification of the critical workflows named in `roadmap.md`.
2. Performance verification: memory footprint and UI responsiveness under continuous camera inference.
3. CI/CD build and test automation.
4. A launch-readiness checklist and Go/No-Go verdict, produced with the `shipping-and-launch` skill.

Delivered work: a GitHub Actions pipeline; three missing/partial BDD workflows (dashboard card drag-and-drop through the view path, viewer focus→default transition, export failure notification); a permanent opt-in performance harness (live inference soak with real ONNX inference, memory sampling, dispatcher-stall probe); a startup-failure recovery path so a database migration failure aborts cleanly instead of leaving a window-less process; release metadata and a changelog; and the launch checklist.

Task list target: **this `plan.md`** — the project convention is `specs/<date>-<feature>/{plan,requirements,validation}.md`, so the skill's default `tasks/todo.md` is not used and no `tasks/` directory is created.

## Architecture & Dependency Graph

```
GitHub Actions CI (Group 1) ──────────────── gate for every later group
        │
BDD gap closure (Group 2)
  DashboardView drop path ──► Presentation/Models/DashboardCardDragData (pure)
  InspectionViewerViewModel.SelectMarker(null) ──► default sidebar state
  DashboardPdfExportService failure ──► Failed state + toast
        │
Performance harness (Group 3)
  FakeCameraService ──► LiveInspectionEngine ──► OnnxModelRunner (real model, CPU)
                    └─► IceDispatcher probe (STA thread, 50 ms timer)
                    └─► memory/GC sampler ──► markdown + json report
        │
Startup recovery (Group 4)
  App.OnStartup ──► StartupCoordinator ──► IDatabaseMigrator / IStartupRoutingService
        │
Release metadata + launch checklist (Group 5)
        │
Final verification, review pipeline, merge (Group 6)
```

Ordering rationale: CI lands first so every subsequent change is gated on a clean build and full suite; performance work needs the stable build from Groups 1–2; startup recovery is the only production behavior change and lands last before documentation so the checklist can validate it.

## Task Breakdown

### Task Group 1: CI/CD Build & Test Automation

#### Task 1.1: GitHub Actions CI Pipeline

- **Description:** Add a single workflow that restores, builds in Release with warnings treated as errors, and runs the unit and BDD test projects separately, publishing `.trx` results on every outcome.
- **Acceptance criteria:**
  - [ ] `.github/workflows/ci.yml` triggers on `push` and `pull_request` targeting `main`, plus `workflow_dispatch`.
  - [ ] `permissions: contents: read`; concurrency group cancels superseded runs on the same ref.
  - [ ] Runs on `windows-latest` with `actions/setup-dotnet` pinned to `10.0.x`; NuGet package cache keyed by `hashFiles('**/*.csproj')`.
  - [ ] Build command is `dotnet build pcb-deftek.slnx -c Release --no-restore -warnaserror`; any warning fails the job.
  - [ ] Unit tests run from `tests/pcb-deftek.Tests`, BDD tests from `tests/pcb-deftek.Bdd`, both with `-c Release --no-build --no-restore` and `--logger "trx;LogFileName=<name>.trx"`.
  - [ ] Test results upload with `if: always()`; a failing test fails the job.
  - [ ] No secrets are referenced; no NuGet package is added; wall-clock under 20 minutes.
- **Verification:**
  - [ ] Workflow run URL and duration recorded in `validation.md` §6.
  - [ ] The same three commands pass locally in `Release` with 0 warnings.
- **Dependencies:** None
- **Files:**
  - `.github/workflows/ci.yml`
- **Estimated scope:** S (1 file)

#### Task 1.2: Repository Build & Test Documentation

- **Description:** Replace the stub `README.md` with the information a new contributor or reviewer needs: prerequisites, build/run/test commands, test project layout, CI status, data/log locations, and links to `specs/`.
- **Acceptance criteria:**
  - [ ] Prerequisites state Windows x64, .NET 10 SDK, optional webcam, optional DirectML-capable GPU.
  - [ ] Commands match `tech-stack.md` §3 exactly (`dotnet build`, `dotnet run --project pcb-deftek.csproj`, `dotnet test`, `dotnet restore`) plus the solution-level build command used by CI.
  - [ ] Documents the isolated-test rule (tests never touch the real `%LOCALAPPDATA%` database or Downloads folder).
  - [ ] Lists runtime data locations: `%LOCALAPPDATA%\PcbDeftek\pcb-deftek\app.db`, `logs\`, `Captures\`; `Downloads\PCB Deftek\{Report,Backup,Images}`.
  - [ ] No secrets, absolute developer paths, or unverified claims.
- **Verification:**
  - [ ] Every documented command executed from the repository root; results recorded.
- **Dependencies:** Task 1.1
- **Files:**
  - `README.md`
- **Estimated scope:** XS (1 file)

---

### Checkpoint 1: CI Enforces the Quality Bar
- [ ] CI is green on the branch and blocks on warnings and test failures.
- [ ] Local reproduction of the CI commands succeeds.
- [ ] README commands are verified, not transcribed.
- [ ] Review the workflow with the human before merging.

---

### Task Group 2: BDD Coverage of Critical Workflows

#### Task 2.1: Dashboard Card Drag Payload & Drop Path

- **Description:** Move the drag payload format and payload resolution out of `DashboardView.xaml.cs` into a testable, presentation-model type; expose the drop application as an internal method the view's `OnCardDrop` delegates to, so BDD and unit tests exercise the same code the mouse path uses.
- **Acceptance criteria:**
  - [ ] A single format constant and payload factory/resolver live in `Presentation/Models/DashboardCardDragData.cs`; the view no longer owns the format string.
  - [ ] Resolving a data object that carries a non-card payload or no payload returns `null` without moving cards.
  - [ ] Dropping a card on itself is a no-op; dropping on another card moves it to that index and persists the order through the existing `MoveCardToAsync`.
  - [ ] `DashboardView.OnCardDrop` contains no reordering logic beyond delegating to the extracted type.
  - [ ] Existing dashboard composition and view-model tests remain green.
- **Verification:**
  - [ ] `dotnet test tests/pcb-deftek.Tests/ --filter FullyQualifiedName~DashboardCardDragDataTests`
  - [ ] `dotnet test tests/pcb-deftek.Tests/ --filter FullyQualifiedName~DashboardViewCompositionTests`
  - [ ] `dotnet build pcb-deftek.slnx` → 0 warnings
- **Dependencies:** Task 1.1
- **Files:**
  - `Presentation/Models/DashboardCardDragData.cs`
  - `Presentation/Views/DashboardView.xaml.cs`
  - `tests/pcb-deftek.Tests/DashboardCardDragDataTests.cs`
  - `tests/pcb-deftek.Tests/DashboardViewCompositionTests.cs`
- **Estimated scope:** M (4 files)

#### Task 2.2: BDD — Dashboard Drag-and-Drop Reorders and Persists

- **Description:** Add a scenario that applies a drop payload to a target card through the view path (`TryApplyCardDrop`) and proves the order persists across a dashboard reload.
- **Acceptance criteria:**
  - [ ] Scenario applies a card payload produced by the real factory to a different target card.
  - [ ] Resulting order matches the target index and is written to `UserSettings.DashboardCardOrder`.
  - [ ] Reloading the dashboard keeps the new order.
  - [ ] Dropping a foreign payload leaves the order unchanged.
  - [ ] Scenario runs against the isolated dashboard database; no real settings row is touched.
- **Verification:**
  - [ ] `dotnet test tests/pcb-deftek.Bdd/ --filter "FullyQualifiedName~DefectStatisticsDashboardFeature"`
- **Dependencies:** Task 2.1
- **Files:**
  - `tests/pcb-deftek.Bdd/Features/DefectStatisticsDashboard.feature`
  - `tests/pcb-deftek.Bdd/StepDefinitions/DashboardStepDefinitions.cs`
- **Estimated scope:** S (2 files)

#### Task 2.3: BDD — Viewer Focus Reset Restores the Default Sidebar

- **Description:** Cover the missing sidebar transition: focused → default when defect focus is cleared.
- **Acceptance criteria:**
  - [ ] After focusing a marker, `IsDefectFocused` is true and the focused fields are present.
  - [ ] Clearing focus (`SelectMarker(null)`, the view's empty-canvas click) returns the default sidebar: record name, timestamp, and class summaries.
  - [ ] The `Delete Image` action remains available in both states.
  - [ ] Existing default/focused scenarios remain unchanged and green.
- **Verification:**
  - [ ] `dotnet test tests/pcb-deftek.Bdd/ --filter "FullyQualifiedName~HistoryAndDefectViewerFeature"`
- **Dependencies:** Task 1.1
- **Files:**
  - `tests/pcb-deftek.Bdd/Features/HistoryAndDefectViewer.feature`
  - `tests/pcb-deftek.Bdd/StepDefinitions/HistoryAndViewerStepDefinitions.cs`
- **Estimated scope:** S (2 files)

#### Task 2.4: BDD — Failed Export Notifies and Writes Nothing

- **Description:** Cover the failure branch of the export notification contract using an unwritable destination, with the real export service and a recording notification service.
- **Acceptance criteria:**
  - [ ] Destination resolves to a path that cannot be created (a file occupies the directory path) or is rejected by the exporter's validation.
  - [ ] Data-export progress reaches `Failed`; the failure toast title is `Report export failed`.
  - [ ] No `.pdf`, `.tmp`, or staging directory remains.
  - [ ] No success toast is raised.
  - [ ] The failure is logged; the scenario asserts observable state only, not log text.
- **Verification:**
  - [ ] `dotnet test tests/pcb-deftek.Bdd/ --filter "FullyQualifiedName~AuditPdfExportFeature"`
- **Dependencies:** Task 1.1
- **Files:**
  - `tests/pcb-deftek.Bdd/Features/AuditPdfExport.feature`
  - `tests/pcb-deftek.Bdd/StepDefinitions/DashboardExportStepDefinitions.cs`
- **Estimated scope:** S (2 files)

---

### Checkpoint 2: Critical Workflows Proven End-to-End
- [ ] Every roadmap-named workflow has a passing BDD scenario (see `validation.md` §3 matrix).
- [ ] New scenarios fail when the corresponding production behavior is reverted (spot-check one).
- [ ] No regression against the 834-test baseline.
- [ ] Review new scenarios with the human before Group 3.

---

### Task Group 3: Performance Verification Harness

#### Task 3.1: Harness Project & CLI

- **Description:** Add an opt-in console harness that runs a repeatable live-inspection workload and writes a machine-readable and human-readable report. It is a tool, not a test: excluded from `dotnet test` and never a CI gate.
- **Acceptance criteria:**
  - [ ] `tests/pcb-deftek.Perf/pcb-deftek.Perf.csproj` targets `net10.0-windows`, `UseWPF=true`, `OutputType=Exe`, `IsTestProject=false`, `IsPackable=false`, references the main project only.
  - [ ] Project is registered in `pcb-deftek.slnx` under `tests/`.
  - [ ] Options: `--duration <minutes>`, `--fps <target>`, `--out <directory>` (default `%TEMP%\pcb-deftek-perf`).
  - [ ] Exit code 0 on a clean run, non-zero when a gate fails or the workload throws.
  - [ ] Writes `perf-<timestamp>.md` and `.json`; never writes inside the repository or `%LOCALAPPDATA%`.
  - [ ] `dotnet test pcb-deftek.slnx` ignores the project.
- **Verification:**
  - [ ] `dotnet run --project tests/pcb-deftek.Perf -c Release -- --duration 1 --fps 10` completes and writes both files.
- **Dependencies:** Task 1.1
- **Files:**
  - `tests/pcb-deftek.Perf/pcb-deftek.Perf.csproj`
  - `tests/pcb-deftek.Perf/Program.cs`
  - `pcb-deftek.slnx`
- **Estimated scope:** S (3 files)

#### Task 3.2: Live Inference Soak Workload

- **Description:** Drive the real live-inspection pipeline: a fake camera emits the packaged test PCB images at the target rate, while `LiveInspectionEngine` runs real ONNX inference, annotation, and telemetry on the CPU execution provider.
- **Acceptance criteria:**
  - [ ] Fake camera implements `ICameraService` and emits frames at the requested rate with backpressure (no unbounded frame queue).
  - [ ] Engine is started with the real `OnnxModelRunner`, `PcbDefectDetector`, `OpenCvVisionPreprocessor`, and `ImageAnnotationService`; model resolved from the build output.
  - [ ] Reports processed frames, dropped/coalesced frames, per-minute FPS, and latency p50/p95/p99/max from `LiveInspectionTelemetry`.
  - [ ] Handles `Mat` lifetimes without leaking unmanaged memory (frames disposed after each emission).
  - [ ] Engine `StopAsync` completes within 2 seconds; no capture or inference thread survives (assert live thread count).
- **Verification:**
  - [ ] 1-minute smoke run produces plausible telemetry (FPS > 0, latency > 0).
- **Dependencies:** Task 3.1
- **Files:**
  - `tests/pcb-deftek.Perf/Program.cs`
  - `tests/pcb-deftek.Perf/FakeCameraService.cs`
  - `tests/pcb-deftek.Perf/PerfReport.cs`
- **Estimated scope:** M (3 files)

#### Task 3.3: Memory and Dispatcher-Responsiveness Probes

- **Description:** Measure the two launch gates the roadmap names: memory footprint and UI responsiveness under continuous inference.
- **Acceptance criteria:**
  - [ ] Samples every second: managed heap, working set, private bytes, Gen0/1/2 collection counts.
  - [ ] First 60 seconds are warm-up and excluded from leak verdicts.
  - [ ] An STA dispatcher thread runs a 50 ms timer while the workload runs and records callback lateness (p50/p95/p99/max).
  - [ ] Report includes steady-state working-set delta, post-run heap after a forced collection versus baseline, and a pass/fail per gate from `validation.md` §5.
  - [ ] Repeated runs are reproducible within ±20 % on the same machine.
- **Verification:**
  - [ ] Two consecutive 1-minute runs produce comparable numbers and identical gate verdicts.
- **Dependencies:** Task 3.2
- **Files:**
  - `tests/pcb-deftek.Perf/Program.cs`
  - `tests/pcb-deftek.Perf/DispatcherProbe.cs`
  - `tests/pcb-deftek.Perf/MemorySampler.cs`
- **Estimated scope:** M (3 files)

#### Task 3.4: Baseline Measurement & Recorded Results

- **Description:** Run the 10-minute soak on the development machine and record the numbers and verdicts.
- **Acceptance criteria:**
  - [ ] 10-minute run with target 30 fps capture completes without exceptions.
  - [ ] Latency, FPS stability, memory, and dispatcher metrics recorded in `validation.md` §9 with machine context (CPU, RAM, GPU, OS build).
  - [ ] Each gate in §5 marked met or waived with justification.
  - [ ] Command and elapsed time documented in `README.md` and `validation.md` §7.
- **Verification:**
  - [ ] Report files kept outside the repository; the recorded numbers match the files.
- **Dependencies:** Task 3.3
- **Files:**
  - `specs/2026-11-15-hardening-bdd-launch-readiness/validation.md`
  - `README.md`
- **Estimated scope:** XS (2 files)

---

### Checkpoint 3: Performance Gates Measured
- [ ] Harness is repeatable and excluded from `dotnet test`.
- [ ] Memory and dispatcher gates measured, not assumed.
- [ ] No throughput decay trend across the soak.
- [ ] Review metrics with the human before Group 4.

---

### Task Group 4: Startup Failure Recovery

#### Task 4.1: Startup Coordinator

- **Description:** Extract startup sequencing from `App.OnStartup` into a testable coordinator: run migrations, then evaluate startup routing, and return a result instead of throwing from an `async void` handler.
- **Acceptance criteria:**
  - [ ] `StartupCoordinator.RunAsync(IServiceProvider, CancellationToken)` returns a result distinguishing success from migration failure, carrying the exception.
  - [ ] A migration failure returns failure; startup routing is not attempted.
  - [ ] A routing failure is logged as a warning and still yields success (unchanged Phase 4 behavior).
  - [ ] `App.OnStartup` logs the failure at `Fatal`, shows one user-safe dialog when `ShowErrorDialogs` is true, calls `Shutdown(1)`, and never constructs `MainWindow`.
  - [ ] Success path behavior is unchanged: migrations, routing, then window shown.
  - [ ] No new NuGet package; no new public surface outside `Infrastructure/Startup`.
- **Verification:**
  - [ ] `dotnet test tests/pcb-deftek.Tests/ --filter FullyQualifiedName~StartupCoordinatorTests`
  - [ ] `dotnet test tests/pcb-deftek.Tests/ --filter FullyQualifiedName~DependencyInjectionTests`
  - [ ] Manual: point the connection factory at an unwritable database path and launch; the app logs, shows one dialog, exits with code 1, and leaves no window.
- **Dependencies:** Task 1.1
- **Files:**
  - `Infrastructure/Startup/StartupCoordinator.cs`
  - `App.xaml.cs`
  - `tests/pcb-deftek.Tests/StartupCoordinatorTests.cs`
- **Estimated scope:** M (3 files)

#### Task 4.2: Startup Failure Tests

- **Description:** Cover both branches with fakes: failing migrator, failing router, and the success path, asserting the coordinator does no UI work itself.
- **Acceptance criteria:**
  - [ ] Failing migrator → failed result with the original exception; router never invoked.
  - [ ] Failing router → success result; migrator invoked once.
  - [ ] Success → both invoked in order and success returned.
  - [ ] Tests run without touching the real `%LOCALAPPDATA%` database.
- **Verification:**
  - [ ] `dotnet test tests/pcb-deftek.Tests/ --filter FullyQualifiedName~StartupCoordinatorTests`
- **Dependencies:** Task 4.1
- **Files:**
  - `tests/pcb-deftek.Tests/StartupCoordinatorTests.cs`
- **Estimated scope:** S (1 file)

---

### Checkpoint 4: Startup Fails Cleanly
- [ ] Migration failure aborts with exit code 1 and one dialog.
- [ ] Success and routing-failure paths unchanged.
- [ ] Full suite still green.
- [ ] Review the extraction with the human before Group 5.

---

### Task Group 5: Release Metadata & Launch Readiness

#### Task 5.1: Version Metadata

- **Description:** Give the produced executable an identifiable version so a build can be traced to a commit and a release.
- **Acceptance criteria:**
  - [ ] `pcb-deftek.csproj` sets `Version`, `AssemblyVersion`, `FileVersion`, and `InformationalVersion` to `0.10.0`; no other project property changes.
  - [ ] Built executable reports the version through `FileVersionInfo`.
  - [ ] Build remains at 0 warnings.
- **Verification:**
  - [ ] `dotnet build pcb-deftek.slnx -c Release` then inspect `FileVersionInfo` of the output executable.
- **Dependencies:** Task 1.1
- **Files:**
  - `pcb-deftek.csproj`
- **Estimated scope:** XS (1 file)

#### Task 5.2: Changelog

- **Description:** Add a curated, capability-level changelog for the first identified build. It is derived from the phase specifications, not from commit messages.
- **Acceptance criteria:**
  - [ ] `CHANGELOG.md` follows Keep a Changelog structure with `## [0.10.0] - 2026-11-15`.
  - [ ] Sections cover every essential feature in `mission.md` §5.1 plus the Phase 9/10 hardening, grouped as Added / Changed / Fixed.
  - [ ] Entries describe user-visible behavior, not internal mechanics.
  - [ ] Nothing is listed that is not implemented and tested on `main`.
  - [ ] Known limitations (no installer, no code signing, no application icon) are stated under a `Known Limitations` heading.
- **Verification:**
  - [ ] Cross-check each entry against `specs/*/requirements.md` and the test suite.
- **Dependencies:** None
- **Files:**
  - `CHANGELOG.md`
- **Estimated scope:** S (1 file)

#### Task 5.3: Launch-Readiness Checklist & Go/No-Go

- **Description:** Produce the launch artifact with the `shipping-and-launch` skill: categories, evidence, residual risks, and an explicit Go/No-Go verdict for a portable Windows build.
- **Acceptance criteria:**
  - [ ] `launch-checklist.md` covers: clean-clone build; full test suite; Release publish; portable artifact contents and size; runtime prerequisites; functional smoke matrix for every essential feature; data-safety checks (user files untouched, backup/export paths); error/log behavior; dependency vulnerability scan (`dotnet list package --vulnerable`); secrets/absolute-path scan; known distribution gaps.
  - [ ] A portable publish (`dotnet publish -c Release -r win-x64 --self-contained false`) is executed and the output is launched on the development machine; the smoke matrix is executed against it.
  - [ ] Every checklist item records pass/fail with evidence (command output, screenshot reference, or measurement).
  - [ ] A single overall Go/No-Go recommendation is stated with conditions.
  - [ ] Scope is explicit: no installer/MSIX, no code signing, no application icon, and no GitHub Release publication in this phase (`mission.md` §5.2).
  - [ ] Multi-instance behavior and database-migration failure are recorded as documented risks with the observed behavior.
- **Verification:**
  - [ ] Checklist re-walked after a fresh `git clean -xdf` build to prove reproducibility.
  - [ ] Publish output runs and completes the smoke matrix.
- **Dependencies:** Tasks 1.1, 2.4, 3.4, 4.2, 5.1, 5.2
- **Files:**
  - `specs/2026-11-15-hardening-bdd-launch-readiness/launch-checklist.md`
- **Estimated scope:** M (1 file)

---

### Checkpoint 5: Launch Package Assessed
- [ ] Version and changelog exist and match the shipped behavior.
- [ ] Portable build verified end-to-end.
- [ ] Go/No-Go stated with evidence, not opinion.
- [ ] Human reviews the checklist before Group 6.

---

### Task Group 6: Final Verification & Merge Readiness

#### Task 6.1: Full Gate Run

- **Description:** Execute every gate in `validation.md` and record results.
- **Acceptance criteria:**
  - [ ] `dotnet build pcb-deftek.slnx -c Release -warnaserror` → 0 errors, 0 warnings.
  - [ ] `dotnet test pcb-deftek.slnx` → 100 % pass, 0 skipped, no regression against the 834-test baseline.
  - [ ] CI run on the final commit is green.
  - [ ] Performance gates met or waived with recorded numbers.
  - [ ] Launch checklist verdict recorded.
- **Verification:**
  - [ ] Results table in `validation.md` §9 updated with commands and counts.
- **Dependencies:** Task Groups 1–5
- **Files:**
  - `specs/2026-11-15-hardening-bdd-launch-readiness/validation.md`
- **Estimated scope:** XS (1 file)

#### Task 6.2: Review Pipeline

- **Description:** Execute the gated review pipeline from `AGENTS.md` §7, asking the user before each subsequent stage.
- **Acceptance criteria:**
  - [ ] `code-review-and-quality` completed with findings recorded.
  - [ ] `code-simplification` executed only after explicit approval; outcome recorded.
  - [ ] `performance-optimization` executed only after explicit approval; outcome recorded.
  - [ ] `security-and-hardening` executed only after explicit approval; outcome recorded.
  - [ ] Blocking findings fixed; non-blocking findings recorded as follow-ups.
- **Verification:**
  - [ ] `validation.md` §8 lists each stage and its verdict.
- **Dependencies:** Task 6.1
- **Files:**
  - `specs/2026-11-15-hardening-bdd-launch-readiness/validation.md`
- **Estimated scope:** XS (1 file)

#### Task 6.3: PR, Human Approval, Merge

- **Description:** Complete the `AGENTS.md` §4 lifecycle for the Phase 10 branch.
- **Acceptance criteria:**
  - [ ] PR links to the Phase 10 issue and lists the gate results.
  - [ ] Human approval recorded; PR not self-merged.
  - [ ] Merge to `main`, issue closed, branch deleted.
  - [ ] Plan checkboxes updated to reflect merged state.
- **Verification:**
  - [ ] `git log --oneline -5` on `main` shows the merge; branch is gone.
- **Dependencies:** Task 6.2
- **Files:** None
- **Estimated scope:** XS (0 files)

---

### Checkpoint 6: Merge Readiness
- [ ] All seven gates in `validation.md` §7 satisfied.
- [ ] Human approval recorded.
- [ ] Branch deleted after merge.

---

## Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Hosted CI runner has no interactive desktop or webcam, so BDD tests fail there | High | All existing BDD scenarios inject fakes (`FakeCameraService`, stub detector); CI runs the same projects that pass locally. First CI run is the proof; diagnose before merging. |
| `-warnaserror` trips on Reqnroll-generated `.feature.cs` code | Medium | Fix at the source or scope `NoWarn` to the generated files only; never disable the solution-wide gate. |
| ONNX model load time (50 MB, tracked in git) inflates CI duration | Medium | Model is copied from the project output; timeout set to 20 minutes and measured in the first run. No new dependency or model trim. |
| Performance harness measures a noisy shared machine | Medium | Harness is opt-in and local-only; gates are trend-based (steady-state deltas) rather than absolute-throughput claims; record machine context. |
| Extracting the drop path changes dashboard behavior | Medium | Behavior-preserving refactor with unit tests before the BDD scenario; `DashboardViewCompositionTests` re-run. |
| Startup extraction alters the success path | High | Success-path coordinator test plus manual cold start after migration; DI tests re-run. |
| BDD scenarios share state or leak temp roots | Medium | Each scenario builds its own container and temp root (existing pattern); new scenarios follow it and clean up in dispose. |
| Versioning/changelog drifts from shipped behavior | Medium | Changelog entries cross-checked against phase specs; version verified from the binary, not only the csproj. |
| Launch checklist becomes a rubber stamp | High | Every item requires evidence; clean-clone rebuild and portable-output smoke run; Go/No-Go with explicit conditions. |
| Scope creep into Phase 5.2 future enhancements (installer, icon, branding, release publication) | Medium | Explicitly out of scope in requirements and checklist; changes require a new phase. |

## Approvals Required (before implementation)

1. CI on GitHub-hosted runners for `ricky-kiva/pcb-deftek` (consumes repository Actions minutes).
2. Version identifier `0.10.0` and the changelog granularity (capability-level, covering Phases 1–10).
3. Launch scope: portable `win-x64` publish only; installer/MSIX, code signing, application icon, and GitHub Release publication remain in `mission.md` §5.2.
4. Startup behavior change: migration failure now exits the process with code 1 instead of leaving a window-less application.

## Open Questions

None. The four items above are approval gates with a stated default, not unresolved design questions.
