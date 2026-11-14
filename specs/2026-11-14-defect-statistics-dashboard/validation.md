# Validation Criteria: Phase 8 — Defect Statistics Dashboard

## 1. Build & Quality Gates

- **Build command:** `dotnet build pcb-deftek.slnx`
- **Pass condition:** exit code 0, 0 errors, 0 warnings.
- **Nullable:** nullable reference types enabled across all new code; no `#nullable disable`, no suppression attributes added.
- **Architecture:** no `System.Windows.Controls` references in ViewModels; SQL only under `Infrastructure/Persistence`; no direct UI-thread file I/O.
- **Dependencies:** no new NuGet packages; `pcb-deftek.csproj` package list unchanged.
- **Database:** no schema migration added; dashboard queries read existing `Inspections`/`Defects`.
- **Assets:** all nine `Assets/Images/dashb-info-{abbreviation}.png` files exist and are resolved through a repository-relative/pack path, never an absolute user path.
- **Files:** no hardcoded absolute user paths; export paths resolved through existing settings/known-folder services.

---

## 2. Automated Test Gates

- **Full suite:** `dotnet test pcb-deftek.slnx`
- **Pass condition:**
  - 100% passing, 0 failed, 0 skipped.
  - Current baseline of **613 tests** does not regress.
  - All new dashboard tests execute in the suite.
- **Focused commands during implementation:**

```powershell
dotnet test tests/pcb-deftek.Tests/ --filter DashboardStatisticsTests
dotnet test tests/pcb-deftek.Tests/ --filter DashboardRepositoryTests
dotnet test tests/pcb-deftek.Tests/ --filter DashboardFilterViewModelTests
dotnet test tests/pcb-deftek.Tests/ --filter DashboardViewModelTests
dotnet test tests/pcb-deftek.Tests/ --filter DashboardProgressServiceTests
dotnet test tests/pcb-deftek.Tests/ --filter PassFailChartTests
dotnet test tests/pcb-deftek.Tests/ --filter MetricTimeSeriesChartTests
dotnet test tests/pcb-deftek.Tests/ --filter DefectBarChartTests
dotnet test tests/pcb-deftek.Tests/ --filter DefectInfoAssetTests
dotnet test tests/pcb-deftek.Tests/ --filter DependencyInjectionTests
dotnet test tests/pcb-deftek.Bdd/ --filter DashboardFeature
```

### 2.1 Required Unit Coverage

- **Contracts:** zero/empty results, all nine class buckets, stable ordering, inclusive boundaries.
- **Repository:** date only; class only; severity only; class+severity; all classes selected; defectless records excluded by defect filters; totals and buckets consistent; no N+1.
- **Filter VM:** presets, week culture boundary, custom range, invalid range, class/severity sets, clear-all single notification.
- **Dashboard VM:** initial all-time load, reload on filter change, cancellation/stale guard, loading/empty/error states, metric projections, layout toggle, reorder commands, export event snapshot, startup persistence, progress state.
- **Charts:** 0/1/many points; max scaling; zero bars; label thinning; hover point resolution; click event.
- **Assets:** all nine expected paths; lowercase abbreviations; missing image fallback.
- **Progress service:** idle/running/completed/failed transitions, 0–100 clamp, UI-thread dispatch safety.

### 2.2 Required BDD Coverage

`tests/pcb-deftek.Bdd/Features/DefectStatisticsDashboard.feature`:

1. Filters compose and update all four metric cards, including per-day PCBs/defects volume charts.
2. Layout toggle switches 1/2 columns and card reorder changes display order.
3. All nine defect information cards load the correct `dashb-info-{abbreviation}.png` asset.
4. Chart hover/click opens the enlarged modal and closes with `Esc`.
5. Notification modal shows idle/running/completed progress states.
6. Startup checkbox persists and reloads.
7. Empty/no-match filters show zero metrics without errors.

Scenarios must use an isolated SQLite database and temporary data; no dependency on the developer's real history or asset downloads.

---

## 3. Functional Acceptance Criteria

### 3.1 Filter & Action Bar

- Default state: all time, no class, no severity; all four cards populated.
- Day/Week/Month presets use local calendar bounds; clicking the active preset clears it.
- Custom range is inclusive; start after end shows validation and issues no query.
- Defect Type and Severity are multi-select; OR within category, AND across categories.
- Clear Filters resets all three categories and reloads.
- Changing filters cancels an in-flight query; no stale metrics or charts are shown.
- Export Data raises a filtered export request; no PDF file is fabricated in Phase 8.
- Notifications opens the progress modal.
- Layout toggle reflects 1-column/2-column state.

### 3.2 Metric Cards

- **Total PCBs:** count matches base inspections; chart points match daily inspection counts for the active range.
- **Total Defects:** count matches matching defects; chart points match daily matching-defect counts.
- **PASS/FAIL Rate:** daily points match base inspection outcomes; green/red series are distinguishable by line and legend, not color alone.
- **Defect Distribution:** nine bars in class order, zero counts visible, class colors match `ColorHex`.
- Hover exposes exact values; card values update after filter changes.
- Card drag handle reorders only the cards; dropping outside restores the current order.
- Move Left/Right commands provide keyboard reorder.
- Empty/no-match states are distinct from loading/error.

### 3.3 Charts & Modal

- All four charts render without exceptions at zero, one, and many points.
- Chart axes/labels remain legible at 1-column and 2-column widths.
- Clicking any chart opens an enlarged modal with the same data.
- `Esc` and close button dismiss; focus returns to the originating card/chart.
- Screen readers can identify each chart's series/categories through accessible names/summaries.

### 3.4 Defect Information Accordion

- One accordion with nine items ordered by `ClassId`.
- Each item shows class name, severity, 1:1 image, explanation, root cause, impact, recommended action.
- Images resolve exactly:
  - `Assets/Images/dashb-info-sh.png`
  - `Assets/Images/dashb-info-op.png`
  - `Assets/Images/dashb-info-mb.png`
  - `Assets/Images/dashb-info-sp.png`
  - `Assets/Images/dashb-info-sc.png`
  - `Assets/Images/dashb-info-hb.png`
  - `Assets/Images/dashb-info-cs.png`
  - `Assets/Images/dashb-info-cfo.png`
  - `Assets/Images/dashb-info-bmfo.png`
- Missing/corrupt images show the placeholder and log a warning.
- Accordion remains keyboard accessible and text-selectable.

### 3.5 Notification Modal

- Displays Inference and Data export bars with Idle/Running/Completed/Failed status.
- Progress values stay within 0–100; idle state shows 0% and no animation.
- Modal is accessible and dismissible via `Esc`/close.
- No fake progress timers.

### 3.6 Startup Preference

- "Skip dashboard on startup" reflects the persisted setting on load.
- Toggling persists immediately and survives restart.
- Persistence failure keeps the previous value and shows an error notification.

---

## 4. Performance & Stability Gates

| Gate | Threshold | Method |
|---|---|---|
| Dashboard query | p95 ≤ 150 ms on 1,000 seeded inspections | Seeded integration timing |
| First dashboard render | Cards and charts visible ≤ 500 ms after query completion | Manual/stopwatch |
| Filter change | Query cancelled; new result within 300 ms for 1,000 records | Seeded timing |
| Chart interaction | Hover/click remains responsive; no per-frame allocations | Manual interaction |
| Layout toggle | Reflow completes without visible stall | Manual |
| Large history | All-time chart remains readable and does not clip | Seeded 1,000 records |
| Memory | Repeated filter cycles and modal open/close show no unbounded growth | Task Manager over 10 cycles |

Stability: filter repeatedly, toggle layout, reorder cards, open/close both modals 20 times, expand every accordion item, switch Light/Dark — no crash, stale data, or leaked timers.

---

## 5. Manual Verification Script

1. Seed records across today, this week, this month, and older; include PASS and FAIL records and all nine classes with mixed severities.
2. Open Dashboard; verify four cards, default two-column layout, and all-time metrics.
3. Apply Day, Week, Month presets; toggle each off; verify bounds and metrics.
4. Apply a custom range; verify validation and result counts.
5. Select multiple classes and severities; verify AND/OR composition, totals, all four charts, and no-match state.
6. Toggle 1-column/2-column; verify no clipping and charts reflow.
7. Drag each card by its handle; verify order changes; drop outside and verify recovery.
8. Use keyboard Move Left/Right; verify accessible reorder.
9. Hover every chart point/bar; verify values.
10. Open each chart modal; resize the window; close with `Esc`; verify focus returns.
11. Expand every defect accordion item; verify all nine images and text.
12. Temporarily rename one image and verify placeholder fallback; restore it.
13. Open Notifications; verify idle bars; trigger progress updates and verify running/completed states.
14. Click Export Data; verify the filter snapshot is raised and no fake file appears.
15. Toggle Skip dashboard; restart and verify startup routing.
16. Repeat steps 2–12 at 100% and 150% DPI in Light and Dark themes.

---

## 6. Edge-Case Verification

- [ ] Empty database: zero metrics, empty chart state, no exceptions.
- [ ] Exactly one inspection and exactly one defect render correctly.
- [ ] A chart with one day renders a point/segment, not a divide-by-zero.
- [ ] All nine class bars present when only one class has defects.
- [ ] Custom start after end does not query.
- [ ] PASS+class filter yields zero matching inspections and a clear empty state.
- [ ] Missing/corrupt image degrades without breaking the accordion.
- [ ] Narrow window: layout toggle prevents clipped cards; labels thin.
- [ ] Drag outside the card grid does not reorder.
- [ ] Notification modal while idle has no running timer.
- [ ] Export command repeated rapidly raises consistent filter snapshots.
- [ ] Startup persistence failure does not crash the page.

---

## 7. Merge Readiness & Definition of Done

Phase 8 may merge into `main` only when:

1. All six task groups in `plan.md` are implemented and checked.
2. `dotnet build pcb-deftek.slnx` succeeds with 0 errors and 0 warnings.
3. `dotnet test pcb-deftek.slnx` passes 100% (no skipped tests), with no regression against the 613-test baseline.
4. All functional acceptance criteria in §3 are verified; performance gates in §4 are met.
5. The nine `Assets/Images/dashb-info-*.png` paths are verified by automated tests.
6. No new dependencies, no schema migration, no hardcoded user paths.
7. Clean Git history following the 6-step lifecycle in `AGENTS.md`.
8. PR linked to its issue and passes the 4-step Feature Review and Quality Improvement pipeline.
9. Human review approval is recorded before merge; the PR is not self-merged.

---

## 8. Failure Triage

| Symptom | First check |
|---|---|
| Totals don't match filters | Dashboard filter → UTC bounds → SQL predicate composition |
| Severity filter returns nothing | Severity→class-code mapping from `DefectKnowledgeBase` |
| PASS series zero with defect filter | Expected by metric definition; verify filter scope |
| Charts blank | `HasData`/projection and zero-point rendering path |
| Chart labels overlap | Label-thinning threshold at current width |
| Card order resets after toggle | Reorder mutates the observable card list, not a template copy |
| Accordion images missing | Asset file names, lowercase abbreviation converter, pack URI |
| Image crashes app | Converter must return placeholder/null, not throw |
| Startup checkbox doesn't persist | `IUserSettingsService` save path and reload |
| Export creates a file | A placeholder PDF path slipped in; Phase 8 must only raise the event |
| Modal timer keeps running | Stop/cancel progress timer when idle/closed |
| DI resolution fails | Dashboard repository/progress service registration and lifetimes |
| BDD touches real data | Isolated connection factory and temp asset paths |
