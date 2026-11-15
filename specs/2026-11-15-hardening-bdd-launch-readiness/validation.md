# Validation Criteria: Phase 10 — Hardening, BDD Verification & Launch Readiness

## 1. Build & Quality Gates

| Gate | Command | Pass condition |
|---|---|---|
| Restore | `dotnet restore pcb-deftek.slnx` | Completes; no new package |
| Build | `dotnet build pcb-deftek.slnx -c Release --no-restore -warnaserror` | 0 errors, **0 warnings** |
| Nullable | Build output | No nullable warnings; no suppression added |
| Project file | `git diff pcb-deftek.csproj` | Version metadata and `ApplicationIcon`/icon `Resource` only |
| Icon | Release build output | `ApplicationIcon` embedded; `Window.Icon` bound in XAML; asset tracked (`git ls-files Assets/app-icon.ico`) |
| Schema | `git diff Infrastructure/Persistence/Migrations` | No migration added |
| Hygiene | `git diff` | No absolute user paths, no secrets, no build output |

## 2. Automated Test Gates

Baseline entering Phase 10: **834 passing** (790 unit + 44 BDD), 0 failed, 0 skipped. Phase 10 keeps that set green and adds only test code.

### 2.1 Required Unit Coverage

| Area | Required assertions |
|---|---|
| Startup coordinator | Failing migrator → failure result with the exception, router not invoked; failing router → success, migrator invoked once; success → both invoked in order |
| Startup wiring | `DependencyInjectionTests` and existing bootstrap tests remain green; no real `%LOCALAPPDATA%` database touched |

### 2.2 Required BDD Coverage

| Scenario | Assertion |
|---|---|
| Clearing viewer focus restores the default sidebar | Focused state → `SelectMarker(null)` → record name, timestamp, class summaries visible; `IsDefectFocused` false; `Delete Image` still available |
| Failed export notifies and writes nothing | Data export reaches `Failed`; toast title `Report export failed`; no `.pdf`/`.tmp`/staging left; no success toast |

Both run against isolated SQLite and temp artifact roots; existing scenarios are unchanged.

### 2.3 Full-Suite Gate

- `dotnet test pcb-deftek.slnx` → 100 % pass, 0 skipped, no regression against the 834-test baseline.
- Expected additions: 2 BDD scenarios plus the startup coordinator tests.

## 3. BDD Coverage Matrix (Roadmap Phase 10)

| Workflow | Scenario | Entering Phase 10 | Verdict |
|---|---|---|---|
| Batch ETA | `FileBatchDetection` — ETA computed, 100 % progress, persisted | Covered | Verify, no change |
| Camera auto-hide | `LiveCameraInspection` — controls hide, status/telemetry preserved, mouse restores | Covered | Verify, no change |
| Viewer sidebar transitions | `HistoryAndDefectViewer` — default, focused | Partial | Add focused → default |
| Dashboard drag-and-drop | `DefectStatisticsDashboard` — reorder + persistence via view model | Partial | Manual smoke step (deferred automation) |
| Export notifications | `AuditPdfExport` — success, no-match, decline, cancel | Partial | Add failure notification |

Performance under continuous inference is **not** covered by this phase; the checklist must state it as unverified.

## 4. CI Verification Gates

| Check | Evidence |
|---|---|
| Workflow valid and triggered | First run starts on push and PR; manual dispatch available |
| Warning gate | Build step uses `-warnaserror`; log shows 0 warnings |
| Tests split by project | Two test steps; both `.trx` artifacts present |
| Artifacts on failure | A deliberately failing local run still uploads results |
| Duration | Job ≤ 20 minutes, recorded |
| Permissions | `contents: read`; no secrets referenced |
| Concurrency | Second push to the same ref cancels the first run |

## 5. Functional Acceptance Criteria

### 5.1 CI & Documentation

- [x] Workflow triggers on push, PR, and manual dispatch; superseded runs cancel.
- [x] Release build fails on any warning.
- [x] Unit and BDD results upload on success and failure.
- [x] README prerequisites, commands, test-isolation rule, and data/log locations are correct and executed.

### 5.2 BDD Closure

- [x] Clearing viewer focus restores the default sidebar and keeps delete available.
- [x] Export to an unwritable destination reports `Failed`, shows the failure toast, and leaves no PDF/temp/staging.
- [x] Existing scenarios still pass without edits.

### 5.3 Startup Hardening

- [x] Migration failure: fatal log, one dialog, exit code 1, no window, no lingering process.
- [x] Routing failure: warning log, shell opens on the default view (unit test asserts startup continues; the warning branch is unchanged code).
- [x] Success path unchanged.
- [ ] Dialog suppressed when `App.ShowErrorDialogs` is false — guarded in code but not exercised by tests or the manual run.

### 5.4 Release Identity & Launch Checklist

- [x] Executable reports version `0.10.0` via `FileVersionInfo`.
- [x] Checklist covers all eight sections in `requirements.md` §6, each item with evidence.
- [x] Release notes for `0.10.0` included in the checklist with Known Limitations.
- [x] Self-contained publish and per-user installer executed, launched, and smoke-tested, including the manual drag steps and a live-camera pass — streaming observed for 5 minutes; installer installs and uninstalls without elevation.
- [x] DirectML GPU path verified on the RTX 2050 (hardware test reports `GpuDirectML`; every soak inference logged `GpuDirectML`).
- [x] Windows toast delivery verified for unpackaged runs (recorded by Windows and visible in Notification Center).
- [x] Executables signed with SHA-256 and an RFC 3161 timestamp and verified in CI; checksums match the signed assets.
- [x] Tag-driven release workflow validated with `v0.10.0-rc.1`: assets downloaded, hashes matched, installer installed, release and tag removed afterwards.
- [x] `dotnet list package --vulnerable` shows no known vulnerable dependency, or the finding is documented with a decision.
- [x] Performance under continuous inference measured for 5 minutes (memory, FPS, latency) with no errors or error dialogs.
- [x] Checklist re-walked against the clean-clone publish and the RC release installer with the same verdict (12/12 smoke checks; install, launch, and uninstall confirmed).
- [x] One overall Go/No-Go recorded.

### 5.5 Brand Identity & README

- [x] Executable icon in Explorer, title bar, and taskbar matches `Assets/app-icon.ico` (Explorer extraction and title bar observed; the taskbar renders the same `Window.Icon`).
- [x] `ApplicationIcon` and the icon `Resource` are set; the window `Icon` is bound; build remains at 0 warnings.
- [x] `Assets/app-icon.ico` and all five `docs/assets/` files are tracked in git.
- [x] README shows the logo at the top and embeds exactly the four staged page screenshots, in page order, with no broken links.
- [x] README states the detection scope: PCB trace defects (open/short circuits), best suited to bare PCBs without mounted components.
- [x] README documents the screenshot naming convention and current values (`1`/`1`).

## 6. CI Results (recorded)

| Item | Value |
|---|---|
| Final main CI | https://github.com/ricky-kiva/pcb-deftek/actions/runs/43753964148 |
| Job duration | 1m56s |
| Unit tests / result | 798 passed, 0 failed, 0 skipped (hardware DirectML test excluded) |
| BDD tests / result | 46 passed, 0 failed, 0 skipped |
| Warning count | 0 |
| Artifact | `test-results` (unit + BDD `.trx`) |
| Release run | `43750845817` → `v0.10.0` published; assets re-hashed, signed, install/uninstall verified |
| Review-fix validation | `43755765713` (`v0.10.0-rc.2`): pre-release artifacts named `PCB-Deftek-0.10.0-rc.2-*`, both signed and verified; release and tag deleted afterwards |
| Earlier runs | Unit: `43704968179` hung on a test-installed `Application.Current`; `43706692821` named the blocked tests via `--blame-hang`; fixed in `ebc5c08`. `43709113215` failed a 2 s frame wait; widened to 15 s in `5d21631`. BDD: `43707982620`/`43708452705` failed cancellation timing; fixed in `6888323` (serial scenarios, 60 s block window). |

## 7. Merge Readiness & Definition of Done

Phase 10 may merge into `main` only when:

1. All five task groups in `plan.md` are complete, and the follow-on `sub-plan-1.md` tasks are complete.
2. `dotnet build pcb-deftek.slnx -c Release -warnaserror` succeeds with 0 warnings and 0 errors.
3. `dotnet test pcb-deftek.slnx` passes 100 %, 0 skipped, with no regression; current baseline is 800 unit (799 in CI, hardware test excluded) + 46 BDD.
4. CI is green on the final commit.
5. The launch checklist has a recorded Go/No-Go with evidence, and `v0.10.0` is published.
6. No new package, migration, or settings key; production changes beyond the startup failure path are limited to the sub-plan scope (toast dispatch, UI dispatch coalescing, camera-index fix).
7. Deferred items and known gaps are listed in the checklist (self-signed publisher, multi-instance, splash branding, drag automation).
8. `AGENTS.md` §7 review pipeline completed with approval at each stage.
9. Clean history per `AGENTS.md` §4; PR linked to its issue; human approval; branch deleted after merge.

## 8. Results (recorded)

### 8.1 Automated verification

| Command | Result |
|---|---|
| `dotnet build pcb-deftek.slnx -c Release -warnaserror` | 0 warnings, 0 errors |
| `dotnet test pcb-deftek.slnx -c Release` | 800 unit + 46 BDD passed, 0 failed, 0 skipped |
| CI on `main` (`43753964148`) | green: 798 unit (hardware test excluded) + 46 BDD |
| Release workflow (`43750845817`) | green: `v0.10.0` published with signed installer, portable zip, checksums |
| Review-fix release validation (`43755765713`) | green: `v0.10.0-rc.2` assets correctly suffixed, signed and verified; release/tag removed |

### 8.2 Launch verdict

| Item | Result |
|---|---|
| Publish command / output size | `build/package.ps1`: 436 files / 380.5 MB self-contained; zip 181.4 MB; installer 144.9 MB |
| Smoke matrix pass count | 12/12 (clean-clone publish and RC release installer) |
| Vulnerable dependency scan | none (`dotnet list package --vulnerable --include-transitive`) |
| Performance verification | 5-minute soak: t=2 510.8 MB WS / 37.1 ms / 26 FPS; t=5 516.0 MB / 39.4 ms / 25 FPS; 0 `ERR`/`FTL` |
| Go/No-Go | **Go — internal pilot**; public distribution **No-Go** until a publicly trusted certificate replaces the self-signed one |

## 9. Review Pipeline Outcomes

Independent review of `9752b0b..f6ea25f` (scoped Phase 10 paths) by a separate reviewer agent; verdict was **request changes**.

| Stage | Executed | Verdict |
|---|---|---|
| `code-review-and-quality` | 2026-11-16 | 2 Required findings, 1 Optional implemented; follow-ups recorded below. Required fixes verified (`43755765713`) |
| `code-simplification` | pending (approval required) | _pending_ |
| `performance-optimization` | pending (approval required) | _pending_ |
| `security-and-hardening` | pending (approval required) | _pending_ |

### Findings resolved

| Finding | Severity | Resolution |
|---|---|---|
| Camera startup read `SelectedCamera` after an async dispatcher post, so it captured index 0 instead of the enumerated first device | Required | Read `cameras[0].Index` directly; regression test blocks the UI thread and asserts index 1 is used |
| Tag validation compared only the numeric core, so `v0.10.0-rc.1` produced final-version-named assets | Required | Full tag version validated, core compared to the project version, full version passed to `package.ps1`; `v0.10.0-rc.2` confirmed `-rc.2` asset names |
| Signing secrets were exported to every job step | Optional | Moved to step-level env on the three steps that need them |

### Follow-ups (non-blocking, recorded)

| Finding | Severity | Disposition |
|---|---|---|
| PFX password appears in the `signtool` command line | Optional | Deferred; mitigate by importing the cert and signing with `/sha1` on a self-hosted runner |
| Packaging/signing scripts are only exercised on tags | Optional | Deferred; consider a CI job running `package.ps1 -SkipInstaller` |
| Notification tests assert wiring and call the real compat notifier | Nit | Deferred; inject the show-side seam |
| Release publishing is not re-runnable (artifact name/release collision) | Nit | Deferred; branch on existing release and use `run_attempt` in the artifact name |
| StartupCoordinator resolves collaborators from the provider; `GetRequiredService` for the migrator sits outside its try | FYI | Deferred; take interfaces via constructor |
| Two UI-marshalling mechanisms plus an internal test seam on the view model | FYI | Deferred; a small dispatcher abstraction would unify them |
| Pinned `AssemblyVersion`/`FileVersion` overrides `package.ps1 -Version` file properties | FYI | Deferred; derive them from `Version` |

## 10. Failure Triage

| Symptom | First check |
|---|---|
| CI job never starts | Workflow YAML syntax; `on:` block; branch filters |
| Build fails only in CI | `-warnaserror` on analyzer/generated code; compare local Release build |
| BDD fails only in CI | Desktop/webcam assumption, culture, temp-path permissions |
| Viewer reset scenario fails | `SelectMarker(null)` no longer clears `IsDefectFocused` or the default projection |
| Export failure scenario writes a file | Destination validated after creation, or cleanup skipped |
| Startup dialog appears twice | Dispatcher handler and coordinator both reporting |
| Startup exits 0 on migration failure | `Shutdown(1)` not reached or exception swallowed |
| Version missing from binary | Wrong PropertyGroup or stale output |
| Executable still shows the default icon | `ApplicationIcon` path wrong, asset not tracked, or stale output; rebuild and check `FileVersionInfo`/icon extraction |
| README images broken | Asset untracked, filename mismatch/case, or wrong relative path; compare `git ls-files docs/assets` with the Markdown links |
| Checklist item has no evidence | Re-run the check; an unevidenced item is a No-Go |
| Checklist implies performance was not verified | Re-run the 5-minute soak and record t=2/t=5 working set, latency and FPS |
