# Implementation Plan: Phase 8 — Defect Statistics Dashboard

## Overview

Replace the placeholder Dashboard with a filterable analytics page: four metric cards, custom WPF line/column/bar charts, a defect knowledge accordion, layout/reorder controls, export and progress seams, and the startup preference. This plan follows the repository convention of tracking tasks in `plan.md` (AGENTS.md §1); no separate `tasks/` directory is used.

## Architecture & Dependency Graph

```
Domain contracts (filter, statistics, chart points)
        │
        ▼
IDashboardRepository / SQLite aggregate query
        │
        ├── DashboardFilterViewModel (date/class/severity)
        │
        └── DashboardViewModel (metrics, cards, actions)
                    │
        ┌───────────┴────────────┐
        ▼                        ▼
Chart controls              DashboardView.xaml
(line/bar geometry)         (cards, filters, accordion, modals)
        │                        │
        └───────────┬────────────┘
                    ▼
        DI registration + BDD + gates
```

Data flow: filters → local date bounds converted to UTC → dashboard SQL aggregates → view-model projections → chart controls/cards. The `DefectKnowledgeBase` supplies taxonomy, severity, colors, and diagnostic text; `Assets/Images` supplies the nine knowledge images.

## Task Breakdown

### Task Group 1: Dashboard Data Contract & Aggregate Query

#### Task 1.1: Dashboard Contracts

- **Description:** Add immutable contracts for the dashboard query and its results, independent of WPF and SQL.
- **Acceptance criteria:**
  - [x] `DashboardFilter` carries inclusive UTC start/end bounds and selected `DefectClass`/`DefectSeverity` sets.
  - [x] `DashboardStatistics` exposes total inspections, total matching defects, total pass/fail counts, the resolved `DashboardBucketGranularity`, adaptive buckets (start/end/label, inspections, matching defects, passes, fails), and per-class defect counts.
  - [x] Buckets are ordered oldest→newest and cover hourly, daily, weekly (culture week start), or monthly granularity; class counts are ordered by `DefectClassInfo.ClassId` and include all nine classes with zero defaults.
  - [x] A pure `DashboardBucketPolicy` maps a date span to granularity: ≤2 days hourly, ≤90 days daily, ≤2 years weekly, otherwise monthly; empty data resolves to daily.
  - [x] Contracts are pure records/read-only types with no WPF or repository dependencies.
- **Verification:**
  - [x] `dotnet test tests/pcb-deftek.Tests/ --filter DashboardStatisticsTests`
  - [x] Build succeeds with 0 warnings
- **Dependencies:** None
- **Files:**
  - `Domain/Contracts/DashboardStatistics.cs`
  - `tests/pcb-deftek.Tests/DashboardStatisticsTests.cs`
- **Estimated scope:** S (2 files)

#### Task 1.2: SQLite Dashboard Aggregate Query

- **Description:** Implement `IDashboardRepository` / `SqliteDashboardRepository` with filtered aggregate SQL for metric and chart data.
- **Acceptance criteria:**
  - [x] Interface exposes one async `GetStatisticsAsync(DashboardFilter, CancellationToken)` method.
  - [x] Query applies inclusive `CreatedAtUtc` bounds and optional class/severity filters; severity maps to class codes through `DefectKnowledgeBase` (no schema change).
  - [x] When class/severity filters are active, only inspections with at least one matching defect are included; PASS inspections are excluded.
  - [x] Total defects and distribution count only defects matching the active filters; distribution includes all nine classes.
  - [x] Query resolves adaptive granularity from explicit filter bounds; for all-time, it uses the matching data extent; empty data falls back to daily.
  - [x] Buckets group in local time at hour/day/week/month granularity; each bucket contains inspections, matching defects, passes, and fails; ordering is stable.
  - [x] Bucket labels are culture-aware and identify the unit (hour/day/week/month).
  - [x] Empty result returns zeroed metrics and nine zero class buckets without exceptions.
- **Verification:**
  - [x] `dotnet test tests/pcb-deftek.Tests/ --filter DashboardRepositoryTests`
  - [x] Seed 120 records covering date/class/severity combinations and assert totals, buckets, ordering, and no N+1 access
  - [x] Build succeeds with 0 warnings
- **Dependencies:** Task 1.1
- **Files:**
  - `Infrastructure/Persistence/Repositories/IDashboardRepository.cs`
  - `Infrastructure/Persistence/Repositories/SqliteDashboardRepository.cs`
  - `tests/pcb-deftek.Tests/DashboardRepositoryTests.cs`
- **Estimated scope:** M (3 files)

#### Task 1.3: Query Integration & Edge Cases

- **Description:** Prove dashboard aggregates against the real SQLite schema, including boundary dates and combined filters.
- **Acceptance criteria:**
  - [x] Date bounds include records exactly at the boundary instants.
  - [x] Adaptive boundaries at 2 days, 90 days, and 2 years select the correct granularity.
  - [x] Class-only, severity-only, and class+severity filters compose correctly.
  - [x] Duplicate defect classes count individually while distribution groups by class.
  - [x] Records with no defects never appear when a class/severity filter is active.
  - [x] All nine selected classes with no severity filter behave like the unfiltered class set.
- **Verification:**
  - [x] `dotnet test tests/pcb-deftek.Tests/ --filter DashboardRepositoryTests`
  - [x] Existing persistence integration tests still pass
  - [x] Build succeeds with 0 warnings
- **Dependencies:** Task 1.2
- **Files:**
  - `tests/pcb-deftek.Tests/DashboardRepositoryTests.cs`
- **Estimated scope:** S (1 file)

---

### Checkpoint 1: Dashboard Data Correct
- [x] Contract and SQL tests pass.
- [x] Totals and chart buckets match seeded data.
- [x] No new schema migration or NuGet package.
- [ ] Review with human before UI work.

---

### Task Group 2: Dashboard Filter & View-Model State

#### Task 2.1: Shared Date-Range Preset Resolver

- **Description:** Extract the local→UTC preset/custom-range calculation shared by History and Dashboard so filter semantics cannot drift.
- **Acceptance criteria:**
  - [x] Resolver returns inclusive local start/end bounds for Day/Week/Month/None, honoring the current culture's first day of week.
  - [x] Custom range validates start ≤ end and converts inclusive dates to UTC bounds.
  - [x] History filter tests continue to pass without behavior changes.
  - [x] Dashboard uses the same resolver rather than a duplicate calculation.
- **Verification:**
  - [x] `dotnet test tests/pcb-deftek.Tests/ --filter HistoryFilterViewModelTests`
  - [x] `dotnet test tests/pcb-deftek.Tests/ --filter DashboardFilterViewModelTests`
  - [x] Build succeeds with 0 warnings
- **Dependencies:** Task 1.1
- **Files:**
  - `Presentation/Models/DateRangePreset.cs`
  - `Presentation/Models/DateRangePresetResolver.cs`
  - `Presentation/Models/FilterOption.cs`
  - `Presentation/ViewModels/HistoryFilterViewModel.cs`
  - `Presentation/ViewModels/DashboardFilterViewModel.cs`
  - `tests/pcb-deftek.Tests/DashboardFilterViewModelTests.cs`
  - `tests/pcb-deftek.Tests/HistoryFilterViewModelTests.cs`
- **Estimated scope:** M (7 files)

#### Task 2.2: `DashboardFilterViewModel`

- **Description:** Dashboard-specific filter state for date presets, custom range, defect types, severities, labels, and clear/reset behavior.
- **Acceptance criteria:**
  - [x] Exposes date preset, custom start/end, custom validation, selected classes, selected severities, and active-filter state.
  - [x] Provides multi-select options for all nine classes and five severities with display labels.
  - [x] Produces `DashboardFilter` with the same inclusive bounds and filter semantics as the repository contract.
  - [x] Raises a single filters-changed notification per user-visible change; `ClearAll` raises once.
  - [x] Invalid custom ranges do not produce a query filter.
- **Verification:**
  - [x] `dotnet test tests/pcb-deftek.Tests/ --filter DashboardFilterViewModelTests`
  - [x] Build succeeds with 0 warnings
- **Dependencies:** Task 2.1
- **Files:**
  - `Presentation/ViewModels/DashboardFilterViewModel.cs`
  - `tests/pcb-deftek.Tests/DashboardFilterViewModelTests.cs`
- **Estimated scope:** S (2 files)

#### Task 2.3: `DashboardViewModel` Metrics & Refresh

- **Description:** Orchestrate loading, cancellation, metrics, chart projections, layout state, card order, and defect information items.
- **Acceptance criteria:**
  - [x] Constructor depends only on interfaces and exposes `Statistics`, `MetricCards`, `DefectInfoItems`, `IsLoading`, `ErrorMessage`, `HasData`, and filter state.
  - [x] Initial navigation loads all-time statistics; filter changes cancel the prior load and reload from scratch without stale updates.
  - [x] Metric card projections expose display values, adaptive-bucket time series (scans, defects, or pass/fail), and class distribution from the aggregate result.
  - [x] Layout and card order are exposed for the view; default layout is two columns/default order until persisted preferences load.
  - [x] Card order is a stable observable list; move left/right and drag reorder mutate that list through the persistence workflow in Task 2.4.
  - [x] Defect info items come from `DefectKnowledgeBase.GetAll()` with the asset path contract from requirements §5.
  - [x] `ExportRequested` is raised with a snapshot of the active filter; no PDF is generated.
- **Verification:**
  - [x] `dotnet test tests/pcb-deftek.Tests/ --filter DashboardViewModelTests`
  - [x] Build succeeds with 0 warnings
- **Dependencies:** Tasks 1.2, 2.2
- **Files:**
  - `Presentation/ViewModels/DashboardViewModel.cs`
  - `Presentation/ViewModels/DashboardMetricCardViewModel.cs`
  - `Presentation/ViewModels/DefectInfoItemViewModel.cs`
  - `tests/pcb-deftek.Tests/DashboardViewModelTests.cs`
- **Estimated scope:** M (4 files)

#### Task 2.4: Persisted Card Order & Layout

- **Description:** Persist metric-card order and column layout in user settings so they survive page changes and restarts.
- **Acceptance criteria:**
  - [x] `UserSettings` gains `DashboardCardOrder` and `DashboardColumnLayout`; missing/default values resolve to the standard four-card order and 2 columns.
  - [x] Settings storage persists both values when a card is reordered or the layout toggle changes.
  - [x] Dashboard load restores order/layout before the first render; unrecognized card keys or invalid layout values are ignored safely.
  - [x] Navigating away/back and restarting preserves the preference.
- **Verification:**
  - [x] `dotnet test tests/pcb-deftek.Tests/ --filter DashboardViewModelTests`
  - [x] `dotnet test tests/pcb-deftek.Tests/ --filter UserSettingsServiceTests`
  - [x] Build succeeds with 0 warnings
- **Dependencies:** Task 2.3
- **Files:**
  - `Domain/Models/DashboardPreferences.cs`
  - `Domain/Models/UserSettings.cs`
  - `Infrastructure/Persistence/Repositories/UserSettingsService.cs`
  - `Presentation/ViewModels/DashboardViewModel.cs`
  - `tests/pcb-deftek.Tests/DashboardViewModelTests.cs`
- **Estimated scope:** S (5 files)

#### Task 2.5: Startup Preference & Tile Progress State

- **Description:** Wire the Dashboard startup checkbox and the notification progress modal state.
- **Acceptance criteria:**
  - [x] `SkipDashboardOnStartup` loads from `IUserSettingsService` and persists on change without blocking the UI.
  - [x] `IDashboardProgressService` exposes observable inference/export progress, status, and messages.
  - [x] Progress updates marshal to the UI thread and are safe when no data exists.
  - [x] `DashboardViewModel` exposes notification modal state and close command; idle state shows 0% without running animations.
- **Verification:**
  - [x] `dotnet test tests/pcb-deftek.Tests/ --filter DashboardViewModelTests`
  - [x] `dotnet test tests/pcb-deftek.Tests/ --filter DashboardProgressServiceTests`
  - [x] Build succeeds with 0 warnings
- **Dependencies:** Task 2.3
- **Files:**
  - `App.xaml.cs`
  - `Presentation/Services/IDashboardProgressService.cs`
  - `Presentation/Services/DashboardProgressService.cs`
  - `Presentation/ViewModels/DashboardViewModel.cs`
  - `tests/pcb-deftek.Tests/DashboardProgressServiceTests.cs`
- **Estimated scope:** S (5 files)

---

### Checkpoint 2: Dashboard State Correct
- [x] All dashboard filter/state tests pass.
- [x] History filter tests remain green after the shared resolver extraction.
- [x] Startup checkbox and progress service behave deterministically in tests.
- [x] Card order/layout persist through a settings reload.
- [ ] Review with human before chart/UI work.

---

### Task Group 3: Chart Primitives

#### Task 3.1: PASS/FAIL Line Chart

- **Description:** Lightweight WPF chart control for the adaptive dual-series PASS/FAIL rate.
- **Acceptance criteria:**
  - [x] Dependency properties for points; renders green PASS and red FAIL series using theme/status brushes.
  - [x] Handles zero, one, and many points without divide-by-zero or clipped axes.
  - [x] Hovering a point exposes a tooltip with day and both counts; accessible summary names both series.
  - [x] Click raises a chart-activated event for the enlarged modal.
  - [x] Label thinning prevents overlap at narrow widths.
- **Verification:**
  - [x] `dotnet test tests/pcb-deftek.Tests/ --filter PassFailChartTests`
  - [x] Offscreen render check at 1-column and 2-column widths
  - [x] Build succeeds with 0 warnings
- **Dependencies:** Task 1.1
- **Files:**
  - `Presentation/Controls/ChartRenderHelpers.cs`
  - `Presentation/Controls/PassFailLineChart.xaml`
  - `Presentation/Controls/PassFailLineChart.xaml.cs`
  - `tests/pcb-deftek.Tests/PassFailChartTests.cs`
- **Estimated scope:** M (3 files)

#### Task 3.2: Total PCBs / Total Defects Time-Series Chart

- **Description:** Lightweight single-series WPF chart for scan and defect volume per adaptive local bucket.
- **Acceptance criteria:**
  - [x] Dependency properties for adaptive bucket points, granularity/label metadata, series brush, and accessible series name.
  - [x] Renders inspections-per-bucket for Total PCBs and matching-defects-per-bucket for Total Defects using their card accent colors.
  - [x] Handles zero, one, and many days without divide-by-zero or clipped axes.
  - [x] Hover shows the day and count; click raises the chart-activated event; labels thin at narrow widths.
  - [x] Empty series renders a clear no-data baseline rather than a broken glyph.
- **Verification:**
  - [x] `dotnet test tests/pcb-deftek.Tests/ --filter MetricTimeSeriesChartTests`
  - [x] Offscreen render check at 1-column and 2-column widths
  - [x] Build succeeds with 0 warnings
- **Dependencies:** Task 1.1
- **Files:**
  - `Presentation/Controls/MetricTimeSeriesChart.xaml`
  - `Presentation/Controls/MetricTimeSeriesChart.xaml.cs`
  - `tests/pcb-deftek.Tests/MetricTimeSeriesChartTests.cs`
- **Estimated scope:** M (3 files)

#### Task 3.3: Defect Distribution Bar Chart

- **Description:** Lightweight WPF vertical bar chart for all nine defect classes.
- **Acceptance criteria:**
  - [x] Renders nine bars in `ClassId` order with abbreviated labels and `ColorHex` fills.
  - [x] Bars scale to the maximum non-zero count; zero counts render a visible baseline.
  - [x] Hovering a bar shows class name and count; accessible summary lists all nine values.
  - [x] Click raises a chart-activated event; labels remain readable at narrow widths.
- **Verification:**
  - [x] `dotnet test tests/pcb-deftek.Tests/ --filter DefectBarChartTests`
  - [x] Offscreen render check in Light/Dark
  - [x] Build succeeds with 0 warnings
- **Dependencies:** Task 1.1
- **Files:**
  - `Presentation/Controls/DefectBarChart.xaml`
  - `Presentation/Controls/DefectBarChart.xaml.cs`
  - `tests/pcb-deftek.Tests/DefectBarChartTests.cs`
- **Estimated scope:** M (3 files)

#### Task 3.4: Chart Modal Projection

- **Description:** Reuse all chart controls in a larger modal without duplicating data projection.
- **Acceptance criteria:**
  - [x] Card chart and enlarged chart bind to the same data.
  - [x] Modal opens from any chart click, closes via `Esc`/close, and restores focus.
  - [x] Modal has an accessible title and no new chart rendering path.
- **Verification:**
  - [x] Offscreen render of card grid and each enlarged chart
  - [x] Build succeeds with 0 warnings
- **Dependencies:** Tasks 3.1, 3.2, 3.3
- **Files:**
  - `Presentation/Controls/DashboardChartHost.xaml`
  - `Presentation/Controls/DashboardChartHost.xaml.cs`
  - `Presentation/Views/DashboardView.xaml`
  - `Presentation/Views/DashboardView.xaml.cs`
  - `tests/pcb-deftek.Tests/DashboardChartHostTests.cs`
- **Estimated scope:** S (3 files)

---

### Checkpoint 3: Charts Render Correctly
- [x] All chart controls render boundary cases.
- [x] Hover/click/accessibility behavior is verified.
- [x] Light/dark chart contrast is acceptable.
- [ ] Review with human before page composition.

---

### Task Group 4: Dashboard Page Composition

#### Task 4.1: Filter Bar, Card Grid & Layout Toggle

- **Description:** Compose Row 1 and Row 2 with the shared History filter-bar visual language and 1/2-column card grid.
- **Acceptance criteria:**
  - [x] Date presets/custom flyout, class/severity multi-selects, clear filters, count/status summary, export/notification controls, and layout toggle render correctly.
  - [x] All four metric cards render their value and supporting chart (PCBs/defects time series, PASS/FAIL line, distribution bars).
  - [x] Metric cards switch between 1-column and 2-column grids without clipping or losing chart state; the saved layout is used on first render.
  - [x] Loading, no-data, error/retry, and filtered-empty states are distinct.
  - [x] All controls are keyboard reachable and use existing Fluent tokens.
- **Verification:**
  - [ ] Offscreen render at 1100×720 and narrow width, Light/Dark
  - [x] Build succeeds with 0 warnings
- **Dependencies:** Tasks 2.3, 3.4
- **Files:**
  - `Presentation/Views/DashboardView.xaml`
  - `Presentation/Views/DashboardView.xaml.cs`
  - `Presentation/Styles/FilterBar.xaml`
  - `Presentation/Styles/Controls.xaml`
  - `Presentation/Converters/DashboardColumnLayoutToColumnsConverter.cs`
- **Estimated scope:** M (5 files)

#### Task 4.2: Drag-and-Drop, Hover & Chart Modal Interaction

- **Description:** Wire card reorder, chart hover/click, layout toggle, and filter flyout interactions.
- **Acceptance criteria:**
  - [x] Dragging a card handle reorders the four cards, updates the grid, and persists the new order; dropping outside restores order.
  - [x] Keyboard Move Left/Right commands provide a non-pointer reorder path and also persist the order.
  - [x] Chart hover shows values; clicking opens the enlarged modal and focus returns on close.
  - [x] Filter flyouts close on selection/outside click and never leave stale popup state.
- **Verification:**
  - [ ] Manual interaction: drag all four cards, toggle layout, hover/click all four charts
  - [x] Build succeeds with 0 warnings
- **Dependencies:** Task 4.1
- **Files:**
  - `Presentation/Views/DashboardView.xaml`
  - `Presentation/Views/DashboardView.xaml.cs`
- **Estimated scope:** S (2 files)

#### Task 4.3: Defect Information Accordion & Assets

- **Description:** Render the single accordion with nine defect cards using the knowledge base and `Assets/Images` assets.
- **Acceptance criteria:**
  - [x] Items ordered by `ClassId`; each shows name, severity, 1:1 image, explanation, root cause, impact, and recommended action.
  - [x] Images resolve from `Assets/Images/dashb-info-{abbreviation}.png` with lowercase abbreviations; all nine paths exist.
  - [x] Missing/corrupt images show the placeholder and log a warning without crashing.
  - [x] Accordion is keyboard accessible and preserves text selection.
  - [x] No hardcoded absolute asset paths.
- **Verification:**
  - [x] `dotnet test tests/pcb-deftek.Tests/ --filter DefectInfoAssetTests`
  - [ ] Offscreen render of expanded/collapsed accordion
  - [x] Build succeeds with 0 warnings
- **Dependencies:** Task 4.1
- **Files:**
  - `Presentation/Views/DashboardView.xaml`
  - `Presentation/Converters/DefectAbbreviationToImagePathConverter.cs`
  - `Assets/Images/dashb-info-*.png`
  - `pcb-deftek.csproj`
  - `tests/pcb-deftek.Tests/DefectInfoAssetTests.cs`
- **Estimated scope:** S (3 files)

#### Task 4.4: Notification Modal & Progress Presentation

- **Description:** Compose the notification modal bound to `IDashboardProgressService`.
- **Acceptance criteria:**
  - [x] Modal shows inference and data-export bars with Idle/Running/Completed/Failed status.
  - [x] Opens from the notification action; closes via `Esc`/close; no fake timers.
  - [x] Bars expose accessible names and valid 0–100 ranges.
  - [x] Layout remains readable at narrow widths and in both themes.
- **Verification:**
  - [x] Offscreen render of idle/running/completed states
  - [x] Build succeeds with 0 warnings
- **Dependencies:** Tasks 2.4, 4.1
- **Files:**
  - `Presentation/Views/DashboardView.xaml`
  - `Presentation/Views/DashboardView.xaml.cs`
- **Estimated scope:** S (2 files)

---

### Checkpoint 4: Dashboard Page Usable
- [x] Filters, cards, charts, accordion, modals, and layout toggle render correctly.
- [ ] Keyboard and pointer paths are verified.
- [ ] Light/dark and narrow-width layouts have no clipped content.
- [ ] Review with human before integration work.

---

### Task Group 5: Actions, Startup & Integration

#### Task 5.1: Export Event & Startup Persistence

- **Description:** Complete the action seams for Phase 9 PDF export and the existing startup preference.
- **Acceptance criteria:**
  - [x] Export Data command raises `ExportRequested` with an immutable filter snapshot; repeated clicks are safe.
  - [x] Startup checkbox loads/persists through `IUserSettingsService` and reports failures without crashing.
  - [x] No placeholder PDF file or fake completion toast is produced.
  - [x] Notification action opens the progress modal regardless of operation state.
- **Verification:**
  - [x] `dotnet test tests/pcb-deftek.Tests/ --filter DashboardViewModelTests`
  - [x] Build succeeds with 0 warnings
- **Dependencies:** Tasks 2.4, 4.4
- **Files:**
  - `Presentation/ViewModels/DashboardViewModel.cs`
  - `tests/pcb-deftek.Tests/DashboardViewModelTests.cs`
- **Estimated scope:** S (2 files)

#### Task 5.2: DI Registration & Resolution

- **Description:** Register dashboard repository, view model, progress service, and chart dependencies; assert lifetimes.
- **Acceptance criteria:**
  - [x] `IDashboardRepository` and `IDashboardProgressService` are singletons; `DashboardViewModel` is transient.
  - [x] `DashboardViewModel` resolves with all dependencies.
  - [x] Existing DI tests remain green.
- **Verification:**
  - [x] `dotnet test tests/pcb-deftek.Tests/ --filter DependencyInjectionTests`
  - [x] Build succeeds with 0 warnings
- **Dependencies:** Tasks 1.2, 2.4
- **Files:**
  - `App.xaml.cs`
  - `tests/pcb-deftek.Tests/DependencyInjectionTests.cs`
- **Estimated scope:** S (2 files)

---

### Checkpoint 5: Actions & Integration Complete
- [x] Export seam and startup persistence pass tests.
- [x] DI resolves the dashboard in the real provider.
- [x] No production stub or fake export remains.
- [ ] Review with human before BDD.

---

### Task Group 6: BDD, Documentation & Final Gates

#### Task 6.1: Dashboard BDD Scenarios

- **Description:** Add Reqnroll acceptance coverage for filters, metrics, layout/reorder, accordion assets, progress modal, and startup preference.
- **Acceptance criteria:**
  - [x] Isolated SQLite database with seeded inspections/defects across dates, classes, and severities.
  - [x] Scenarios assert totals, adaptive chart buckets, filter composition, persisted layout/reorder, all nine accordion assets, progress idle state, and persisted startup preference.
  - [x] No dependency on the developer's real app data or asset downloads.
- **Verification:**
  - [x] `dotnet test tests/pcb-deftek.Bdd/ --filter DashboardFeature`
  - [x] Full suite passes: `dotnet test pcb-deftek.slnx`
  - [x] Build succeeds with 0 warnings
- **Dependencies:** Task Group 5
- **Files:**
  - `tests/pcb-deftek.Bdd/Features/DefectStatisticsDashboard.feature`
  - `tests/pcb-deftek.Bdd/StepDefinitions/DashboardStepDefinitions.cs`
- **Estimated scope:** M (2 files)

#### Task 6.2: Documentation & Plan Status

- **Description:** Record completion, deviations, and final verification.
- **Acceptance criteria:**
  - [x] Plan checkboxes reflect actual state; deferred items remain in requirements.
  - [ ] Validation captures manual Light/Dark, DPI, drag/hover checks.
  - [x] No unchecked task is silently skipped.
- **Verification:**
  - [x] `git diff` shows only status/documentation edits in this file
  - [x] Build succeeds with 0 warnings
- **Dependencies:** Task 6.1
- **Files:**
  - `specs/2026-11-14-defect-statistics-dashboard/plan.md`
  - `specs/2026-11-14-defect-statistics-dashboard/validation.md`
- **Estimated scope:** XS (2 files)

---

### Checkpoint 6: Merge Readiness
- [ ] All six task groups complete.
- [x] Solution builds with 0 warnings.
- [x] All unit, integration, and BDD tests pass (`dotnet test pcb-deftek.slnx`).
- [x] PR linked to its issue and passes the 4-step Feature Review and Quality Improvement pipeline.
- [ ] Human review approved; PR not merged without approval.

---

## Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Severity is metadata, not a SQL column | High | Map selected severities to class codes through `DefectKnowledgeBase`; test all five severities |
| Filter semantics silently diverge from History | High | Shared date-range resolver + parity tests |
| Chart axes divide by zero on empty/single-point data | Medium | Chart projection tests for 0/1/many points |
| No chart library leads to fragile custom drawing | Medium | Small focused controls with pure projection tests and offscreen renders |
| Drag/drop conflicts with click-to-open chart | Medium | Drag handle only; click threshold; keyboard move commands |
| Defect image assets missing/renamed | Medium | Asset-path contract test for all nine files; placeholder fallback |
| Class/severity filters zero out PASS series | Low | Documented metric definition; no misleading normalization |
| Progress modal becomes fake UI | Medium | `IDashboardProgressService` with idle/running/completed/failed test states |
| PDF button implies export exists | Medium | Event seam documented as Phase 9; no fake files/toasts |
| Layout selected state not notified | Low | View-model notification tests for both grid modes |

## Open Questions

None. Card order/layout persistence (D10/D11), permanent Dashboard startup checkbox (D14), and adaptive bucketing (D18) are resolved.
