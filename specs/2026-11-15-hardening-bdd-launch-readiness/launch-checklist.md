# Launch Readiness Checklist: PCB Deftek 0.10.0

Prepared 2026-11-15 with the `shipping-and-launch` skill. Target artifact: portable, framework-dependent `win-x64` publish.

- Code commit under test: `a3e4272` (version metadata), on `feature/phase-10-task-group-4`
- Test machine: Windows 11 Home Single Language 10.0.26200 (build 26200), Intel Core i7-11800H, 15.7 GB RAM, Intel UHD Graphics, one camera device, .NET SDK 10.0.402
- Evidence artifacts (screenshots, publish output) live under `%TEMP%\pcb-deftek-*`; nothing generated is committed.

## 1. Build & Reproducibility

| Check | Result | Evidence |
|---|---|---|
| Clean clone of the branch | PASS | `git clone` into `%TEMP%\pcb-deftek-clean` |
| Restore from clean clone | PASS | `dotnet restore pcb-deftek.slnx` |
| Release build, warnings as errors | PASS | 0 warnings, 0 errors |
| Full test suite in clean clone | PASS | 793 unit + 46 BDD passed, 0 failed, 0 skipped |
| Portable publish | PASS | `dotnet publish pcb-deftek.csproj -c Release -r win-x64 --self-contained false` |
| Publish from clean clone | PASS | Reproduced 40 files / 217.1 MB from `%TEMP%\pcb-deftek-clean` |
| Publish output | PASS | 40 files, 217.1 MB (largest: `OpenCvSharpExtern.dll` 65.4 MB, ffmpeg 27.3 MB, DirectML 17.7 MB, onnxruntime 16.5 MB, model 50 MB) |
| Version identity | PASS | `FileVersion 0.10.0.0`; `ProductVersion 0.10.0+<sha>` (SDK appends the source revision) |
| Run from publish output | PASS | Launches, renders the dashboard, exits cleanly |

No database migration and no new NuGet package is introduced in this phase.

## 2. Runtime Prerequisites

| Requirement | Result | Evidence |
|---|---|---|
| Windows 10/11 x64 | PASS | Verified on Windows 11 build 26200 |
| .NET 10 Desktop Runtime (framework-dependent build) | PASS on this machine | SDK 10.0.402 present; a runtime-only machine was not available for testing |
| Optional webcam | PARTIAL | One camera device enumerated; the Live Detection page rendered `Camera Idle` / `0 FPS` and streaming was not observed (see Known Gaps) |
| Optional DirectML GPU | NOT VERIFIED | Settings showed `Detection Device = Cpu`; the GPU provider path was not exercised |

## 3. Functional Smoke Matrix (published build, UI Automation + screenshots)

| # | Check | Result | Evidence |
|---|---|---|---|
| 1 | Dashboard renders with four metric cards | PASS | Cards: Defects Detected, Inspection Rate, PCBs Inspected, Defect Distribution |
| 2 | Metric card drag reorders | PASS | `Inspection Rate` dragged before `Defects Detected`; order changed |
| 3 | Dropping a card on itself is a no-op | PASS | Order unchanged after self-drop |
| 4 | Card order persists across restart | PASS | Reordered layout restored after relaunch |
| 5 | Dashboard PDF export | PASS | `PCB-Deftek-Report-20261115-202002.pdf`, `%PDF-` signature, 3,963,078 bytes; created file removed after verification |
| 6 | File & batch detection page | PASS | "File & Batch Inspection", drag/drop area, empty queue state |
| 7 | History gallery | PASS | "Detection History", 93 results, PASS/FAIL badges, date grouping |
| 8 | Settings modal | PASS | Confidence threshold, detection device, startup page, report/image directories |
| 9 | Settings closes with `Escape` | PASS | Modal dismissed |
| 10 | Live Detection page and HUD | PASS | "Live Optical Inspection Feed", PASS badge, 0 defects, latency/FPS cards, capture button |
| 11 | Sidebar collapses on Live Detection | PASS | Navigation labels absent while streaming view is active |
| 12 | Original card order restored after testing | PASS | Settings left as found |

Smoke run: 12/12 PASS, first against the working-tree publish (`%TEMP%\pcb-deftek-smoke-20261115-201943`) and re-walked against the clean-clone publish (`%TEMP%\pcb-deftek-smoke-20261115-202449`, 12/12 PASS). Card order was restored after both runs.

Not exercised through the UI: file/folder picker, batch start-to-finish, capture persistence, viewer pan/zoom/crop, database backup/export, multiline delete. These are covered by the BDD suite against real services (46 scenarios), which passed in the clean clone and in CI.

### 3a. Live-Inference Soak (5 minutes)

Reduced from 10 minutes with owner approval. One minute warm-up, then samples at t=2 and t=5 while the real camera streams continuously.

- Artifact: Release build from `main` @ `a71f14a` (`bin/Release/net10.0-windows10.0.19041.0`). The Phase A changes merged with #131 do not touch the inference path.
- Machine: Windows 11 build 26200, Intel Core i7-11800H, 15.7 GB RAM, DirectML device 0 (RTX 2050, owner-confirmed); USB2.0 HD UVC WebCam at index 0.
- Method: Live Detection page, real camera, 5 minutes uninterrupted. HUD values read through UI Automation at each sample and captured in screenshots; process counters read with `Get-Process`.

| Sample | Elapsed | Working set | Private bytes | Inference latency | HUD FPS |
|---|---|---|---|---|---|
| Warm-up | 1 min | not sampled (per plan) | — | — | — |
| t=2 | 2 min | 510.8 MB | 717.2 MB | 37.1 ms | 26 |
| t=5 | 5 min | 516.0 MB | 715.3 MB | 39.4 ms | 25 |

- Memory: +5.2 MB working set and −1.9 MB private bytes across the sampled window; no growth trend.
- Provider: startup logged `ONNX Runtime initialized successfully with DirectML GPU provider (Device 0)` and every inference logged `... detected on "GpuDirectML"` for the full run.
- Errors: 0 `[ERR]`/`[FTL]` entries in the run window; no dialog, no crash; PASS/FAIL badge and defect counter updated as expected for the scene.
- Evidence: `%TEMP%\soak-task7\t2.png`, `%TEMP%\soak-task7\t5.png`, run log extract `%TEMP%\soak-task7\soak-window.log`.

**Soak result: PASS** for the acceptance criteria above.

**Finding — fixed in #135:** closing the window while Live Detection streamed deadlocked shutdown (`ExitCode: 0` logged, then no service shutdown lines): the UI thread blocked in `LiveInspectionEngine.Dispose` → `StopAsync().GetAwaiter().GetResult()` while the inference worker blocked in `LiveDetectionViewModel.UpdateOnUiThread` → `Dispatcher.Invoke`. #135 replaced the synchronous dispatch with a coalescing `InvokeAsync` path; closing now exits in 0.7 s with both "stopped cleanly" lines logged. The §11 verdict below was written before the fix and is superseded by the Task 8 verdict update.

## 4. Data Safety

| Check | Result | Evidence |
|---|---|---|
| User-owned source images never modified or deleted | PASS | Deletion policy enforced by `InspectionDeletionService`; BDD scenarios assert external originals survive and app-owned captures are removed |
| Database location | PASS | `%LOCALAPPDATA%\PcbDeftek\pcb-deftek\app.db` |
| Export locations | PASS | `Downloads\PCB Deftek\{Report,Backup,Images}`; configurable in Settings |
| No schema change in this phase | PASS | `git diff` shows no migration files |
| Corrupt-database startup | PASS | Published build showed one dialog, logged `Fatal`, exited with code 1, left no window; the backup was restored afterwards |
| Backup/restore guidance | PASS | Manual DB export writes a snapshot to the configured backup directory; restoring it is a file copy over `app.db` while the app is closed |

## 5. Diagnostics & Error Handling

| Check | Result | Evidence |
|---|---|---|
| Structured logs | PASS | Serilog rolling files, 30 retained, `%LOCALAPPDATA%\PcbDeftek\pcb-deftek\logs\app-YYYYMMDD.log` |
| Startup failure visible | PASS | Dialog + `Fatal` log entry + exit code 1 |
| No silent catches on critical paths | PASS | Exporter logs and notifies failures; startup failure aborts; background tasks log unobserved exceptions |
| Error dialogs suppressible for automation | PASS (code) | `App.ShowErrorDialogs`; suppression itself not exercised |

## 6. Security & Dependencies

| Check | Result | Evidence |
|---|---|---|
| Dependency audit | PASS | `dotnet list pcb-deftek.csproj package --vulnerable --include-transitive` → no vulnerable packages |
| Secrets in tracked files | PASS | Pattern scan for password/secret/api key/token assignments found none |
| Absolute developer paths | PASS | Scan for `C:\Users\...` in tracked files found none |
| Network access / telemetry | PASS | Local-only application; no telemetry or remote calls in source |
| Third-party binaries | PASS | Only NuGet packages already reviewed in earlier phases; no new package in this phase |

## 7. Release Notes — 0.10.0

### Added

- Application icon for the executable, title bar, and taskbar (`Assets/app-icon.ico`).
- GitHub Actions CI: Release build with warnings as errors, unit and BDD test runs, `.trx` artifacts.
- README with brand assets, detection-scope statement, screenshots, prerequisites, commands, data locations, attribution, and sample-image links.
- BDD coverage for the viewer's focused→default sidebar transition and for failed report exports.
- Clean startup abort path for database migration failures (dialog, `Fatal` log, exit code 1).
- Build version identity (`0.10.0`).

### Changed

- Startup sequencing extracted into `StartupCoordinator`; migrations are fatal, routing stays best-effort.
- BDD scenarios run serially for deterministic cancellation and timing assertions.

### Fixed

- The application could stay alive with no window after a migration failure.
- The window icon failed to load when `MainWindow` was constructed outside the application assembly (test host).
- A unit test installed a non-pumping `Application.Current` that deadlocked later dispatcher calls.
- Export and batch cancellation tests were timing-sensitive on CI.

### Known Limitations

- Installer/MSIX, code signing, splash branding, and GitHub Release publication remain deferred.
- No separate changelog file; these notes are the release record for 0.10.0.
- Multi-instance execution is allowed and not guarded.
- Performance under continuous camera inference is not measured (deferred).
- No automated dashboard drag-path test; the interaction is verified manually here.

## 8. Known Gaps (accepted for this release)

| Gap | Impact | Disposition |
|---|---|---|
| Live camera streaming not observed on this machine (`Camera Idle`, 0 FPS) | Live inspection unverified end-to-end here | Verify on target hardware before pilot use; BDD uses a fake camera |
| DirectML GPU path not exercised | GPU acceleration unverified | Settings show the CPU default; verify on target hardware |
| Windows toast notifications not asserted | Notification delivery unverified programmatically | Manual observation during export; service logs dispatch failures |
| Performance/memory soak not run | Long-session behaviour unknown | Accepted launch gap per plan; run an ad-hoc soak before wider distribution |
| Drag-and-drop automation deferred | Only dashboard cards are covered manually; other drag targets untested | Manual steps executed for cards; tree/file drag paths covered by unit and BDD tests |

## 9. Rollback Plan

- **Trigger:** the published build fails to start, corrupts data, or blocks core inspection work.
- **Steps:** close the app, replace the portable folder with the previous build output, and restore `app.db` from the most recent backup in `Downloads\PCB Deftek\Backup` if data is suspect. No schema migration runs in this phase, so an older build can open the existing database.
- **Time:** under 5 minutes (file copy plus relaunch).
- **Verification after rollback:** app starts, dashboard renders, history opens, log file shows a clean session.

## 10. Post-Launch Verification (first use)

1. Launch the published build; confirm the dashboard renders and the icon appears.
2. Open History; confirm records and thumbnails load.
3. Run one export; confirm the PDF opens and the toast fires.
4. Run one file-based batch; confirm progress, ETA, and completion toast.
5. Check `%LOCALAPPDATA%\PcbDeftek\pcb-deftek\logs` for `ERR`/`FTL` entries.

## 11. Verdict

**Go — conditional — for an internal pilot.** Build, tests, packaging, icon, export, history, settings, and startup-failure handling are evidenced. Public distribution stays **No-Go** until these conditions are met:

1. Live camera streaming and one capture verified on target hardware.
2. DirectML path verified on a machine with a supported GPU (or explicitly disabled in the UI).
3. Windows toast delivery confirmed on a machine with notifications enabled.
4. A 10-minute live-inference soak recorded (memory, FPS, latency).
5. Installer/MSIX and code signing produced for external distribution.
