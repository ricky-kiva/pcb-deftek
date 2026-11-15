# Implementation Plan: Phase 10 — Hardening, BDD Verification & Launch Readiness

## Overview

Phase 10 closes the roadmap with the smallest set of work that makes the shipped product **verifiable and distributable**. Twelve tasks in six groups: CI, two test-only BDD scenarios, one manual performance soak, one startup-safety fix, release metadata, the launch checklist, and the merge gates.

Deliberately deferred to keep launch fast (recorded in `requirements.md` §2.2 and as known gaps in the launch checklist): the four-task performance harness, the dashboard drag-path refactor and its BDD scenario, and a separate changelog file. The roadmap's performance requirement is satisfied by a recorded manual soak instead of a tool; the drag path is covered by a documented manual smoke step; release notes live inside the launch checklist.

Task list target: **this `plan.md`** — project convention is `specs/<date>-<feature>/{plan,requirements,validation}.md`, so no `tasks/` directory is created.

## Architecture & Dependency Graph

```
1. CI + README ──────────────────────────── gate for everything below
        │
2. BDD: viewer focus reset, export failure   (test-only, no production change)
3. Manual performance soak + recorded numbers (documentation only)
4. Startup failure recovery ── StartupCoordinator ── App.OnStartup
        │
5. Version metadata + launch checklist (portable publish, Go/No-Go)
        │
6. Full gates → review pipeline → PR → human approval → merge
```

Ordering rationale: CI first so later changes are gated; groups 2–4 are independent; the checklist is written last because it evaluates the final build; the review pipeline and PR close the `AGENTS.md` lifecycle.

## Task Breakdown

### Task Group 1: CI/CD Build & Test Automation

#### Task 1.1: GitHub Actions CI Pipeline

- **Description:** Add one workflow that restores, builds in Release with warnings as errors, and runs the unit and BDD projects separately, publishing `.trx` results on every outcome.
- **Acceptance criteria:**
  - [ ] `.github/workflows/ci.yml` triggers on `push`/`pull_request` to `main` plus `workflow_dispatch`; `permissions: contents: read`; concurrency cancels superseded runs.
  - [ ] Runs on `windows-latest`, `actions/setup-dotnet` pinned to `10.0.x`, NuGet cache keyed by `hashFiles('**/*.csproj')`.
  - [ ] `dotnet build pcb-deftek.slnx -c Release --no-restore -warnaserror` — any warning fails the job.
  - [ ] Unit and BDD tests run separately with `-c Release --no-build --no-restore --logger trx`; results upload with `if: always()`.
  - [ ] No secrets, no new NuGet package, job under 20 minutes.
- **Verification:**
  - [ ] Workflow run URL and duration recorded in `validation.md` §6.
  - [ ] The same commands pass locally in Release with 0 warnings.
- **Dependencies:** None
- **Files:**
  - `.github/workflows/ci.yml`
- **Estimated scope:** S (1 file)

#### Task 1.2: Minimal README

- **Description:** Replace the 12-byte stub with only what a new contributor or reviewer needs to build, run, and test.
- **Acceptance criteria:**
  - [ ] Prerequisites: Windows x64, .NET 10 SDK, optional webcam, optional DirectML GPU.
  - [ ] Commands: `dotnet restore`, `dotnet build pcb-deftek.slnx -c Release`, `dotnet run --project pcb-deftek.csproj`, `dotnet test pcb-deftek.slnx`.
  - [ ] States that tests use isolated temp databases/roots and never touch real user data.
  - [ ] Lists data/log locations: `%LOCALAPPDATA%\PcbDeftek\pcb-deftek\{app.db,logs,Captures}`, `Downloads\PCB Deftek\{Report,Backup,Images}`.
  - [ ] No secrets or developer-specific paths.
- **Verification:**
  - [ ] Every documented command executed from the repository root.
- **Dependencies:** Task 1.1
- **Files:**
  - `README.md`
- **Estimated scope:** XS (1 file)

---

### Checkpoint 1: CI Enforces the Quality Bar
- [ ] CI green on the branch; blocks on warnings and test failures.
- [ ] README commands verified.
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

### Task Group 3: Manual Performance Verification

#### Task 3.1: Live-Inference Soak & Recorded Baseline

- **Description:** Measure the roadmap's memory and responsiveness requirement by hand on the Release build, instead of building a harness. Numbers are recorded; no code is added.
- **Acceptance criteria:**
  - [ ] 10-minute continuous Live Detection run (real camera; if none is available, mark the gate untested and record it as a launch gap).
  - [ ] First 60 seconds are warm-up; samples taken at t = 2 min and t = 10 min.
  - [ ] Working set, private bytes, HUD FPS, and HUD average latency recorded at both samples.
  - [ ] UI responsiveness checked at t = 5 min: navigate Dashboard → History → back, open the viewer, move the confidence slider, toggle telemetry; no freeze over 1 s.
  - [ ] No crash, no unhandled-exception dialog, and no `Error`/`Fatal` entries in the app log during the soak.
  - [ ] Results and machine context (OS build, CPU, RAM, GPU) recorded in `validation.md` §9; each gate in §5 marked met, failed, or waived.
- **Verification:**
  - [ ] Gate verdicts match the recorded numbers; a failed gate is a merge blocker or an explicitly accepted risk.
- **Dependencies:** Tasks 1.1, 1.2
- **Files:**
  - `specs/2026-11-15-hardening-bdd-launch-readiness/validation.md`
- **Estimated scope:** XS (1 file + manual run)

---

### Task Group 4: Startup Failure Recovery

#### Task 4.1: Startup Coordinator & Failure Path

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

### Checkpoint 2: This Build Is Measured and Fails Cleanly
- [ ] Both new BDD scenarios pass; existing 44 scenarios unchanged.
- [ ] Startup failure path verified manually and by tests.
- [ ] Soak gates recorded (or explicitly waived with a gap note).
- [ ] Human review before Group 5.

---

### Task Group 5: Release Identity & Launch Readiness

#### Task 5.1: Version Metadata

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

#### Task 5.2: Launch-Readiness Checklist & Go/No-Go

- **Description:** Produce the launch artifact with the `shipping-and-launch` skill: evidence per item, release notes, known gaps, and one Go/No-Go verdict for a portable build.
- **Acceptance criteria:**
  - [ ] Covers: clean-clone build; full suite; Release publish; artifact contents/size; runtime prerequisites; functional smoke matrix for every essential feature in `mission.md` §5.1; data-safety checks; log/error behavior; `dotnet list package --vulnerable`; secrets/absolute-path scan; known gaps.
  - [ ] Functional smoke matrix includes the two manual steps that replace the deferred BDD drag scenario: drag a metric card and confirm persistence after reload; drop a card on itself (no change).
  - [ ] Release notes for `0.10.0` are included in the checklist (Added / Changed / Fixed / Known Limitations), derived from phase specs — no separate changelog file.
  - [ ] `dotnet publish -c Release -r win-x64 --self-contained false` executed; output launched; smoke matrix run against it.
  - [ ] Every item records pass/fail with evidence; one overall Go/No-Go with conditions.
  - [ ] Scope stated: no installer/MSIX, no signing, no icon, no GitHub Release publication (`mission.md` §5.2).
  - [ ] Known gaps recorded: no automated drag-path test, no dispatcher-stall probe, manual-only performance numbers, multi-instance behavior.
- **Verification:**
  - [ ] Checklist re-walked after `git clean -xdf`; same verdict.
- **Dependencies:** Tasks 2.1, 2.2, 3.1, 4.1, 5.1
- **Files:**
  - `specs/2026-11-15-hardening-bdd-launch-readiness/launch-checklist.md`
- **Estimated scope:** M (1 file + manual runs)

---

### Checkpoint 3: Launch Package Assessed
- [ ] Version, release notes, and checklist exist and agree.
- [ ] Portable build runs and completes the smoke matrix.
- [ ] Go/No-Go stated with evidence.
- [ ] Human review before Group 6.

---

### Task Group 6: Final Verification & Merge Readiness

#### Task 6.1: Full Gate Run

- **Description:** Execute and record every gate in `validation.md`.
- **Acceptance criteria:**
  - [ ] `dotnet build pcb-deftek.slnx -c Release -warnaserror` → 0 errors, 0 warnings.
  - [ ] `dotnet test pcb-deftek.slnx` → 100 % pass, 0 skipped, no regression against the 834-test baseline.
  - [ ] CI green on the final commit; performance gates recorded; launch verdict recorded.
- **Verification:**
  - [ ] `validation.md` §9 updated with commands and counts.
- **Dependencies:** Task Groups 1–5
- **Files:**
  - `specs/2026-11-15-hardening-bdd-launch-readiness/validation.md`
- **Estimated scope:** XS (1 file)

#### Task 6.2: Review Pipeline

- **Description:** Execute the gated `AGENTS.md` §7 pipeline, asking before each stage.
- **Acceptance criteria:**
  - [ ] `code-review-and-quality` completed; findings recorded.
  - [ ] `code-simplification`, `performance-optimization`, `security-and-hardening` executed only after explicit approval; outcomes recorded.
  - [ ] Blocking findings fixed; non-blocking recorded as follow-ups.
- **Verification:**
  - [ ] `validation.md` §10 lists each stage and verdict.
- **Dependencies:** Task 6.1
- **Files:**
  - `specs/2026-11-15-hardening-bdd-launch-readiness/validation.md`
- **Estimated scope:** XS (1 file)

#### Task 6.3: PR, Human Approval, Merge

- **Description:** Complete the `AGENTS.md` §4 lifecycle.
- **Acceptance criteria:**
  - [ ] PR links to the Phase 10 issue and lists gate results.
  - [ ] Human approval recorded; PR not self-merged.
  - [ ] Merge to `main`, issue closed, branch deleted; plan checkboxes updated.
- **Verification:**
  - [ ] `git log --oneline -5` on `main` shows the merge; branch gone.
- **Dependencies:** Task 6.2
- **Files:** None
- **Estimated scope:** XS (0 files)

---

### Checkpoint 4: Merge Readiness
- [ ] All gates in `validation.md` §8 satisfied.
- [ ] Human approval recorded.
- [ ] Branch deleted after merge.

---

## Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| CI runner has no interactive desktop or webcam | High | Existing BDD injects `FakeCameraService` and stubs; first CI run is the proof. Fix failures, do not filter tests. |
| `-warnaserror` trips on Reqnroll-generated code | Medium | Fix at source or scope `NoWarn` to generated files only. |
| ONNX model load (50 MB) inflates CI duration | Medium | Model copied from output; 20-minute timeout measured on first run. |
| Manual soak numbers are noisy or unrepeatable | Medium | Warm-up excluded; both samples recorded; rerun before waiving a failed gate; machine context captured. |
| No automated drag-path test | Medium | Manual checklist step (drag + reload, drop-on-self) executed against the portable build. |
| Startup extraction alters the success path | High | Coordinator success-path test, DI tests, and a manual cold start after the change. |
| New scenarios leak temp roots or share state | Medium | Follow the existing per-scenario container/temp-root pattern and dispose. |
| Checklist becomes a rubber stamp | High | Evidence required per item; clean-clone rebuild; smoke matrix against the published output. |
| Scope creeps back into deferred work (harness, drag refactor, changelog) | Medium | Explicit deferral list in `requirements.md` §2.2; changes require a new task. |

## Approvals Required (before implementation)

1. CI on GitHub-hosted runners for `ricky-kiva/pcb-deftek`.
2. Version `0.10.0`; release notes live in the launch checklist rather than `CHANGELOG.md`.
3. Launch scope: portable `win-x64` publish only; installer/MSIX, signing, icon, and release publication stay in `mission.md` §5.2.
4. Startup behavior change: migration failure exits with code 1 instead of leaving a window-less process.
5. Acceptance of the deferred items listed in `requirements.md` §2.2 as documented launch gaps.

## Open Questions

None. The five items above are approval gates with stated defaults.
