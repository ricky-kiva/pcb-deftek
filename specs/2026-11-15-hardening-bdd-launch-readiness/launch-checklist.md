# Launch Readiness Checklist: PCB Deftek 0.10.0

Prepared 2026-11-15, updated 2026-11-16 after Phase A–C validation with the `shipping-and-launch` skill. Target artifacts: signed per-user installer and portable, self-contained `win-x64` zip (version 0.10.0).

- Code under test: `main` @ `36f6541`, tagged `v0.10.0`; includes the shutdown fix from #135 and the soak evidence recorded in §3a
- Test machine: Windows 11 Home Single Language 10.0.26200 (build 26200), Intel Core i7-11800H, 15.7 GB RAM, NVIDIA RTX 2050 (DirectML device 0), USB2.0 HD UVC WebCam, .NET SDK 10.0.402
- Evidence artifacts live under `%TEMP%\pcb-deftek-*`, `%TEMP%\soak-task7*`, `%TEMP%\phaseb-task34*`; nothing generated is committed.

## 1. Build & Reproducibility

| Check | Result | Evidence |
|---|---|---|
| Clean clone of the branch | PASS | `git clone` into `%TEMP%\pcb-deftek-clean` |
| Restore from clean clone | PASS | `dotnet restore pcb-deftek.slnx` |
| Release build, warnings as errors | PASS | 0 warnings, 0 errors |
| Full test suite in clean clone | PASS | 798 unit + 46 BDD passed, 0 failed, 0 skipped (hardware-only DirectML test excluded on CI runners) |
| Packaging script | PASS | `build/package.ps1` publishes, zips, compiles the installer, signs when configured, and writes `SHA256SUMS.txt` (~170 s) |
| Publish output | PASS | `artifacts/pcb-deftek-0.10.0-win-x64`: 436 files, 380.5 MB self-contained |
| Version identity | PASS | `FileVersion 0.10.0.0`; `ProductVersion 0.10.0+<sha>` |
| Run from publish output | PASS | Launches, renders the dashboard, exits cleanly (0.4 s) |
| Installer | PASS | `PCB-Deftek-0.10.0-setup.exe`, 144.9 MB, per-user, no elevation |
| Code signing | PASS (CI) | `signtool verify /pa /all` passes on the ephemeral runner after the self-signed public certificate is trusted; both executables carry an RFC 3161 timestamp |
| Checksums | PASS | `SHA256SUMS.txt` is produced after signing; recomputation matches both assets |
| Release publication | PASS | [`v0.10.0`](https://github.com/ricky-kiva/pcb-deftek/releases/tag/v0.10.0) from run `43750845817`: installer, portable zip and checksums; downloaded assets re-hashed and the installer install/launch/uninstall cycle passed |

No database migration and no new NuGet package is introduced in this phase.

## 2. Runtime Prerequisites

| Requirement | Result | Evidence |
|---|---|---|
| Windows 10 version 2004+ / 11 x64 | PASS | Verified on Windows 11 build 26200; the target framework now requires 10.0.19041 or later |
| .NET runtime | PASS | Self-contained publish bundles the .NET runtime; no separate install required |
| Optional webcam | PASS | USB2.0 HD UVC WebCam streamed for the 5-minute soak at 25–28 FPS |
| Optional DirectML GPU | PASS | `DirectMlInferenceTests` reports `GpuDirectML`; every soak inference logged `detected on "GpuDirectML"` (RTX 2050) |

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

**Finding — fixed in #135:** closing the window while Live Detection streamed deadlocked shutdown (`ExitCode: 0` logged, then no service shutdown lines): the UI thread blocked in `LiveInspectionEngine.Dispose` → `StopAsync().GetAwaiter().GetResult()` while the inference worker blocked in `LiveDetectionViewModel.UpdateOnUiThread` → `Dispatcher.Invoke`. #135 replaced the synchronous dispatch with a coalescing `InvokeAsync` path; closing now exits in 0.7 s with both "stopped cleanly" lines logged.

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
| Clean shutdown while streaming | PASS | Close during Live Detection exits in 0.7 s with `ExitCode 0` and both "stopped cleanly" logs (#135) |

## 6. Security & Dependencies

| Check | Result | Evidence |
|---|---|---|
| Dependency audit | PASS | `dotnet list pcb-deftek.csproj package --vulnerable --include-transitive` → no vulnerable packages |
| Secrets in tracked files | PASS | Pattern scan for password/secret/api key/token assignments found none |
| Absolute developer paths | PASS | Scan for `C:\Users\...` in tracked files found none |
| Network access / telemetry | PASS | Local-only application; no telemetry or remote calls in source |
| Third-party binaries | PASS | Only NuGet packages already reviewed in earlier phases; no new package in this phase |
| Signing secrets in CI | PASS | Repository secrets only; the decoded PFX lives in `$RUNNER_TEMP` with restricted ACLs and is deleted in `finally`; no key material appears in logs or artifacts |

## 7. Release Notes — 0.10.0

### Added

- Application icon for the executable, title bar, and taskbar (`Assets/app-icon.ico`).
- GitHub Actions CI: Release build with warnings as errors, unit and BDD test runs, `.trx` artifacts.
- README with brand assets, detection-scope statement, screenshots, prerequisites, commands, data locations, attribution, sample-image links, and an installation section.
- BDD coverage for the viewer's focused→default sidebar transition and for failed report exports.
- Clean startup abort path for database migration failures (dialog, `Fatal` log, exit code 1).
- Build version identity (`0.10.0`).
- Hardware-tagged DirectML verification test (`GpuDirectML` on the RTX 2050).
- Reproducible packaging script producing a self-contained publish, portable zip, and per-user Inno Setup installer.
- Tag-driven release workflow that validates the tag against the project version, runs all tests, signs both executables, verifies signatures, uploads checksums, and publishes a GitHub Release.
- Recorded 5-minute live-inference soak (memory, FPS, latency, DirectML provider, zero errors).

### Changed

- Startup sequencing extracted into `StartupCoordinator`; migrations are fatal, routing stays best-effort.
- BDD scenarios run serially for deterministic cancellation and timing assertions.
- Target framework raised to `net10.0-windows10.0.19041.0`, so the minimum OS is Windows 10 version 2004.
- Background-to-UI updates now dispatch asynchronously and coalesce live frames (latest frame wins).

### Fixed

- The application could stay alive with no window after a migration failure.
- The window icon failed to load when `MainWindow` was constructed outside the application assembly (test host).
- A unit test installed a non-pumping `Application.Current` that deadlocked later dispatcher calls.
- Export and batch cancellation tests were timing-sensitive on CI.
- Windows toast notifications never appeared in unpackaged runs (VS debugging, `dotnet run`, portable builds) because the notifier had no AppUserModelID.
- Closing the app while Live Detection streamed deadlocked shutdown and left the process running.

### Known Limitations

- The release certificate is self-signed; Windows reports the publisher as untrusted until a publicly trusted code-signing certificate is used.
- Splash branding is deferred.
- No separate changelog file; these notes are the release record for 0.10.0.
- Multi-instance execution is allowed and not guarded.
- No automated dashboard drag-path test; the interaction is verified manually here.

## 8. Known Gaps (accepted for this release)

| Gap | Impact | Disposition |
|---|---|---|
| Self-signed publisher certificate | External users see an untrusted publisher; SmartScreen warnings | Replace the PFX secret with a publicly trusted code-signing certificate before public distribution; no workflow change needed |
| Multi-instance execution | Two instances can share the database | Unchanged from 0.10.0; guard or document for the pilot |
| Splash branding | No startup branding | Deferred to a future release |
| Drag-and-drop automation | Only dashboard cards are covered manually | Manual steps remain; tree/file drag paths covered by unit and BDD tests |

## 9. Rollback Plan

- **Trigger:** the published build fails to start, corrupts data, or blocks core inspection work.
- **Steps:** close the app, uninstall the current build, install the previous `PCB-Deftek-<version>-setup.exe` (or restore the previous portable folder), and restore `app.db` from the most recent backup in `Downloads\PCB Deftek\Backup` if data is suspect. No schema migration runs in this phase, so an older build can open the existing database.
- **Time:** under 5 minutes (uninstall/install or file copy plus relaunch).
- **Verification after rollback:** app starts, dashboard renders, history opens, log file shows a clean session.

## 10. Post-Launch Verification (first use)

1. Verify the installer's Authenticode signature and compare its SHA-256 with `SHA256SUMS.txt`.
2. Launch the app; confirm the dashboard renders and the icon appears.
3. Open History; confirm records and thumbnails load.
4. Run one export; confirm the PDF opens and the toast fires.
5. Run one file-based batch; confirm progress, ETA, and completion toast.
6. Check `%LOCALAPPDATA%\PcbDeftek\pcb-deftek\logs` for `ERR`/`FTL` entries.

## 11. Verdict

**Go — conditional — for an internal pilot.** Build, tests, packaging, installer, signing pipeline, DirectML, live camera streaming, toast delivery, and a 5-minute soak are evidenced. The release workflow was validated end-to-end with `v0.10.0-rc.1`; the RC installer installed, launched, and uninstalled cleanly, and its assets matched `SHA256SUMS.txt`.

**No-Go for public distribution** until a publicly trusted code-signing certificate replaces the self-signed certificate; end users currently see an untrusted publisher.

The final [`v0.10.0`](https://github.com/ricky-kiva/pcb-deftek/releases/tag/v0.10.0) assets are published and verified: downloaded SHA-256 values match `SHA256SUMS.txt`, both executables are signed with an RFC 3161 timestamp, and the installer installs, launches, and uninstalls cleanly.

Accepted gaps for the pilot: multi-instance execution, splash branding, and manual-only drag-and-drop coverage beyond dashboard cards.
