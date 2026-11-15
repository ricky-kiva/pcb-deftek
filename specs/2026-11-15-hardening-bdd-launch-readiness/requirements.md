# Requirements & Context: Phase 10 — Hardening, BDD Verification & Launch Readiness

## 1. Executive Summary & Context

Phases 1–9 delivered every essential feature in `mission.md` §5.1 with 834 passing tests (790 unit, 44 BDD), 0 warnings, and a working PDF export engine. Phase 10 adds only what is needed to launch: a CI gate, the application icon, a branded README, two test-only BDD scenarios for untested failure/transition branches, a startup-failure fix, release metadata, and a launch checklist with a Go/No-Go verdict.

The goal is **speed to launch**. Performance verification under continuous camera inference (`roadmap.md` Phase 10 bullet 2) is consciously deferred: it requires either a retained harness or a dedicated soak session, and both are recorded as a launch gap rather than implemented here. Deferred work is listed in §2.2 and must appear as known gaps in the launch checklist.

### Current State (verified)

| Concern | Existing behavior | Evidence |
|---|---|---|
| CI/CD | None; no workflow files in the repository | Repository listing |
| Test baseline | 834 passing (790 unit + 44 BDD), 0 skipped | Phase 9 `validation.md` §2.2 |
| BDD coverage | 10 features / 44 scenarios; batch ETA, camera auto-hide, export success/no-match/cancel, viewer default+focused covered | `tests/pcb-deftek.Bdd/Features/` |
| Viewer focus reset | No scenario returns from focused → default | `HistoryAndDefectViewer.feature` |
| Export failure | Failure toast and cleanup exist in code; no scenario | `AuditPdfExport.feature`, `DashboardPdfExportService.cs:274` |
| Dashboard drag-and-drop | View-model reorder + persistence covered indirectly; `DragDrop` payload and `OnCardDrop` untested | `DashboardView.xaml.cs`, `DefectStatisticsDashboard.feature` |
| Live telemetry | HUD exposes FPS and average latency; no memory metric | `Domain/Models/LiveInspectionTelemetry.cs` |
| Performance measurement | None retained | Repository listing |
| Startup failure | Migration failure rethrown from `async void OnStartup`; dispatcher sets `Handled = true` | `App.xaml.cs` |
| Logging | Serilog rolling files, 30 retained, `%LOCALAPPDATA%\PcbDeftek\pcb-deftek\logs` | `LoggingBootstrapper.cs` |
| Versioning / packaging | No version metadata, changelog, or installer; no icon wired, though `Assets/app-icon.ico` is staged (untracked) | `pcb-deftek.csproj`, repository listing |
| Documentation | `README.md` is a 12-byte stub; logo and four page screenshots staged under `docs/assets/` (untracked) | `README.md`, `docs/assets/` |

## 2. Scope

### 2.1 In Scope

1. **CI and application icon** — GitHub Actions workflow: Release build with warnings-as-errors, separate unit and BDD test runs, result artifacts; `Assets/app-icon.ico` wired as executable and window icon.
2. **README with brand assets** — logo, product scope statement, four page screenshots, prerequisites, commands, test isolation rule, data/log locations, and the screenshot naming convention.
3. **BDD closure (test-only)** — viewer focused→default transition; export failure notification and cleanup.
4. **Startup hardening** — a testable coordinator so migration failure logs, shows one dialog, exits with code 1, and leaves no window; routing failure stays non-fatal.
5. **Release identity** — `0.10.0` metadata in the project.
6. **Launch readiness** — checklist with evidence, release notes, known gaps, portable publish smoke run, and one Go/No-Go.

### 2.2 Deferred (explicitly out of scope)

| Deferred item | Why deferred | Where it is acknowledged |
|---|---|---|
| Performance verification under continuous camera inference (`roadmap.md` Phase 10 bullet 2) | Requires a harness or a timed soak; not required to prove the build is distributable | Launch checklist known gaps + Go/No-Go condition |
| Full performance harness (dispatcher probe, memory sampler, reproducibility gates) | Depends on the item above | Launch checklist known gaps |
| Dashboard drag-path refactor (`DashboardCardDragData`, `TryApplyCardDrop`) and its BDD scenario | Production refactor right before launch buys test coverage at code risk; the reorder behavior is already covered through the view model | Manual smoke step: drag a card, reload, drop on self |
| Separate `CHANGELOG.md` | Release notes are written inside the launch checklist for this build | Launch checklist release-notes section |
| Installer/MSIX, code signing, splash branding, GitHub Release publication | `mission.md` §5.2 future enhancements | Checklist known gaps |
| Coverage tooling/coverage gate | New dependency with no launch requirement | Not tracked |
| Multi-instance enforcement | Product decision with UX consequences; surfaced rather than invented | Checklist risk entry |
| Performance optimization of inference | Measurement must precede optimization | Not tracked |

The application icon, previously listed under `mission.md` §5.2, is pulled into Phase 10 and therefore no longer deferred.

Also out of scope: new features, UI redesign, schema changes, new settings keys, new NuGet packages, localization, auto-update.

## 3. Decisions & Rationale

| # | Decision | Rationale |
|---|---|---|
| D1 | Essential-only scope: CI, two test-only scenarios, startup fix, version, checklist, merge gates. | Minimizes time to a verifiable, distributable build; every remaining task is either a gate or a launch artifact. |
| D2 | CI is GitHub Actions on `windows-latest`, one job: restore → Release build (`-warnaserror`) → unit tests → BDD tests → artifact upload. | Windows is mandatory for WPF/DirectML/OpenCvSharp; one job is fastest; separate test steps keep failures attributable. |
| D3 | No `global.json`; the SDK is pinned by `dotnet-version: 10.0.x` in the workflow. | Avoids a project-level persistent configuration change (`tech-stack.md` §5). |
| D4 | No coverage package or coverage gate. `.trx` results are the CI evidence. | New dependency with no launch requirement. |
| D5 | CI runs the projects unfiltered; no test category is excluded. | Hardware already goes through interfaces and BDD injects fakes; a CI failure is a defect, not a filtering problem. |
| D6 | Warnings are errors in CI only; no `TreatWarningsAsErrors` property is added. | One visible gate; local builds stay fast. |
| D7 | BDD additions are test-only: viewer focus reset and export failure. No production code changes for testability. | The two branches are the highest-trust risks at launch; the drag path would require a refactor. |
| D8 | Performance verification is deferred and recorded as a known gap; no harness, no soak task. | Accepted roadmap deviation: it does not block proving the build is distributable, and the checklist carries the risk explicitly. |
| D9 | Startup sequencing moves to `Infrastructure/Startup/StartupCoordinator` returning a result; `App.OnStartup` owns logging, the dialog, and `Shutdown(1)`. | Fixes a real defect (window-less process) with a small, testable unit. |
| D10 | Startup routing failure remains non-fatal (warning + default view). | A preference lookup must not block inspection work. |
| D11 | Version `0.10.0` in project metadata; no tag until merge with human approval; release notes live in the launch checklist. | Traceable build without a release ceremony; avoids a second documentation file. |
| D12 | Launch scope is a portable framework-dependent `win-x64` publish. Multi-instance behavior is documented, not blocked. | Matches `mission.md` §5.2 and keeps the distribution decision explicit. |
| D13 | No new NuGet packages, migrations, or settings keys. | Freezes the dependency and schema surface for launch. |
| D14 | BDD scenarios reuse the isolated-database, temp-root, and stub-service pattern. | Tests never touch real user data. |
| D15 | Dashboard drag-and-drop is covered by a manual smoke step in the checklist, not automation. | The behavior already passes through the tested view-model path; manual verification is sufficient for launch. |
| D16 | The application icon ships in Phase 10: `<ApplicationIcon>Assets\app-icon.ico</ApplicationIcon>` for the executable plus `<Resource>` and `Window.Icon` for the window/taskbar. | The asset is already produced (six-size ICO, 362 KB) and an unbranded executable/taskbar entry undermines an otherwise finished launch package. |
| D17 | README brand assets live in `docs/assets/` with fixed names: logo `readme-logo-1-1-c.png`; screenshots `readme-ss-page-{page_name}-{int_image_number}-{int_revision}-c.jpg` (currently `1`/`1` for dashboard, live-detection, file-detection, history). Assets are committed so relative Markdown links render on GitHub. | Deterministic names let later revisions be added without rewriting the README or breaking links; only existing pages are shown. |

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

## 5. BDD Coverage Contract

| Workflow | Coverage | Phase 10 action |
|---|---|---|
| Batch ETA | `FileBatchDetection.feature` — ETA computed, 100 % progress, records persisted | Verify only |
| Camera auto-hide | `LiveCameraInspection.feature` — controls hide, status/telemetry preserved, mouse restores | Verify only |
| Viewer sidebar transitions | Default and focused covered | Add focused → default |
| Dashboard drag-and-drop | Reorder + persistence via view-model command | Manual smoke step (deferred automation) |
| Export notifications | Success, no-match, decline, cancel covered | Add failure notification and cleanup |

New scenarios assert observable behavior only (state values, toast title, file absence).

## 6. Launch Readiness Contract

`launch-checklist.md` (produced with the `shipping-and-launch` skill) must contain:

1. **Build & reproducibility** — clean-clone restore/build/test; 0 warnings; publish command; artifact contents and size.
2. **Runtime prerequisites** — Windows 10/11 x64, .NET 10 desktop runtime, optional webcam, optional DirectML GPU.
3. **Functional smoke matrix** — every essential feature in `mission.md` §5.1, including the manual drag steps (drag + reload, drop on self), a live-camera pass (streaming, overlays, telemetry, auto-hide, capture) where hardware exists, and confirmation that the executable, title bar, and taskbar show `Assets/app-icon.ico`.
4. **Data safety** — user-owned images untouched; app-owned captures only; database/export locations; backup/restore guidance.
5. **Diagnostics** — log location/retention, startup failure behavior, dialog suppression, no silent catches on critical paths.
6. **Security & dependencies** — `dotnet list package --vulnerable`, secrets/absolute-path scan, no network calls or telemetry.
7. **Release notes for 0.10.0** — Added / Changed / Fixed, derived from phase specs; Known Limitations (performance under continuous inference unverified, no installer, unsigned, splash branding deferred, multi-instance allowed, no automated drag-path test). README logo and screenshots verified as resolving from `docs/assets/`.
8. **Verdict** — per-category pass/fail with evidence and one Go/No-Go with conditions.

A Go requires: clean-clone build, full suite green, portable build starts and completes the smoke matrix, export produces a valid PDF, deletion policy verified, logs present, no vulnerable dependency, and no known data-loss defect. Unverified performance is an accepted condition of the verdict, not a silent omission.

## 7. Technical Constraints

- Windows-only WPF (`net10.0-windows`), nullable enabled, 0 warnings in Release CI.
- No new NuGet packages, migrations, or settings keys.
- CI must not require a camera, interactive desktop, or DirectML GPU.
- BDD/unit tests isolate databases, captures, report directories, and settings under per-test temp roots.
- The only production behavior change is the startup failure path.
- Version metadata is the only project-file change.
- No performance claim may be recorded as verified without measured evidence; the checklist states it is unverified.

## 8. Edge Cases

| Case | Expected behavior |
|---|---|
| CI runner has no webcam/GPU | BDD uses fakes and the CPU provider; no device is opened |
| Reqnroll-generated code warns | Fix at source or scope `NoWarn` to generated files only |
| Viewer focus cleared | Default sidebar restored; delete availability unchanged |
| Export destination occupied by a file | `Failed` state, failure toast, no artifacts |
| Migration fails at startup | Fatal log, one dialog, exit code 1, no window |
| Routing fails at startup | Warning log, default view, app usable |
| No camera on the launch machine | Checklist marks camera features not verified on that machine; recorded as a gap |
| Icon asset absent or untracked | `ApplicationIcon` points at a missing file → build error; the asset must be tracked before it is referenced |
| README image path wrong, case-mismatched, or untracked | Links render broken on GitHub; verify with `git ls-files docs/assets` and a rendered check |
| A page has no screenshot | No image is invented; only the four staged captures appear |
| Portable publish without .NET 10 runtime | Framework-dependent build reports the missing runtime; documented prerequisite |
| Existing `app.db` from an earlier phase | Migration runner upgrades it; checklist verifies on a copy |

## 9. Open Questions

None. Approval gates are listed in `plan.md`: CI runner usage, version/release-note placement, portable-only launch scope, the startup behavior change, and acceptance of the deferred items in §2.2 — including the deferred performance verification.
