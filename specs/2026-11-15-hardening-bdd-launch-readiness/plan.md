# Implementation Plan: Phase 10 — Hardening, BDD Verification & Launch Readiness

## Overview

Phase 10 closes the roadmap with the smallest set of work that makes the shipped product **verifiable and distributable**. Ten tasks in five groups: CI with the application icon, README with brand assets, two test-only BDD scenarios, one startup-safety fix, release metadata, the launch checklist, and the merge gates.

Deliberately deferred to keep launch fast (recorded in `requirements.md` §2.2 and as known gaps in the launch checklist): performance verification under continuous camera inference (roadmap Phase 10 bullet 2), the performance harness, the dashboard drag-path refactor and its BDD scenario, and a separate changelog file. The drag path is covered by a manual smoke step; release notes live inside the launch checklist.

Brand assets already staged in the working tree: `Assets/app-icon.ico` (362 KB, six-size ICO) and `docs/assets/` (logo + one screenshot per page). Both are untracked today and are committed by Tasks 1.1 and 1.2.

Task list target: **this `plan.md`** — project convention is `specs/<date>-<feature>/{plan,requirements,validation}.md`, so no `tasks/` directory is created.

## Architecture & Dependency Graph

```
1. CI, application icon, README brand assets ── gate for everything below
        │
2. BDD: viewer focus reset, export failure   (test-only, no production change)
3. Startup failure recovery ── StartupCoordinator ── App.OnStartup
        │
4. Version metadata + launch checklist (portable publish, Go/No-Go)
        │
5. Full gates → review pipeline → PR → human approval → merge
```

Ordering rationale: CI first so later changes are gated; the BDD additions are independent of the startup fix, but both land before the checklist so the checklist evaluates the final behavior; the review pipeline and PR close the `AGENTS.md` lifecycle.

## Task Breakdown

### Task Group 1: CI/CD Build & Test Automation

#### Task 1.1: GitHub Actions CI Pipeline & Application Icon

- **Description:** Add one workflow that restores, builds in Release with warnings as errors, and runs the unit and BDD projects separately, publishing `.trx` results on every outcome. In the same task, wire the staged `Assets/app-icon.ico` as the executable icon and the WPF window icon.
- **Acceptance criteria:**
  - [x] `.github/workflows/ci.yml` triggers on `push`/`pull_request` to `main` plus `workflow_dispatch`; `permissions: contents: read`; concurrency cancels superseded runs.
  - [x] Runs on `windows-latest`, `actions/setup-dotnet` pinned to `10.0.x`, NuGet cache keyed by `hashFiles('**/*.csproj')`.
  - [x] `dotnet build pcb-deftek.slnx -c Release --no-restore -warnaserror` — any warning fails the job.
  - [x] Unit and BDD tests run separately with `-c Release --no-build --no-restore --logger trx`; results upload with `if: always()`.
  - [x] No secrets, no new NuGet package, job under 20 minutes (1m53s).
  - [x] `pcb-deftek.csproj` sets `<ApplicationIcon>Assets\app-icon.ico</ApplicationIcon>` and includes the file as `<Resource>`; no other build item changes.
  - [x] `MainWindow.xaml` sets `Icon` to the packaged `Assets/app-icon.ico` resource; title bar and taskbar show the icon at runtime (title bar captured; the taskbar consumes the same `Window.Icon`).
  - [x] `Assets/app-icon.ico` is tracked in git (`git ls-files Assets/app-icon.ico`).
  - [x] Release build remains at 0 warnings and the solution builds unchanged.
- **Verification:**
  - [x] Workflow run URL and duration recorded in `validation.md` §6.
  - [x] The same commands pass locally in Release with 0 warnings.
  - [x] Built executable shows the icon in Explorer (`[System.Drawing.Icon]::ExtractAssociatedIcon` or manual check); running app shows it in the title bar and taskbar.
- **Dependencies:** None
- **Files:**
  - `.github/workflows/ci.yml`
  - `pcb-deftek.csproj`
  - `MainWindow.xaml`
  - `Assets/app-icon.ico` (tracked)
  - `tests/pcb-deftek.Tests/DependencyInjectionTests.cs`
  - `tests/pcb-deftek.Bdd/AssemblyInfo.cs`
  - `tests/pcb-deftek.Bdd/StepDefinitions/DashboardExportStepDefinitions.cs`
- **Deviations:**
  - The window icon uses an assembly-qualified pack URI (`pack://application:,,,/pcb-deftek;component/Assets/app-icon.ico`). The relative form resolved against the entry assembly and broke `MainWindow` construction under the test host.
  - `tests/pcb-deftek.Tests/DependencyInjectionTests.cs` stopped constructing a real `App`; doing so installed a non-pumping `Application.Current`, which deadlocked `Dispatcher.Invoke` in later tests and hung the CI unit step.
  - `tests/pcb-deftek.Bdd` runs scenarios serially (`CollectionBehavior(DisableTestParallelization = true)`). Parallel feature collections starved the thread pool on CI, so the batch-cancel and export-cancel scenarios observed `Completed` instead of `Cancelled`. The export-cancel test's annotation block window was raised to 60 s and its wait reports annotation/progress state.
  - `LiveInspectionEngineTests` and `CameraServiceTests` frame-event waits were raised from 1.5–2 s to 15 s; a cold CI runner exceeded the original bound before the first frame was processed. Assertions are unchanged.
- **Estimated scope:** M (7 files, three of them test-only CI stabilizations)

#### Task 1.2: README with Brand Assets

- **Description:** Replace the 12-byte stub with a launch-ready README: logo, what the product does and what it is not for, page screenshots, prerequisites, commands, test isolation rule, and data/log locations.
- **Acceptance criteria:**
  - [x] Logo `docs/assets/readme-logo-1-1-c.png` is displayed at the top via a relative path that renders on GitHub.
  - [x] Description states, in these terms: "This application is specifically designed to detect PCB trace defects (e.g., open circuits and short circuits). It is best suited for bare PCBs without mounted components."
  - [x] A Screenshots section embeds all four staged captures from `docs/assets/`, in page order with captions: `readme-ss-page-dashboard-1-1-c.jpg`, `readme-ss-page-live-detection-1-1-c.jpg`, `readme-ss-page-file-detection-1-1-c.jpg`, `readme-ss-page-history-1-1-c.jpg`. No screenshot is invented for a page that has none.
  - [x] Documents the screenshot naming convention `readme-ss-page-{page_name}-{int_image_number}-{int_revision}-c.jpg` (current values: image number `1`, revision `1`) so later captures slot in without renaming.
  - [x] Prerequisites: Windows x64, .NET 10 SDK, optional webcam, optional DirectML GPU.
  - [x] Commands: `dotnet restore`, `dotnet build pcb-deftek.slnx -c Release`, `dotnet run --project pcb-deftek.csproj`, `dotnet test pcb-deftek.slnx`.
  - [x] States that tests use isolated temp databases/roots and never touch real user data.
  - [x] Lists data/log locations: `%LOCALAPPDATA%\PcbDeftek\pcb-deftek\{app.db,logs,Captures}`, `Downloads\PCB Deftek\{Report,Backup,Images}`.
  - [x] `docs/assets/` (logo + four screenshots) is tracked in git; every relative link resolves (`git ls-files docs/assets` matches the referenced files).
  - [x] No secrets or developer-specific paths.
- **Verification:**
  - [x] Every documented command executed from the repository root.
  - [x] README rendered locally or on the branch and checked for broken image links (11/11 local refs resolve).
- **Dependencies:** Task 1.1
- **Files:**
  - `README.md`
  - `docs/assets/readme-logo-1-1-c.png` (tracked)
  - `docs/assets/readme-ss-page-dashboard-1-1-c.jpg` (tracked)
  - `docs/assets/readme-ss-page-live-detection-1-1-c.jpg` (tracked)
  - `docs/assets/readme-ss-page-file-detection-1-1-c.jpg` (tracked)
  - `docs/assets/readme-ss-page-history-1-1-c.jpg` (tracked)
- **Estimated scope:** S (1 markdown file + 5 tracked assets)

---

### Checkpoint 1: CI Enforces the Quality Bar
- [x] CI green on the branch; blocks on warnings and test failures.
- [x] README commands verified; logo, four screenshots, and scope statement render correctly.
- [x] Application icon visible in Explorer, title bar, and taskbar; icon and README assets tracked.
- [ ] Human review before Group 2.

---

### Task Group 2: BDD Coverage of Failure/Transition Branches

#### Task 2.1: BDD — Viewer Focus Reset Restores the Default Sidebar

- **Description:** Cover the only missing sidebar transition: focused → default. Test-only; no production change.
- **Acceptance criteria:**
  - [ ] Focusing a marker sets `IsDefectFocused` and shows focused diagnostics.
  - [ ] `SelectMarker(null)` (the existing empty-canvas click path) restores the default sidebar: record name, timestamp, class summaries.
  - [ ] `Delete Image` remains available in both states.
  - [ ] Scenario uses the isolated history database; existing scenarios unchanged.
- **Verification:**
  - [ ] `dotnet test tests/pcb-deftek.Bdd/ --filter "FullyQualifiedName~HistoryAndDefectViewerFeature"`
- **Dependencies:** Task 1.1
- **Files:**
  - `tests/pcb-deftek.Bdd/Features/HistoryAndDefectViewer.feature`
  - `tests/pcb-deftek.Bdd/StepDefinitions/HistoryAndViewerStepDefinitions.cs`
- **Estimated scope:** S (2 files)

#### Task 2.2: BDD — Failed Export Notifies and Writes Nothing

- **Description:** Cover the export failure branch with an unwritable destination. Test-only; no production change.
- **Acceptance criteria:**
  - [ ] Destination cannot be created (a file occupies the directory path).
  - [ ] Data-export progress reaches `Failed`; failure toast title is `Report export failed`.
  - [ ] No `.pdf`, `.tmp`, or staging directory remains; no success toast.
  - [ ] Scenario uses an isolated temp root and recording notification service.
- **Verification:**
  - [ ] `dotnet test tests/pcb-deftek.Bdd/ --filter "FullyQualifiedName~AuditPdfExportFeature"`
- **Dependencies:** Task 1.1
- **Files:**
  - `tests/pcb-deftek.Bdd/Features/AuditPdfExport.feature`
  - `tests/pcb-deftek.Bdd/StepDefinitions/DashboardExportStepDefinitions.cs`
- **Estimated scope:** S (2 files)

---

### Task Group 3: Startup Failure Recovery

#### Task 3.1: Startup Coordinator & Failure Path

- **Description:** Stop rethrowing migration failures from `async void App.OnStartup`. Extract sequencing into a testable coordinator; abort cleanly with a fatal log, one dialog, and exit code 1.
- **Acceptance criteria:**
  - [ ] `Infrastructure/Startup/StartupCoordinator.RunAsync(IServiceProvider, CancellationToken)` returns success/failure and carries the exception.
  - [ ] Migration failure returns failure; startup routing is not attempted; no `MainWindow` is constructed.
  - [ ] Routing failure remains non-fatal (warning + default view), matching current behavior.
  - [ ] `App.OnStartup` logs `Fatal`, shows one user-safe dialog when `ShowErrorDialogs` is true, then calls `Shutdown(1)`.
  - [ ] Success path unchanged: migrations → routing → window.
  - [ ] Tests cover failing migrator (router never invoked), failing router (success), and the success path; no real `%LOCALAPPDATA%` database is touched.
- **Verification:**
  - [ ] `dotnet test tests/pcb-deftek.Tests/ --filter FullyQualifiedName~StartupCoordinatorTests`
  - [ ] `dotnet test tests/pcb-deftek.Tests/ --filter FullyQualifiedName~DependencyInjectionTests`
  - [ ] Manual: replace `app.db` with a corrupt copy (back it up first) and launch; expect fatal log, one dialog, exit without a window; restore the backup.
- **Dependencies:** Task 1.1
- **Files:**
  - `Infrastructure/Startup/StartupCoordinator.cs`
  - `App.xaml.cs`
  - `tests/pcb-deftek.Tests/StartupCoordinatorTests.cs`
- **Estimated scope:** M (3 files)

---

### Checkpoint 2: Critical Branches Are Proven
- [ ] Both new BDD scenarios pass; existing 44 scenarios unchanged.
- [ ] Startup failure path verified manually and by tests.
- [ ] Full suite green with no regression against the 834-test baseline.
- [ ] Human review before Group 4.

---

### Task Group 4: Release Identity & Launch Readiness

#### Task 4.1: Version Metadata

- **Description:** Make the produced executable traceable to this build.
- **Acceptance criteria:**
  - [ ] `pcb-deftek.csproj` sets `Version`, `AssemblyVersion`, `FileVersion`, `InformationalVersion` to `0.10.0`; no other property changes.
  - [ ] Built executable reports the version through `FileVersionInfo`.
  - [ ] Build remains at 0 warnings.
- **Verification:**
  - [ ] `dotnet build pcb-deftek.slnx -c Release` then inspect `FileVersionInfo` of the output.
- **Dependencies:** Task 1.1
- **Files:**
  - `pcb-deftek.csproj`
- **Estimated scope:** XS (1 file)

#### Task 4.2: Launch-Readiness Checklist & Go/No-Go

- **Description:** Produce the launch artifact with the `shipping-and-launch` skill: evidence per item, release notes, known gaps, and one Go/No-Go verdict for a portable build.
- **Acceptance criteria:**
  - [ ] Covers: clean-clone build; full suite; Release publish; artifact contents/size; runtime prerequisites; functional smoke matrix for every essential feature in `mission.md` §5.1; data-safety checks; log/error behavior; `dotnet list package --vulnerable`; secrets/absolute-path scan; known gaps.
  - [ ] Functional smoke matrix includes the manual steps that replace deferred automation: drag a metric card and confirm persistence after reload; drop a card on itself (no change); open Live Detection and confirm streaming, overlays, telemetry, auto-hide, and capture.
  - [ ] Checklist verifies the application icon: executable icon in Explorer, title-bar and taskbar icon at runtime, matching `Assets/app-icon.ico`.
  - [ ] Checklist verifies README rendering: logo and all four page screenshots resolve from `docs/assets/`, and the trace-defect/bare-PCB scope statement is present.
  - [ ] Release notes for `0.10.0` are included in the checklist (Added / Changed / Fixed / Known Limitations), derived from phase specs — no separate changelog file.
  - [ ] `dotnet publish -c Release -r win-x64 --self-contained false` executed; output launched; smoke matrix run against it.
  - [ ] Every item records pass/fail with evidence; one overall Go/No-Go with conditions.
  - [ ] Scope stated: no installer/MSIX, no signing, no splash branding, no GitHub Release publication (`mission.md` §5.2); the application icon and README brand assets are in scope.
  - [ ] Known gaps recorded: performance under continuous inference unverified, no automated drag-path test, multi-instance behavior allowed, splash branding deferred.
- **Verification:**
  - [ ] Checklist re-walked after `git clean -xdf`; same verdict.
- **Dependencies:** Tasks 2.1, 2.2, 3.1, 4.1
- **Files:**
  - `specs/2026-11-15-hardening-bdd-launch-readiness/launch-checklist.md`
- **Estimated scope:** M (1 file + manual runs)

---

### Checkpoint 3: Launch Package Assessed
- [ ] Version, release notes, and checklist exist and agree.
- [ ] Portable build runs and completes the smoke matrix.
- [ ] Go/No-Go stated with evidence, including the unverified-performance gap.
- [ ] Human review before Group 5.

---

### Task Group 5: Final Verification & Merge Readiness

#### Task 5.1: Full Gate Run

- **Description:** Execute and record every gate in `validation.md`.
- **Acceptance criteria:**
  - [ ] `dotnet build pcb-deftek.slnx -c Release -warnaserror` → 0 errors, 0 warnings.
  - [ ] `dotnet test pcb-deftek.slnx` → 100 % pass, 0 skipped, no regression against the 834-test baseline.
  - [ ] CI green on the final commit; launch verdict recorded.
- **Verification:**
  - [ ] `validation.md` §8 updated with commands and counts.
- **Dependencies:** Task Groups 1–4
- **Files:**
  - `specs/2026-11-15-hardening-bdd-launch-readiness/validation.md`
- **Estimated scope:** XS (1 file)

#### Task 5.2: Review Pipeline

- **Description:** Execute the gated `AGENTS.md` §7 pipeline, asking before each stage.
- **Acceptance criteria:**
  - [ ] `code-review-and-quality` completed; findings recorded.
  - [ ] `code-simplification`, `performance-optimization`, `security-and-hardening` executed only after explicit approval; outcomes recorded.
  - [ ] Blocking findings fixed; non-blocking recorded as follow-ups.
- **Verification:**
  - [ ] `validation.md` §9 lists each stage and verdict.
- **Dependencies:** Task 5.1
- **Files:**
  - `specs/2026-11-15-hardening-bdd-launch-readiness/validation.md`
- **Estimated scope:** XS (1 file)

#### Task 5.3: PR, Human Approval, Merge

- **Description:** Complete the `AGENTS.md` §4 lifecycle.
- **Acceptance criteria:**
  - [ ] PR links to the Phase 10 issue and lists gate results.
  - [ ] Human approval recorded; PR not self-merged.
  - [ ] Merge to `main`, issue closed, branch deleted; plan checkboxes updated.
- **Verification:**
  - [ ] `git log --oneline -5` on `main` shows the merge; branch gone.
- **Dependencies:** Task 5.2
- **Files:** None
- **Estimated scope:** XS (0 files)

---

### Checkpoint 4: Merge Readiness
- [ ] All gates in `validation.md` §7 satisfied.
- [ ] Human approval recorded.
- [ ] Branch deleted after merge.

---

## Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| CI runner has no interactive desktop or webcam | High | Existing BDD injects `FakeCameraService` and stubs; first CI run is the proof. Fix failures, do not filter tests. |
| `-warnaserror` trips on Reqnroll-generated code | Medium | Fix at source or scope `NoWarn` to generated files only. |
| ONNX model load (50 MB) inflates CI duration | Medium | Model copied from output; 20-minute timeout measured on first run. |
| Launching without measured memory/UI behavior under continuous inference | High | Accepted for launch speed; recorded as a known gap in the checklist, with an ad-hoc pre-pilot soak recommended before wider distribution. |
| No automated drag-path test | Medium | Manual checklist step (drag + reload, drop-on-self) executed against the portable build. |
| Brand assets stay untracked or README paths break on GitHub | Medium | Commit `Assets/app-icon.ico` and `docs/assets/*`; verify links with `git ls-files` and a rendered check of the branch. |
| Icon wiring breaks XAML resource loading or adds a warning | Low | `<ApplicationIcon>` for the executable, `<Resource>` + `Window.Icon` for the window; build and launch smoke in the same task. |
| Startup extraction alters the success path | High | Coordinator success-path test, DI tests, and a manual cold start after the change. |
| New scenarios leak temp roots or share state | Medium | Follow the existing per-scenario container/temp-root pattern and dispose. |
| Checklist becomes a rubber stamp | High | Evidence required per item; clean-clone rebuild; smoke matrix against the published output. |
| Scope creeps back into deferred work (performance verification, drag refactor, changelog) | Medium | Explicit deferral list in `requirements.md` §2.2; changes require a new task. |

## Approvals Required (before implementation)

1. CI on GitHub-hosted runners for `ricky-kiva/pcb-deftek`.
2. Version `0.10.0`; release notes live in the launch checklist rather than `CHANGELOG.md`.
3. Launch scope: portable `win-x64` publish only; installer/MSIX, code signing, splash branding, and release publication stay in `mission.md` §5.2. The official application icon and README brand assets are pulled into this phase.
4. Startup behavior change: migration failure exits with code 1 instead of leaving a window-less process.
5. Acceptance of the deferred items in `requirements.md` §2.2 — including performance verification under continuous inference — as documented launch gaps.

## Open Questions

None. The five items above are approval gates with stated defaults.
