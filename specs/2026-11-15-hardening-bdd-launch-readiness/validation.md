# Validation Criteria: Phase 10 — Hardening, BDD Verification & Launch Readiness

## 1. Build & Quality Gates

| Gate | Command | Pass condition |
|---|---|---|
| Restore | `dotnet restore pcb-deftek.slnx` | Completes; no new package |
| Build | `dotnet build pcb-deftek.slnx -c Release --no-restore -warnaserror` | 0 errors, **0 warnings** |
| Nullable | Build output | No nullable warnings; no suppression added |
| Project file | `git diff pcb-deftek.csproj` | Only version metadata |
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
| Dashboard drag-and-drop | `DefectStatisticsDashboard` — reorder + persistence via view model | Partial | Deferred to manual smoke step |
| Export notifications | `AuditPdfExport` — success, no-match, decline, cancel | Partial | Add failure notification |

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

## 5. Manual Performance Gates

Protocol in `requirements.md` §5. Release build, Live Detection, 10 minutes, samples at t = 2 and t = 10 minutes.

| Metric | Target | Basis |
|---|---|---|
| Working-set growth after warm-up | ≤ 50 MB | Leak detection |
| FPS change t=2 → t=10 | ≤ 25 % decline | Throughput stability |
| Average latency change t=2 → t=10 | ≤ 25 % increase | Pipeline stability |
| UI freeze during interaction | none over 1 s | Responsiveness |
| Crash / unhandled dialog / `ERR`+`FTL` log entries | none | Stability |
| Camera unavailable | gate recorded untested | Launch gap, not a silent pass |

Recorded limitations: no dispatcher-stall percentiles, no repeated automated runs, no memory trend across sessions.

## 6. CI Results (to be recorded)

| Item | Value |
|---|---|
| Workflow run URL | _pending_ |
| Job duration | _pending_ |
| Unit tests / result | _pending_ |
| BDD tests / result | _pending_ |
| Warning count | _pending_ |

## 7. Functional Acceptance Criteria

### 7.1 CI & Documentation

- [ ] Workflow triggers on push, PR, and manual dispatch; superseded runs cancel.
- [ ] Release build fails on any warning.
- [ ] Unit and BDD results upload on success and failure.
- [ ] README prerequisites, commands, test-isolation rule, and data/log locations are correct and executed.

### 7.2 BDD Closure

- [ ] Clearing viewer focus restores the default sidebar and keeps delete available.
- [ ] Export to an unwritable destination reports `Failed`, shows the failure toast, and leaves no PDF/temp/staging.
- [ ] Existing scenarios still pass without edits.

### 7.3 Performance

- [ ] 10-minute soak completed; four values sampled at t=2 and t=10.
- [ ] UI interactions performed at t=5 with no freeze over 1 s.
- [ ] Log window free of `ERR`/`FTL` entries.
- [ ] Gate verdicts recorded in §9, with machine context and observed numbers.
- [ ] Failed gates fixed or accepted in writing in the launch checklist.

### 7.4 Startup Hardening

- [ ] Migration failure: fatal log, one dialog, exit code 1, no window, no lingering process.
- [ ] Routing failure: warning log, shell opens on the default view.
- [ ] Success path unchanged.
- [ ] Dialog suppressed when `App.ShowErrorDialogs` is false.

### 7.5 Release Identity & Launch Checklist

- [ ] Executable reports version `0.10.0` via `FileVersionInfo`.
- [ ] Checklist covers all eight sections in `requirements.md` §7, each item with evidence.
- [ ] Release notes for `0.10.0` included in the checklist with Known Limitations.
- [ ] Portable publish executed, launched, and smoke-tested.
- [ ] `dotnet list package --vulnerable` shows no known vulnerable dependency, or the finding is documented with a decision.
- [ ] Checklist re-walked after `git clean -xdf` with the same verdict.
- [ ] One overall Go/No-Go recorded.

## 8. Merge Readiness & Definition of Done

Phase 10 may merge into `main` only when:

1. All six task groups in `plan.md` are complete.
2. `dotnet build pcb-deftek.slnx -c Release -warnaserror` succeeds.
3. `dotnet test pcb-deftek.slnx` passes 100 %, 0 skipped, no regression against 834.
4. CI is green on the final commit.
5. Performance gates are met or explicitly accepted with recorded numbers.
6. The launch checklist has a recorded Go/No-Go with evidence.
7. No new package, migration, or settings key; no production change beyond the startup failure path.
8. Deferred items are listed as known gaps in the checklist.
9. `AGENTS.md` §7 review pipeline completed with approval at each stage.
10. Clean history per `AGENTS.md` §4; PR linked to its issue; human approval; branch deleted after merge.

## 9. Results (completing during implementation)

### 9.1 Automated verification

| Command | Result |
|---|---|
| _pending_ | _pending_ |

### 9.2 Performance soak

| Metric | t = 2 min | t = 10 min | Gate |
|---|---|---|---|
| Working set (MB) | _pending_ | _pending_ | delta ≤ 50 MB |
| Private bytes (MB) | _pending_ | _pending_ | recorded |
| FPS | _pending_ | _pending_ | ≤ 25 % decline |
| Average latency (ms) | _pending_ | _pending_ | ≤ 25 % increase |
| UI freeze > 1 s | _pending_ | — | none |
| `ERR`/`Fatal` log entries | _pending_ | — | none |
| Machine context | _pending_ | — | — |

### 9.3 Launch verdict

| Item | Result |
|---|---|
| Publish command / output size | _pending_ |
| Smoke matrix pass count | _pending_ |
| Vulnerable dependency scan | _pending_ |
| Go/No-Go | _pending_ |

## 10. Review Pipeline Outcomes

| Stage | Executed | Verdict |
|---|---|---|
| `code-review-and-quality` | _pending_ | _pending_ |
| `code-simplification` | _pending (approval required)_ | _pending_ |
| `performance-optimization` | _pending (approval required)_ | _pending_ |
| `security-and-hardening` | _pending (approval required)_ | _pending_ |

## 11. Failure Triage

| Symptom | First check |
|---|---|
| CI job never starts | Workflow YAML syntax; `on:` block; branch filters |
| Build fails only in CI | `-warnaserror` on analyzer/generated code; compare local Release build |
| BDD fails only in CI | Desktop/webcam assumption, culture, temp-path permissions |
| Viewer reset scenario fails | `SelectMarker(null)` no longer clears `IsDefectFocused` or the default projection |
| Export failure scenario writes a file | Destination validated after creation, or cleanup skipped |
| Soak shows linear memory growth | Annotated `BitmapSource`/`Mat` retention, or an event subscription left attached |
| FPS degrades during soak | Inference queue backlog; check stale-frame coalescing |
| UI freezes during soak | Work moved onto the dispatcher; inspect annotation/encode paths |
| Startup dialog appears twice | Dispatcher handler and coordinator both reporting |
| Startup exits 0 on migration failure | `Shutdown(1)` not reached or exception swallowed |
| Version missing from binary | Wrong PropertyGroup or stale output |
| Checklist item has no evidence | Re-run the check; an unevidenced item is a No-Go |
