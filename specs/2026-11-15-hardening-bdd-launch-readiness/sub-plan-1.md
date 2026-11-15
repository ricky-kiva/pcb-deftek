# Sub-Plan 1: Hardware Verification, Notifications, Installer & Release

Follow-on to `plan.md` after Task Group 4. Scope: prove DirectML, fix Windows toast delivery, produce a signed installer, publish a GitHub Release, and record a 5-minute live soak. Streamlined: 8 tasks, 3 checkpoints.

- Parent spec: `plan.md`, `requirements.md`, `validation.md`, `launch-checklist.md`
- Task list target: **this file** (project convention; no `tasks/` directory)
- Owner approvals already given: launch-checklist condition 1 (live camera) accepted; soak reduced from 10 to 5 minutes.

## Context & Findings (read-only)

| Topic | Finding |
|---|---|
| DirectML | `OnnxModelRunner` appends the DML provider when `PreferredDevice == GpuDirectML` and silently falls back to CPU on failure (`OnnxModelRunner.cs:55-78`). `ActiveDevice` is public. No test asserts the GPU path; existing tests pass either way. |
| Toast failure | `WindowsNotificationService.DefaultToastDispatcher` calls WinRT `ToastNotificationManager.CreateToastNotifier()` through reflection with no AppUserModelID (`WindowsNotificationService.cs:83`). Unpackaged processes — including `dotnet run` and VS debugging — have no registered AUMID, so the toast never shows; the exception is caught and logged as a warning. **Not intended.** |
| Toast fix available | The referenced package already ships `ToastNotificationManagerCompat.CreateToastNotifier()` and `ToastContentBuilder.Show()`, which register an AUMID and Start-menu shortcut for unpackaged apps. No new NuGet package required. |
| Packaging | No installer, no publish profile, no signing, no release workflow. |
| Soak | No retained measurement; reduced to 5 minutes, manual. |

## Dependency Graph

```
DirectML test ─┐
Toast fix ─────┴─→ self-contained publish → installer → signing → GitHub Release
                                                      └─→ 5-minute soak → docs update
```

## Tasks

### Phase A: Hardware & Notifications

#### Task 1: DirectML Verification Test

- **Description:** Add a hardware-tagged test that loads `best.onnx` with `PreferredDevice = GpuDirectML`, runs one inference, and asserts the active provider. Exclude it from CI so hosted runners are unaffected.
- **Acceptance criteria:**
  - [x] `tests/pcb-deftek.Tests/DirectMlInferenceTests.cs` marked `[Trait("Category", "Hardware")]`; asserts `ActiveDevice == ExecutionDevice.GpuDirectML` and that one inference returns a result.
  - [x] Failure message reports the actual device and that DirectML initialization fell back, so "not working" is diagnosable.
  - [x] CI runs unit tests with `--filter "FullyQualifiedName!~DirectMlInferenceTests"`; the hardware test never gates the hosted build.
  - [x] No production code change; no new package.
- **Verification:**
  - [x] Locally on the RTX 2050: `dotnet test tests/pcb-deftek.Tests/ --filter FullyQualifiedName~DirectMlInferenceTests` → pass (`ActiveDevice == GpuDirectML`, output `[1, 13, 8400]`).
  - [x] CI run `43722334202`: the unit step used `FullyQualifiedName!~DirectMlInferenceTests` and passed 795 tests; BDD passed 46.
- **Deviations:**
  - CI excludes by fully-qualified name instead of `Category!=Hardware`; the trait is still present, but the FQN filter does not depend on VSTest trait mapping.
- **Dependencies:** None
- **Files:**
  - `tests/pcb-deftek.Tests/DirectMlInferenceTests.cs`
  - `.github/workflows/ci.yml`
- **Estimated scope:** S (2 files)

#### Task 2: Fix Unpackaged Toast Delivery

- **Description:** Replace the AUMID-less WinRT reflection path with the package's compat notifier so toasts work for unpackaged runs (VS debug, `dotnet run`, portable publish).
- **Acceptance criteria:**
  - [x] `WindowsNotificationService` dispatches via `ToastContentBuilder.Show()`; no raw `Type.GetType`/`CreateToastNotifier` reflection remains.
  - [x] Failures remain non-throwing and logged (existing contract), and the operation still succeeds when the shell is unavailable.
  - [x] Existing notification tests pass; added two asserting the default constructor wires the compat dispatcher and an injected delegate does not.
  - [x] AUMID/shortcut registration happens on first dispatch; documented in the code comment.
- **Verification:**
  - [x] `dotnet test tests/pcb-deftek.Tests/ --filter FullyQualifiedName~WindowsNotificationServiceTests` → 11 passed.
  - [x] Manual: an unpackaged probe (same hosting as VS debug / `dotnet run`) dispatched through the default service; Windows recorded the notification (`LastNotificationAddedTime`, `PeriodicNotificationCount`) and the entries appear in Notification Center. Banners are suppressed by this machine's Focus Assist/DND, which is an OS setting, not app behaviour.
- **Deviations:**
  - The app and both test projects moved to `net10.0-windows10.0.19041.0`. The package's compat API lives in its Windows-specific asset; the generic `net5.0` asset chosen for `net10.0-windows` has no `ToastContentBuilder.Show()` or `ToastNotificationManagerCompat`. Baseline is now Windows 10 2004+.
- **Dependencies:** None
- **Files:**
  - `Infrastructure/Notifications/WindowsNotificationService.cs`
  - `tests/pcb-deftek.Tests/WindowsNotificationServiceTests.cs`
  - `pcb-deftek.csproj`, `tests/pcb-deftek.Tests/pcb-deftek.Tests.csproj`, `tests/pcb-deftek.Bdd/pcb-deftek.Bdd.csproj` (target framework)
- **Estimated scope:** S (2 files + target framework)

---

### Checkpoint A: Hardware Proven, Toasts Delivered
- [x] DirectML test passes on the RTX 2050 (`ActiveDevice == GpuDirectML`) and is excluded from CI.
- [x] Toast dispatched from an unpackaged run and present in Notification Center (banner suppressed by Focus Assist on this machine; published-build confirmation folds into the Task 8 re-walk).
- [x] Full suite green (796 unit, 46 BDD); build 0 warnings.
- [ ] Human review before Phase B.

---

### Phase B: Installer, Signing & Release

#### Task 3: Self-Contained Publish & Portable Zip

- **Description:** Add a reproducible packaging script for the distributable artifacts: a self-contained `win-x64` publish (bundles the .NET runtime) and a portable zip.
- **Acceptance criteria:**
  - [ ] `build/package.ps1` publishes `-c Release -r win-x64 --self-contained true` into `artifacts/pcb-deftek-<version>-win-x64/`.
  - [ ] Produces `artifacts/PCB-Deftek-<version>-portable-win-x64.zip` and a `SHA256SUMS.txt`.
  - [ ] `artifacts/` is git-ignored; script fails on any build error.
  - [ ] Published app launches and the dashboard renders.
- **Verification:**
  - [ ] Run the script; inspect file count/size; launch the output.
- **Dependencies:** Tasks 1, 2
- **Files:**
  - `build/package.ps1`
  - `.gitignore`
- **Estimated scope:** S (2 files)

#### Task 4: Inno Setup Installer

- **Description:** Build a per-user installer around the self-contained publish: Start-menu and optional desktop shortcuts, uninstall entry, and app icon.
- **Acceptance criteria:**
  - [ ] `installer/pcb-deftek.iss` installs to `%LOCALAPPDATA%\Programs\PCB Deftek`, creates shortcuts with `Assets/app-icon.ico`, and registers an uninstaller.
  - [ ] Installer output `artifacts/PCB-Deftek-<version>-setup.exe`; app launches after install; uninstall removes files and shortcuts.
  - [ ] User data (`%LOCALAPPDATA%\PcbDeftek`, Downloads exports) is untouched by install/uninstall.
  - [ ] No admin rights required.
- **Verification:**
  - [ ] Install silently (`/VERYSILENT /SUPPRESSMSGBOXES`), launch, inspect shortcuts, uninstall, confirm cleanup.
- **Dependencies:** Task 3
- **Files:**
  - `installer/pcb-deftek.iss`
  - `build/package.ps1`
- **Estimated scope:** S (2 files)

#### Task 5: Code Signing

- **Description:** Sign the executable and installer with the project certificate and verify the signature, wiring the same step into CI later.
- **Acceptance criteria:**
  - [ ] `build/sign.ps1` signs `pcb-deftek.exe` and the setup executable with SHA-256 and an RFC 3161 timestamp; certificate comes from `SIGNING_CERT_PFX_BASE64`/`SIGNING_CERT_PASSWORD` or a local PFX path.
  - [ ] `signtool verify /pa /all` reports success for both files.
  - [ ] Failure to find a certificate fails the script with a clear message; unsigned artifacts are never silently published.
  - [ ] No certificate or password is committed.
- **Verification:**
  - [ ] Locally: `signtool verify /pa` on both outputs; Windows file properties show the publisher.
- **Dependencies:** Task 4 · **Requires:** code-signing certificate + timestamp URL (owner-supplied)
- **Files:**
  - `build/sign.ps1`
  - `.gitignore`
- **Estimated scope:** S (2 files)

#### Task 6: GitHub Release Workflow

- **Description:** Tag-driven release: build, package, sign, and publish a GitHub Release with installer, portable zip, checksums, and release notes.
- **Acceptance criteria:**
  - [ ] `.github/workflows/release.yml` triggers on `v*` tags; builds self-contained, runs `package.ps1`, signs, and fails if signing secrets are missing.
  - [ ] Creates the release with `PCB-Deftek-<version>-setup.exe`, portable zip, `SHA256SUMS.txt`, and a release-notes body.
  - [ ] Release notes reuse the 0.10.0 notes from `launch-checklist.md` plus install instructions.
  - [ ] Tag `v0.10.0` produces a release whose assets download and verify against the checksums.
- **Verification:**
  - [ ] Push a test tag on the branch (or run with `workflow_dispatch`), download assets, compare hashes, run the installer from the release.
- **Dependencies:** Tasks 3, 4, 5
- **Files:**
  - `.github/workflows/release.yml`
  - `README.md` (install section)
- **Estimated scope:** M (2 files)

---

### Checkpoint B: Distributable & Signed
- [ ] Installer installs/uninstalls cleanly and is signed.
- [ ] Release assets exist, are signed, and match checksums.
- [ ] Human review before Phase C.

---

### Phase C: Soak & Documentation

#### Task 7: 5-Minute Live-Inference Soak

- **Description:** Measure the reduced soak on the RTX 2050: 1 minute warm-up, samples at t=2 and t=5, recorded in the checklist.
- **Acceptance criteria:**
  - [ ] Live Detection runs 5 minutes with DirectML active (telemetry shows the GPU device).
  - [ ] Working set and private bytes at t=2 and t=5; HUD FPS and average latency at both points.
  - [ ] No crash, no error dialog, no `ERR`/`FTL` log entries.
  - [ ] Numbers and machine context recorded; any failure is a release condition.
- **Verification:**
  - [ ] Recorded table in `launch-checklist.md` §3a with the observed values.
- **Dependencies:** Tasks 1, 3
- **Files:**
  - `specs/2026-11-15-hardening-bdd-launch-readiness/launch-checklist.md`
- **Estimated scope:** XS (1 file + manual run)

#### Task 8: Update Launch Documents

- **Description:** Fold the new evidence into the launch record and revise the verdict.
- **Acceptance criteria:**
  - [ ] `launch-checklist.md`: DirectML PASS evidence, toast PASS, installer/signing PASS, soak numbers, install instructions, revised known gaps.
  - [ ] `validation.md` §5.4 live-camera item updated to owner-verified; new gates for DirectML, notifications, installer, signature, release.
  - [ ] Verdict updated: **Go** for external distribution once signature verification and release-asset checks pass; remaining gaps listed (multi-instance, splash branding).
  - [ ] `plan.md` links this sub-plan.
- **Verification:**
  - [ ] Re-walk the checklist against the released installer; same verdict.
- **Dependencies:** Tasks 1–7
- **Files:**
  - `specs/2026-11-15-hardening-bdd-launch-readiness/launch-checklist.md`
  - `specs/2026-11-15-hardening-bdd-launch-readiness/validation.md`
  - `specs/2026-11-15-hardening-bdd-launch-readiness/plan.md`
- **Estimated scope:** S (3 files)

---

### Checkpoint C: Release Ready
- [ ] All gates in `validation.md` pass, including the new hardware/notification/packaging rows.
- [ ] Signed installer installs on a clean profile and the app starts.
- [ ] Human approval recorded before tagging `v0.10.0`.

---

## Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| DirectML unavailable on the RTX 2050 (driver/D3D12 issue) | High | Test fails with the active device named; fall back documented, GPU option hidden or labelled |
| Toast still blocked by Focus Assist or notification settings | Medium | Manual check from both VS debug and installed build; service logs every failure |
| No certificate available | High | Task 5 blocks until the PFX/Azure credential is supplied; unsigned publishing is refused |
| Inno Setup not installed on the dev machine | Low | Install via winget/choco; documented in the script header |
| Self-contained publish grows the installer (~250–350 MB) | Medium | Accepted for offline installs; portable zip remains available |
| GitHub secrets misconfigured | Medium | Release workflow fails before creating the release; verification step re-runs locally |
| Soak numbers regress on GPU | Medium | 5-minute gate is short; record values and treat failure as a release condition |

## Prerequisites Requiring Owner Input

1. **Code-signing certificate** — PFX + password (or Azure Trusted Signing/SignPath credentials) and the timestamp URL. Nothing can be signed without it.
2. **Inno Setup availability** — `winget install JRSoftware.InnoSetup` on this machine if absent.
3. **Repository secrets** — `SIGNING_CERT_PFX_BASE64`, `SIGNING_CERT_PASSWORD` before the release workflow runs.
4. **Tag approval** — `v0.10.0` is pushed only after Checkpoint C.

## Open Questions

None beyond the prerequisites above; the toast root cause is confirmed in code and the fix path uses the already-referenced package.
