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
- [x] Human review before Phase B (approved; Phase B proceeded with Tasks 3-4 in #139).

---

### Phase B: Installer, Signing & Release

#### Task 3: Self-Contained Publish & Portable Zip

- **Description:** Add a reproducible packaging script for the distributable artifacts: a self-contained `win-x64` publish (bundles the .NET runtime) and a portable zip.
- **Acceptance criteria:**
  - [x] `build/package.ps1` publishes `-c Release -r win-x64 --self-contained true` into `artifacts/pcb-deftek-<version>-win-x64/` (436 files, 380.5 MB).
  - [x] Produces `artifacts/PCB-Deftek-<version>-portable-win-x64.zip` (181.4 MB) and `SHA256SUMS.txt`; recomputed hashes match.
  - [x] `artifacts/` is git-ignored (existing `.gitignore` rule); the script exits 1 on a publish failure (verified with an invalid RID: `NETSDK1083`).
  - [x] Published app launches and the dashboard renders (screenshot, four metric cards, exit code 0).
- **Verification:**
  - [x] `powershell -File build/package.ps1` → publish + zip + installer + checksums in one run, ~170 s.
  - [x] Version defaults to the csproj `<Version>`; input is validated against a SemVer allowlist before it reaches any path or compiler argument (`-Version 1.2` → exit 1).
  - [x] SHA-256 recomputation via PowerShell matches both entries in `SHA256SUMS.txt`.
- **Deviations:**
  - `Assets/app-icon.ico` is an embedded `<Resource>`, not a copied publish file, so the script validates the exe and the model instead; the icon is verified through the executable and the installer.
- **Dependencies:** Tasks 1, 2
- **Files:**
  - `build/package.ps1`
  - `.gitignore` (already covered `artifacts/`; unchanged)
- **Estimated scope:** S (2 files)

#### Task 4: Inno Setup Installer

- **Description:** Build a per-user installer around the self-contained publish: Start-menu and optional desktop shortcuts, uninstall entry, and app icon.
- **Acceptance criteria:**
  - [x] `installer/pcb-deftek.iss` installs to `%LOCALAPPDATA%\Programs\PCB Deftek`, creates Start-menu shortcuts with the app icon, and registers an uninstaller (`unins000.exe`, HKCU uninstall entry).
  - [x] Installer output `artifacts/PCB-Deftek-<version>-setup.exe` (144.9 MB); the installed app launches; uninstall removes the install directory, shortcuts, and the registry entry.
  - [x] User data (`%LOCALAPPDATA%\PcbDeftek`) is untouched by install and uninstall (SHA-256 snapshot diff = 0 both times; launching the app changes logs/db as expected).
  - [x] No admin rights required: `PrivilegesRequired=lowest`, silent install ran without elevation.
- **Verification:**
  - [x] `PCB-Deftek-0.10.0-setup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-` → exit 0; uninstall entry `{A3F1C0D2-6B8E-4E37-9C2A-7D5B1E9F4C60}_is1` with DisplayName "PCB Deftek", version 0.10.0.
  - [x] Start-menu folder contains the app and uninstall shortcuts; desktop shortcut absent by default (opt-in task).
  - [x] Installed app renders the dashboard and exits with code 0; `unins000.exe /VERYSILENT` removes the directory, Start-menu folder, and registry entry.
- **Deviations:**
  - Inno Setup was installed per-user (`%LOCALAPPDATA%\Programs\Inno Setup 6`), so the script probes both per-user and machine-wide compiler paths and accepts `-InnoCompiler`; `-SkipInstaller` produces only the publish and zip.
  - The first compile embedded a doubled closing brace in `AppId`, producing `...}}_is1` in the uninstall registry; corrected to `{{GUID}` before the recorded verification.
- **Dependencies:** Task 3
- **Files:**
  - `installer/pcb-deftek.iss`
  - `build/package.ps1`
- **Estimated scope:** S (2 files)

#### Task 5: Code Signing

- **Description:** Sign the application executable and the installer with the project certificate using SHA-256 and an RFC 3161 timestamp. Signing runs automatically in the tag-driven release workflow; there is no local signing requirement. `package.ps1` is extended so signing happens in the correct order: publish → sign `pcb-deftek.exe` → build portable zip → compile installer → sign `setup.exe` → write checksums.
- **Acceptance criteria:**
  - [x] `build/sign.ps1` signs `pcb-deftek.exe` and the setup executable with SHA-256 and an RFC 3161 timestamp against `http://timestamp.digicert.com`; the certificate comes from `SIGNING_PFX_BASE64`/`SIGNING_PFX_PASSWORD` (CI) or an explicit local PFX path (developer use).
  - [x] `signtool verify /pa /all` reports success for both files **in CI**; the workflow fails the job otherwise.
  - [x] Signing failures (missing secret, unreadable PFX, wrong password, timestamp failure) fail with a clear message; artifacts are never uploaded or published unsigned.
  - [x] No certificate, password, decoded PFX, or base64 material is committed, printed, or retained as a workflow artifact; the decoded PFX is written only to `$RUNNER_TEMP` and deleted in a `finally` block.
  - [x] `SHA256SUMS.txt` is produced after signing, so the recorded hashes match the signed files.
- **Verification:**
  - [x] CI run `43746365252` (`v0.10.0-rc.1`): `Successfully verified` for `pcb-deftek.exe` and `PCB-Deftek-0.10.0-setup.exe`; publisher `CN=PCB Deftek Developer`; RFC 3161 timestamp present.
  - [x] Downloaded release assets: both SHA-256 values match `SHA256SUMS.txt`; the zip contains the signed executable.
  - [x] The RC installer installs silently (exit 0), the installed app launches and exits 0, and uninstall removes the install directory.
- **Deviations:**
  - The owner-supplied certificate is self-signed (`CN=PCB Deftek Developer`, issuer equals subject, valid to 2031), so the release workflow installs its **public part** into `Cert:\LocalMachine\Root` on the ephemeral runner before verification. `/pa` passes there; end-user machines still report an untrusted publisher until a publicly trusted certificate is supplied.
- **Dependencies:** Task 4 · **Requires:** repository secrets (fulfilled) and the DigiCert timestamp URL (supplied)
- **Files:**
  - `build/sign.ps1`
  - `build/package.ps1` (signing hook and checksum ordering)
  - `.github/workflows/release.yml` (runner trust step)
  - `.gitignore` (already ignores `*.pfx`)
- **Estimated scope:** S (3 files + `.gitignore` review)

#### Task 6: GitHub Release Workflow

- **Description:** Tag-driven release: build, package, sign, and publish a GitHub Release with installer, portable zip, checksums, and release notes. The workflow is the sole signing entry point (tag-only; no push or PR triggers).
- **Acceptance criteria:**
  - [x] `.github/workflows/release.yml` triggers only on `v*` tags; trusts the certificate's public part on the ephemeral runner, installs Inno Setup, runs `package.ps1` with signing enabled, and fails before publishing if either signing secret is missing.
  - [x] The workflow asserts the tag version equals the csproj `<Version>` and refuses to sign mismatched artifacts.
  - [x] Creates the release with `PCB-Deftek-<version>-setup.exe`, portable zip, `SHA256SUMS.txt`, and a release-notes body.
  - [x] Release notes reuse the 0.10.0 notes from `launch-checklist.md` plus install instructions.
  - [ ] Tag `v0.10.0` produces a release whose assets download and verify against the checksums and carry valid signatures (same path exercised by `v0.10.0-rc.1`; final tag after Checkpoint C).
- **Verification:**
  - [x] Temporary `v0.10.0-rc.1` run `43746365252`: signed and verified both executables, uploaded signed artifacts, published a pre-release with installer, portable zip, checksums and generated notes. Checksums match the downloaded assets; the installer installs, launches and uninstalls cleanly.
  - [x] The RC release and tag were deleted after validation; no leftover release or tag.
  - [ ] Final `v0.10.0` tag after Checkpoint C.
- **Dependencies:** Tasks 3, 4, 5
- **Files:**
  - `.github/workflows/release.yml`
  - `README.md` (install section)
- **Estimated scope:** M (2 files)

---

### Checkpoint B: Distributable & Signed
- [x] Installer installs/uninstalls cleanly and is signed (validated from the `v0.10.0-rc.1` release asset; self-signed publisher).
- [ ] Release assets exist, are signed, and match checksums (assets were produced and verified for the RC, then deleted; the permanent release is created with `v0.10.0`).
- [x] Human review before Phase C (approved; Task 8 proceeded).

---

### Phase C: Soak & Documentation

#### Task 7: 5-Minute Live-Inference Soak

- **Description:** Measure the reduced soak on the RTX 2050: 1 minute warm-up, samples at t=2 and t=5, recorded in the checklist.
- **Acceptance criteria:**
  - [x] Live Detection ran 5 minutes with DirectML active (`ONNX Runtime initialized successfully with DirectML GPU provider (Device 0)`; every inference logged `GpuDirectML`).
  - [x] Working set and private bytes at t=2 and t=5; HUD FPS and average latency at both points.
  - [x] No crash, no error dialog, no `ERR`/`FTL` log entries (pre-fix run: the shutdown hang found there is fixed in #135).
  - [x] Numbers and machine context recorded; any failure is a release condition.
- **Verification:**
  - [x] Recorded table in `launch-checklist.md` §3a: t=2 510.8 MB WS / 717.2 MB private / 37.1 ms / 26 FPS; t=5 516.0 MB / 715.3 MB / 39.4 ms / 25 FPS.
  - [x] Post-fix re-run after #135: 28 FPS, ~714 MB private, close in 0.7 s, 0 `ERR`/`FTL`.
- **Deviations:**
  - Task 3 was not yet implemented when the soak ran, so it used the Release build from `main` @ `a71f14a`; the inference path is identical. The published-artifact re-run folds into Task 8.
- **Dependencies:** Tasks 1, 3
- **Files:**
  - `specs/2026-11-15-hardening-bdd-launch-readiness/launch-checklist.md`
- **Estimated scope:** XS (1 file + manual run)

#### Task 8: Update Launch Documents

- **Description:** Fold the new evidence into the launch record and revise the verdict.
- **Acceptance criteria:**
  - [x] `launch-checklist.md`: DirectML PASS evidence, toast PASS, installer/signing PASS, soak numbers, install instructions, revised known gaps (self-signed publisher, multi-instance, splash branding, drag automation).
  - [x] `validation.md` §5.4: live-camera item updated to owner-verified; new gates for DirectML, notifications, installer, signature, and release all pass; the performance gate now records the measured soak.
  - [x] Verdict updated: **Go — internal pilot**; public distribution remains **No-Go** until a publicly trusted certificate replaces the self-signed one and `v0.10.0` assets are published. Remaining gaps listed (multi-instance, splash branding, drag automation).
  - [x] `plan.md` links this sub-plan and notes the superseded deferred scope.
- **Verification:**
  - [x] Checklist re-walked against the clean-clone publish and the `v0.10.0-rc.1` release installer (download → checksum → install → launch → uninstall) with the same verdict.
  - [ ] Final `v0.10.0` release remains gated on Checkpoint C approval.
- **Deviations:**
  - The certificate is self-signed, so the verdict gates public distribution on a publicly trusted certificate; the original acceptance wording assumed a trusted certificate.
  - Install/uninstall was validated on the existing user profile; a clean-profile install is still listed under Checkpoint C.
- **Dependencies:** Tasks 1–7
- **Files:**
  - `specs/2026-11-15-hardening-bdd-launch-readiness/launch-checklist.md`
  - `specs/2026-11-15-hardening-bdd-launch-readiness/validation.md`
  - `specs/2026-11-15-hardening-bdd-launch-readiness/plan.md`
- **Estimated scope:** S (3 files)

---

### Checkpoint C: Release Ready
- [x] All gates in `validation.md` pass, including the new hardware/notification/packaging/signature/release rows.
- [ ] Signed installer installs on a clean profile and the app starts (validated on the existing profile; a clean-profile install remains).
- [ ] Human approval recorded before tagging `v0.10.0`.

---

## Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| DirectML unavailable on the RTX 2050 (driver/D3D12 issue) | High | Test fails with the active device named; fall back documented, GPU option hidden or labelled |
| Toast still blocked by Focus Assist or notification settings | Medium | Manual check from both VS debug and installed build; service logs every failure |
| Certificate is self-signed (issuer equals subject) | Medium | CI trusts the public part on the ephemeral runner so `/pa` passes; end users still see an untrusted publisher until a publicly trusted certificate replaces it |
| Checksums computed before signing | High | Signing hook in `package.ps1`: exe signed before zip/installer, setup signed before `SHA256SUMS.txt` |
| Tag/version mismatch produces wrongly named signed artifacts | Medium | Workflow asserts tag version equals csproj `<Version>` before packaging |
| Inno Setup absent on `windows-latest` | Low | Release workflow installs it before `package.ps1`; local runs probe per-user and machine-wide paths |
| Self-contained publish grows the installer (~250–350 MB) | Medium | Accepted for offline installs; portable zip remains available |
| Signing secrets mishandled or exposed | High | Secrets stay repo-level; decoded PFX only in `$RUNNER_TEMP` with `finally` cleanup; nothing printed or uploaded |
| Soak numbers regress on GPU | Medium | 5-minute gate is short; record values and treat failure as a release condition |

## Owner Decisions & Fulfilled Prerequisites

1. **Signing runs only in the tag-driven release workflow** (`v*` tags). No push, PR, or manual-dispatch trigger. `workflow_dispatch` is intentionally absent.
2. **Temporary test tag approved:** `v0.10.0-rc.1` validates signing in CI after Task 6 lands, then is deleted without leaving a release.
3. **Signature verification happens in CI only** (`signtool verify /pa /all`). No local verification step is required.
4. **`build/package.ps1` may be modified** to place signing before zipping/installer compilation and before checksum generation.
5. **Secret names confirmed:** `SIGNING_PFX_BASE64` and `SIGNING_PFX_PASSWORD` (repository-level; no environment scope). The earlier `SIGNING_CERT_*` names in this plan were wrong.
6. **Timestamp server supplied:** DigiCert RFC 3161 at `http://timestamp.digicert.com`.
7. **Tasks 3-4 merged:** `build/package.ps1` and `installer/pcb-deftek.iss` are on `main` (#139).
8. **Tag approval:** `v0.10.0` is pushed only after Checkpoint C passes.
9. **Certificate is self-signed** (`CN=PCB Deftek Developer`, issuer equals subject, valid 2026-11-15 to 2031-11-15, thumbprint `E8A43160…`). The release workflow installs its public part into `Cert:\LocalMachine\Root` on the ephemeral runner so `signtool verify /pa` passes; no private material is installed or exported.
10. **External distribution caveat:** a publicly trusted code-signing certificate is still required before shipping externally, because end-user machines do not trust this root.

Still owner-side and only observable at signing time: the PFX's code-signing EKU, private-key presence, validity window, password correctness, and the publisher subject shown in file properties.

## Open Questions

- **Public trust:** the current certificate is self-signed, so external users see an untrusted publisher even though signatures and timestamps verify. A publicly trusted code-signing certificate is required before shipping outside the internal pilot; the workflow needs no change when that PFX replaces the secret.
