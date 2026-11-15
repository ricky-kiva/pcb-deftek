# Requirements & Context: Phase 10 — Hardening, BDD Verification & Launch Readiness

## 1. Executive Summary & Context

Phases 1–9 delivered every essential feature in `mission.md` §5.1 with 834 passing tests (790 unit, 44 BDD), 0 warnings, and a working PDF export engine. Phase 10 adds only what is needed to launch: a CI gate, two test-only BDD scenarios for untested failure/transition branches, a recorded manual performance soak, a startup-failure fix, release metadata, and a launch checklist with a Go/No-Go verdict.

The goal is **speed to launch**, so verification is limited to what a first release actually requires. Deferred work is listed explicitly in §2.2 and must appear as known gaps in the launch checklist.

### Current State (verified)

| Concern | Existing behavior | Evidence |
|---|---|---|
| CI/CD | None; no workflow files in the repository | Repository listing |
| Test baseline | 834 passing (790 unit + 44 BDD), 0 skipped | Phase 9 `validation.md` §2.2 |
| BDD coverage | 10 features / 44 scenarios; batch ETA, camera auto-hide, export success/no-match/cancel, viewer default+focused all covered | `tests/pcb-deftek.Bdd/Features/` |
| Viewer focus reset | No scenario returns from focused → default | `HistoryAndDefectViewer.feature` |
| Export failure | Failure toast and cleanup exist in code; no scenario | `AuditPdfExport.feature`, `DashboardPdfExportService.cs:274` |
| Dashboard drag-and-drop | View-model reorder + persistence covered indirectly; `DragDrop` payload and `OnCardDrop` untested | `DashboardView.xaml.cs`, `DefectStatisticsDashboard.feature` |
| Live telemetry | HUD exposes FPS and average latency | `Domain/Models/LiveInspectionTelemetry.cs` |
| Performance measurement | None retained | Repository listing |
| Startup failure | Migration failure rethrown from `async void OnStartup`; dispatcher sets `Handled = true` | `App.xaml.cs` |
| Logging | Serilog rolling files, 30 retained, `%LOCALAPPDATA%\PcbDeftek\pcb-deftek\logs` | `LoggingBootstrapper.cs` |
| Versioning / packaging | No version metadata, changelog, installer, or icon | `pcb-deftek.csproj`, repository listing |
| Documentation | `README.md` is a 12-byte stub | `README.md` |

## 2. Scope

### 2.1 In Scope

1. **CI** — GitHub Actions workflow: Release build with warnings-as-errors, separate unit and BDD test runs, result artifacts.
2. **README** — prerequisites, commands, test isolation rule, data/log locations.
3. **BDD closure (test-only)** — viewer focused→default transition; export failure notification and cleanup.
4. **Manual performance verification** — 10-minute live-inference soak with recorded memory, FPS, latency, responsiveness, and log cleanliness.
5. **Startup hardening** — a testable coordinator so migration failure logs, shows one dialog, exits with code 1, and leaves no window; routing failure stays non-fatal.
6. **Release identity** — `0.10.0` metadata in the project.
7. **Launch readiness** — checklist with evidence, release notes, known gaps, portable publish smoke run, and one Go/No-Go.

### 2.2 Deferred (explicitly out of scope)

| Deferred item | Why deferred | Where it is acknowledged |
|---|---|---|
| Full performance harness (dispatching probe, memory sampler, reproducibility gates) | Manual soak satisfies the roadmap requirement at a fraction of the cost | Launch checklist known gaps |
| Dashboard drag-path refactor (`DashboardCardDragData`, `TryApplyCardDrop`) and its BDD scenario | Production refactor right before launch buys test coverage at code risk; the reorder behavior is already covered through the view model | Manual smoke step: drag a card, reload, drop on self |
| Separate `CHANGELOG.md` | Release notes are written inside the launch checklist for this build | Launch checklist release-notes section |
| Installer/MSIX, code signing, application icon, branding, GitHub Release publication | `mission.md` §5.2 future enhancements | Checklist known gaps |
| Coverage tooling/coverage gate | Would require a new dependency for no launch requirement | Not tracked |
| Multi-instance enforcement | Product decision with UX consequences; surfaced rather than invented | Checklist risk entry |
| Performance optimization of inference | Phase 10 measures; improvement needs its own phase | Not tracked |

Also out of scope: new features, UI redesign, schema changes, new settings keys, new NuGet packages, localization, auto-update.

## 3. Decisions & Rationale

| # | Decision | Rationale |
|---|---|---|
| D1 | Essential-only scope: CI, two test-only scenarios, manual soak, startup fix, version, checklist. | Minimizes time to a verifiable, distributable build; nothing in the list can be replaced by documentation alone except the soak's depth. |
| D2 | CI is GitHub Actions on `windows-latest`, one job: restore → Release build (`-warnaserror`) → unit tests → BDD tests → artifact upload. | Windows is mandatory for WPF/DirectML/OpenCvSharp; one job is fastest; separate test steps keep failures attributable. |
| D3 | No `global.json`; the SDK is pinned by `dotnet-version: 10.0.x` in the workflow. | Avoids a project-level persistent configuration change (`tech-stack.md` §5). |
| D4 | No coverage package or coverage gate. `.trx` results are the CI evidence. | New dependency with no launch requirement. |
| D5 | CI runs the projects unfiltered; no test category is excluded. | Hardware already goes through interfaces and BDD injects fakes; a CI failure is a defect, not a filtering problem. |
| D6 | Warnings are errors in CI only; no `TreatWarningsAsErrors` property is added. | One visible gate; local builds stay fast. |
| D7 | Performance is verified manually, not with a harness: Release build, 10-minute live run, samples at t=2 and t=10 minutes, HUD telemetry plus process memory. | Satisfies `roadmap.md` Phase 10 at the depth a first release needs and avoids committing test-only tooling. |
| D8 | Performance gates are relative: working-set growth ≤ 50 MB after warm-up; FPS and average latency within 25 % between the two samples; no UI freeze > 1 s; no `Error`/`Fatal` log entries. | Machine-independent trend checks that catch leaks and freezes without false failures. |
| D9 | If no camera is available, the performance gate is recorded as untested and becomes a launch gap/No-Go condition for camera validation. | Honest evidence beats fabricated numbers. |
| D10 | BDD additions are test-only: viewer focus reset and export failure. No production code changes for testability. | The two branches are the highest-trust risks at launch; the drag path is manual because testing it requires a refactor. |
| D11 | Startup sequencing moves to `Infrastructure/Startup/StartupCoordinator` returning a result; `App.OnStartup` owns logging, the dialog, and `Shutdown(1)`. | Fixes a real defect (window-less process) with a small, testable unit. |
| D12 | Startup routing failure remains non-fatal (warning + default view). | A preference lookup must not block inspection work. |
| D13 | Version `0.10.0` in project metadata; no tag until merge with human approval; release notes live in the launch checklist. | Traceable build without a release ceremony; avoids a second documentation file. |
| D14 | Launch scope is a portable framework-dependent `win-x64` publish. Multi-instance behavior is documented, not blocked. | Matches `mission.md` §5.2 and keeps the distribution decision explicit. |
| D15 | No new NuGet packages, migrations, or settings keys. | Freezes the dependency and schema surface for launch. |
| D16 | BDD scenarios reuse the isolated-database, temp-root, and stub-service pattern. | Tests never touch real user data. |

## 4. CI Contract

| Concern | Requirement |
|---|---|
| Triggers | `push`/`pull_request` to `main`, `workflow_dispatch` |
| Runner | `windows-latest`, timeout ≤ 20 minutes |
| SDK | `actions/setup-dotnet` with `10.0.x`; no `global.json` |
| Cache | NuGet packages keyed by `hashFiles('**/*.csproj')` |
| Restore | `dotnet restore pcb-deftek.slnx` |
| Build | `dotnet build pcb-deftek.slnx -c Release --no-restore -warnaserror` |
| Unit tests | `dotnet test tests/pcb-deftek.Tests/pcb-deftek.Tests.csproj -c Release --no-build --no-restore --logger trx` |
| BDD tests | `dotnet test tests/pcb-deftek.Bdd/pcb-deftek.Bdd.csproj -c Release --no-build --no-restore --logger trx` |
| Artifacts | `.trx` uploaded with `if: always()` |
| Permissions / secrets | `contents: read`; no secrets |
| Exclusions | None |

## 5. Manual Performance Verification Protocol

**Setup:** Release build (`dotnet run -c Release --project pcb-deftek.csproj` or the published output). Real camera where available. Settings left at defaults; telemetry enabled.

1. Open Live Detection; discard the first 60 seconds as warm-up.
2. At t = 2 min record: working set, private bytes (`Get-Process pcb-deftek | Select WorkingSet64, PrivateMemorySize64`), HUD FPS, HUD average latency.
3. At t = 5 min exercise the UI: Dashboard → History → back, open the viewer and close it, move the confidence slider, toggle telemetry. Note any freeze over 1 s.
4. At t = 10 min record the same four values.
5. Check the log (`%LOCALAPPDATA%\PcbDeftek\pcb-deftek\logs\app-*.log`) for `ERR`/`FTL` entries during the window.
6. Record machine context (OS build, CPU, RAM, GPU) and the computed deltas in `validation.md` §9.

**Gates:**

| Gate | Threshold |
|---|---|
| Working-set growth (t=2 → t=10, after warm-up) | ≤ 50 MB |
| FPS change (t=2 → t=10) | ≤ 25 % decline |
| Average latency change (t=2 → t=10) | ≤ 25 % increase |
| UI freeze during interaction | none over 1 s |
| Stability | no crash, no unhandled-exception dialog, no `ERR`/`Fatal` log entries |
| Depth limitation | no dispatcher-stall percentile measurement, no automated repetition — recorded as a known gap |

A failed gate is either fixed, or accepted in writing in the launch checklist with the observed number.

## 6. BDD Coverage Contract

| Workflow | Coverage | Phase 10 action |
|---|---|---|
| Batch ETA | `FileBatchDetection.feature` — ETA computed, 100 % progress, records persisted | Verify only |
| Camera auto-hide | `LiveCameraInspection.feature` — controls hide, status/telemetry preserved, mouse restores | Verify only |
| Viewer sidebar transitions | Default and focused covered | Add focused → default |
| Dashboard drag-and-drop | Reorder + persistence via view-model command | Deferred to manual smoke step |
| Export notifications | Success, no-match, decline, cancel covered | Add failure notification and cleanup |

New scenarios assert observable behavior only (state values, toast title, file absence).

## 7. Launch Readiness Contract

`launch-checklist.md` (produced with the `shipping-and-launch` skill) must contain:

1. **Build & reproducibility** — clean-clone restore/build/test; 0 warnings; publish command; artifact contents and size.
2. **Runtime prerequisites** — Windows 10/11 x64, .NET 10 desktop runtime, optional webcam, optional DirectML GPU.
3. **Functional smoke matrix** — every essential feature in `mission.md` §5.1, plus the manual drag steps (drag + reload, drop on self) and the performance soak summary.
4. **Data safety** — user-owned images untouched; app-owned captures only; database/export locations; backup/restore guidance.
5. **Diagnostics** — log location/retention, startup failure behavior, dialog suppression, no silent catches on critical paths.
6. **Security & dependencies** — `dotnet list package --vulnerable`, secrets/absolute-path scan, no network calls or telemetry.
7. **Release notes for 0.10.0** — Added / Changed / Fixed, derived from phase specs; Known Limitations (no installer, unsigned, no icon, multi-instance allowed, deferred verification).
8. **Verdict** — per-category pass/fail with evidence and one Go/No-Go with conditions.

A Go requires: clean-clone build, full suite green, portable build starts and completes the smoke matrix, export produces a valid PDF, deletion policy verified, logs present, no vulnerable dependency, no known data-loss defect, performance gates met or explicitly accepted.

## 8. Technical Constraints

- Windows-only WPF (`net10.0-windows`), nullable enabled, 0 warnings in Release CI.
- No new NuGet packages, migrations, or settings keys.
- CI must not require a camera, interactive desktop, or DirectML GPU.
- BDD/unit tests isolate databases, captures, report directories, and settings under per-test temp roots.
- The only production behavior change is the startup failure path.
- Version metadata is the only project-file change.
- Performance numbers must come from an actual run; no estimates.

## 9. Edge Cases

| Case | Expected behavior |
|---|---|
| CI runner has no webcam/GPU | BDD uses fakes and the CPU provider; no device is opened |
| Reqnroll-generated code warns | Fix at source or scope `NoWarn` to generated files only |
| No camera for the soak | Gate recorded untested; listed as a launch gap |
| Soak run on a loaded machine | Rerun before recording; a waived gate needs the observed number |
| Viewer focus cleared | Default sidebar restored; delete availability unchanged |
| Export destination occupied by a file | `Failed` state, failure toast, no artifacts |
| Migration fails at startup | Fatal log, one dialog, exit code 1, no window |
| Routing fails at startup | Warning log, default view, app usable |
| Portable publish without .NET 10 runtime | Framework-dependent build reports the missing runtime; documented prerequisite |
| Existing `app.db` from an earlier phase | Migration runner upgrades it; checklist verifies on a copy |

## 10. Open Questions

None. Approval gates are listed in `plan.md`: CI runner usage, version/release-note placement, portable-only launch scope, the startup behavior change, and acceptance of the deferred items in §2.2.
