# Validation Criteria: Phase 10 — Hardening, BDD Verification & Launch Readiness

## 1. Build & Quality Gates

| Gate | Command | Pass condition |
|---|---|---|
| Restore | `dotnet restore pcb-deftek.slnx` | Completes; no new package added by this phase |
| Build | `dotnet build pcb-deftek.slnx -c Release --no-restore -warnaserror` | 0 errors, **0 warnings** |
| Nullable | Build output | No nullable warnings; no suppression added |
| Dependencies | `git diff` on `pcb-deftek.csproj` | Only version metadata changes |
| Schema | `git diff` on `Infrastructure/Persistence/Migrations` | No migration added |
| Hygiene | `git diff` | No absolute user paths, no secrets, no generated artifacts committed |

## 2. Automated Test Gates

Baseline entering Phase 10: **834 passing tests** (790 unit + 44 BDD), 0 failed, 0 skipped (Phase 9 `validation.md` §2.2). Phase 10 must keep that set green and add the coverage below.

### 2.1 Required Unit / Integration Coverage

| Area | Required assertions |
|---|---|
| Card drag payload | Payload round-trips a card; foreign/empty data resolves to `null`; dropping a card on itself is a no-op; `TryApplyCardDrop` moves to the target index and persists order |
| Startup coordinator | Failing migrator → failed result carrying the exception, router not invoked; failing router → success, migrator invoked once; success → both invoked in declaration order |
| Startup wiring | `DependencyInjectionTests` and existing bootstrap tests remain green; no real `%LOCALAPPDATA%` database touched |
| View composition | `DashboardViewCompositionTests` still proves the card items host and drag affordances after the extraction |
| Harness (build only) | Perf project compiles in the solution; `dotnet test pcb-deftek.slnx` does not execute it |

### 2.2 Required BDD Coverage

| Scenario | Assertion |
|---|---|
| Dashboard card drop reorders and persists | Order follows the drop target index, survives reload, and a foreign payload changes nothing |
| Clearing viewer focus restores the default sidebar | Focused state → `SelectMarker(null)` → record name, timestamp, and class summaries visible; `IsDefectFocused` false |
| Failed export notifies and writes nothing | `Data export` reaches `Failed`, toast title `Report export failed`, no `.pdf`/`.tmp`/staging left, no success toast |

BDD runs against isolated SQLite and temp artifact roots; it must never touch the developer's real database, Downloads folder, or settings.

### 2.3 Full-Suite Gate

- `dotnet test pcb-deftek.slnx` → 100 % pass, 0 skipped, no regression against the 834-test baseline.
- Record the new totals; expected additions: 3 BDD scenarios plus the new unit tests.

## 3. BDD Coverage Matrix (Roadmap Phase 10 Checklist)

| Roadmap-named workflow | Scenario | Status entering Phase 10 | Phase 10 verdict |
|---|---|---|---|
| Batch ETA | `FileBatchDetection` — inline ETA computed; 100 % progress; records persisted | Covered | Verify, no change |
| Camera auto-hide | `LiveCameraInspection` — controls hide after 3 s; status/telemetry preserved; mouse restores | Covered | Verify, no change |
| Viewer sidebar state transitions | `HistoryAndDefectViewer` — default and focused | Partial | Add focused → default scenario |
| Dashboard drag-and-drop | `DefectStatisticsDashboard` — reorder + persistence via view-model command | Partial | Add drop-path unit tests + BDD scenario |
| Export notifications | `AuditPdfExport` — success, no-match, large-export decline, cancel | Partial | Add failure-notification scenario |

Existing scenarios are executed unchanged; the phase must not weaken or re-pin them.

## 4. CI Verification Gates

| Check | Evidence |
|---|---|
| Workflow exists and is valid YAML | File committed; first run starts successfully |
| Triggers correct | Run appears for push and for a pull request to `main`; `workflow_dispatch` listed |
| Warning gate effective | Build step uses `-warnaserror`; job log shows 0 warnings |
| Tests split by project | Two test steps; both `.trx` artifacts present |
| Artifacts on failure | Deliberately failing run (local branch) still uploads results |
| Duration | Job ≤ 20 minutes, recorded |
| Permissions | `permissions: contents: read` present; no secrets referenced |
| Concurrency | Pushing twice to the same ref cancels the first run |

## 5. Performance Gates

Measured by `tests/pcb-deftek.Perf` on the development machine (10-minute run, 30 fps capture target, CPU provider, real `best.onnx`).

| Metric | Target | Basis |
|---|---|---|
| Dispatcher callback lateness p99 | ≤ 100 ms | Phase 9 UI-responsiveness gate |
| Dispatcher callback lateness max | ≤ 500 ms | Single outlier tolerated, freezes not |
| Throughput decay (first vs last steady-state minute) | ≤ 25 % | Leak/slowdown detection |
| Steady-state working-set growth (minute 3 → 10) | ≤ 50 MB | Plateau expected |
| Post-run managed heap | ≤ 25 % above post-warm-up baseline after forced GC | Retained-graph detection |
| Engine shutdown | ≤ 2 s, no surviving capture/inference threads | Navigation/shutdown hygiene |
| Run stability | 0 unhandled exceptions; report written; exit code 0 | Tool correctness |
| Repeatability | Two 1-minute runs within ±20 % on the same machine | Prevents noise-driven verdicts |

Absolute FPS and latency values are recorded for the baseline; they are not merge gates because hardware varies.

## 6. CI Results (to be recorded)

| Item | Value |
|---|---|
| Workflow run URL | _pending_ |
| Runner / OS image | _pending_ |
| Job duration | _pending_ |
| Unit test count / result | _pending_ |
| BDD test count / result | _pending_ |
| Warning count | _pending_ |

## 7. Functional Acceptance Criteria

### 7.1 CI & Documentation

- [ ] Workflow triggers on push, pull request, and manual dispatch; superseded runs cancel.
- [ ] Release build fails on any C#/XAML warning.
- [ ] Unit and BDD results are uploaded on success and failure.
- [ ] `README.md` documents prerequisites, commands, test layout, data/log locations, and CI.
- [ ] Every documented command was executed successfully.

### 7.2 BDD Closure

- [ ] Dashboard drop with a valid payload reorders cards and the new order survives reload.
- [ ] Dashboard drop with a foreign payload leaves order and settings unchanged.
- [ ] Clearing viewer focus restores the default sidebar and keeps `Delete Image` available.
- [ ] Export to an unwritable destination reports `Failed`, shows the failure toast, and leaves no PDF/temp/staging.
- [ ] All previously passing scenarios still pass without edits to their assertions.

### 7.3 Performance

- [ ] Harness runs for 1 minute and 10 minutes and writes both report files.
- [ ] Harness writes only under `--out`, never inside the repository or `%LOCALAPPDATA%`.
- [ ] Every gate in §5 evaluated and recorded in §9.
- [ ] Any waived gate has a written justification and a rerun note.
- [ ] `dotnet test pcb-deftek.slnx` does not run the harness.

### 7.4 Startup Hardening

- [ ] Migration failure: fatal log, one dialog (when enabled), exit code 1, no `MainWindow`, no lingering process.
- [ ] Routing failure: warning log, shell opens on the default view.
- [ ] Success path: migrations → routing → window, unchanged.
- [ ] Dialog is suppressed when `App.ShowErrorDialogs` is false (automation).

### 7.5 Release Identity & Launch Checklist

- [ ] Built executable reports version `0.10.0` via `FileVersionInfo`.
- [ ] `CHANGELOG.md` covers every essential feature and lists known limitations.
- [ ] `launch-checklist.md` contains evidence per item and one overall Go/No-Go.
- [ ] Portable publish executed; output launched; smoke matrix executed against it.
- [ ] `dotnet list package --vulnerable` shows no known vulnerable dependency (or the finding is documented with a decision).
- [ ] Re-walking the checklist after `git clean -xdf` produces the same result.

## 8. Merge Readiness & Definition of Done

Phase 10 may merge into `main` only when:

1. All six task groups in `plan.md` are implemented and checked.
2. `dotnet build pcb-deftek.slnx -c Release -warnaserror` succeeds.
3. `dotnet test pcb-deftek.slnx` passes 100 %, 0 skipped, with no regression against the 834-test baseline.
4. CI is green on the final commit.
5. All performance gates in §5 are met or explicitly waived with recorded numbers.
6. The launch checklist has a recorded Go/No-Go with evidence.
7. No new NuGet package, database migration, or settings key is introduced.
8. No production behavior changes beyond the startup failure path.
9. The `AGENTS.md` §7 review pipeline completed with user approval at each stage; outcomes recorded in §10.
10. Clean Git history following the `AGENTS.md` §4 lifecycle; PR linked to its issue; human approval recorded; branch deleted after merge.

## 9. Results (to be completed during implementation)

### 9.1 Automated verification

| Command | Result |
|---|---|
| _pending_ | _pending_ |

### 9.2 Performance baseline

| Metric | Value | Gate |
|---|---|---|
| Machine context | _pending_ | — |
| Processed frames / emitted frames | _pending_ | — |
| FPS first vs last steady-state minute | _pending_ | ≤ 25 % decay |
| Latency p50 / p95 / p99 / max | _pending_ | recorded |
| Dispatcher lateness p50 / p95 / p99 / max | _pending_ | p99 ≤ 100 ms; max ≤ 500 ms |
| Working-set delta (minute 3 → 10) | _pending_ | ≤ 50 MB |
| Managed heap after forced GC vs baseline | _pending_ | ≤ +25 % |
| Engine stop time / surviving threads | _pending_ | ≤ 2 s / 0 |
| Report paths | _pending_ | outside repository |

### 9.3 Launch verdict

| Item | Result |
|---|---|
| Publish command and output size | _pending_ |
| Smoke matrix pass count | _pending_ |
| Vulnerable dependency scan | _pending_ |
| Go/No-Go | _pending_ |

## 10. Review Pipeline Outcomes (to be recorded)

| Stage | Executed | Verdict / findings |
|---|---|---|
| `code-review-and-quality` | _pending_ | _pending_ |
| `code-simplification` | _pending (requires approval)_ | _pending_ |
| `performance-optimization` | _pending (requires approval)_ | _pending_ |
| `security-and-hardening` | _pending (requires approval)_ | _pending_ |

## 11. Failure Triage

| Symptom | First check |
|---|---|
| CI job never starts | Workflow YAML syntax/indentation; `on:` block; branch name filters |
| CI fails on setup-dotnet | `dotnet-version` availability on `windows-latest`; use `10.0.x` |
| Build fails only in CI | `-warnaserror` on analyzer/generated code; compare local Release build |
| BDD fails only in CI | Interactive-desktop assumption, culture, or temp-path permissions; scenario must use injected fakes and temp roots |
| Tests pass but counts differ | A filter or trait excluded tests; CI must run the projects without filters |
| Drop scenario passes but real drag does not | `TryApplyCardDrop` diverged from `OnCardDrop`; view must delegate, not duplicate |
| Viewer reset scenario fails | `SelectMarker(null)` no longer clears `IsDefectFocused` or the default projection |
| Export failure scenario writes a file | Exporter created the destination before validation, or cleanup skipped the failure path |
| Perf harness reports zero FPS | Fake camera not started, engine not subscribed, or frames disposed before processing |
| Perf memory grows linearly | Staged/annotated `BitmapSource` not released, `Mat` leak, or event subscription left attached |
| Dispatcher lateness spikes | Inference/composition on the dispatcher; verify engine marshalling |
| Harness leaves threads alive | `StopAsync` not awaited or camera subscription not detached |
| Startup dialog appears twice | Both dispatcher handler and coordinator reporting the same failure |
| Startup exits 0 on migration failure | `Shutdown(1)` not reached; exception swallowed |
| Version missing from binary | `Version` properties set in the wrong PropertyGroup or stale build output |
| Changelog claims unimplemented behavior | Entry derived from commit messages instead of verified specs/tests |
| Checklist item has no evidence | Re-run the check; an unevidenced item is a No-Go |
