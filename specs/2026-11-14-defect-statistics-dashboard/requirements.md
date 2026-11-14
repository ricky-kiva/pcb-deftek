# Requirements & Context: Phase 8 — Defect Statistics Dashboard

## 1. Executive Summary & Context

Phase 8 replaces the Dashboard placeholder with the product's analytics surface. It turns persisted inspection and defect rows into four interactive metric cards, filter-driven charts, and a defect knowledge accordion.

This phase builds directly on:

- `specs/roadmap.md` — Phase 8: Defect Statistics Dashboard.
- `specs/mission.md` — Statistics Dashboard scope (Section 5.1) and plant/production manager audience.
- `specs/tech-stack.md` — WPF Fluent UI, SQLite repository, MVVM purity, async I/O, no hardcoded user paths.
- Completed Phase 7 — `InspectionRecord`, `InspectionSummary`, `IInspectionRepository`, defect class metadata/colors, and the History filter patterns.
- `Assets/Images/` — nine pre-existing defect knowledge images.

### Current State (verified in code)

| Area | Current state | Phase 8 impact |
|---|---|---|
| `DashboardViewModel` | Title-only placeholder | Full analytics view model |
| `DashboardView.xaml` | Centered "Phase 8 delivery" panel | Full dashboard layout |
| Repository aggregates | Only paged record/summary queries and total count | Add filtered dashboard aggregate query |
| Chart library | None in the project | Build lightweight WPF chart primitives; no new package |
| Defect metadata | `DefectKnowledgeBase` already carries display name, severity, explanation, root cause, impact, action, and `ColorHex` | Reuse directly; do not duplicate taxonomy |
| Defect images | `Assets/Images/dashb-info-{abbreviation}.png` exist for all nine classes | Wire into the defect information accordion |
| Dashboard filters | No dashboard filter state | Reuse the History date-preset behavior and add severity multi-select |
| PDF engine | Not implemented (Phase 9) | Dashboard export button exposes a filter-bearing command/event seam only |
| Startup preference | `SkipDashboardOnStartup` persists but has no UI after the Settings cleanup | Re-expose it in Dashboard Row 4 per roadmap |

---

## 2. Scope

### 2.1 In Scope

1. **Filter/action bar (Row 1)**
   - Date Range Selector: Day / Week / Month + Custom range; default all time.
   - Defect Type multi-select across all nine classes.
   - Defect Severity multi-select across all five severities.
   - PDF Export Data action (event seam for Phase 9).
   - Notification action opening a progress modal for inference and data export.
   - 1-column / 2-column metric-card layout toggle.
2. **Metric cards (Row 2)**
   - Total PCBs Inspected, with a per-time-unit scan volume chart.
   - Total Defects Detected, with a per-time-unit defect volume chart.
   - PASS/FAIL Inspection Rate dual-line chart.
   - Defect Distribution Across Defect Classes vertical bar chart.
   - Drag-and-drop card reordering (session-only).
   - Click a chart to open an enlarged chart modal.
   - Hover feedback on chart data points.
3. **Defect Class Information (Row 3)**
   - One accordion containing all nine defect cards.
   - Each card: class name, severity, 1:1 image, explanation, root cause, impact, recommended action.
4. **Startup preference (Row 4)**
   - "Skip dashboard on startup" checkbox, persisted through `IUserSettingsService`.
5. **Data and presentation enablers**
   - Dashboard aggregate contract and SQL query.
   - Dashboard filter/state view models.
   - Custom WPF line/bar chart controls with accessible names.
   - Progress state for the notification modal.
6. **Verification**
   - Unit tests for aggregate queries, filter composition, view-model state, chart projection, and asset paths.
   - Reqnroll BDD scenarios for the primary dashboard flow.
   - DI registrations and full build/test gates.

### 2.2 Out of Scope (Deferred)

- Actual PDF generation and report layout (Phase 9). Phase 8 only exposes the filtered export command/event.
- Persisting metric-card order across restarts; reordering is session-only.
- Drill-down navigation from a chart point into History.
- Custom chart types beyond the specified line, time-series column, and class-distribution bar charts.
- Exporting dashboard data to CSV/Excel.
- Dashboard live-refresh while the user is on another page.
- Redesigning the Defect Knowledge Base content or palette.

---

## 3. Decisions & Rationale

| # | Decision | Rationale |
|---|---|---|
| D1 | Add a dedicated dashboard aggregate contract/query rather than loading records into the view model | Keeps aggregation in SQLite and avoids loading defect rows for analytics. |
| D2 | Dashboard query is separate from `IInspectionRepository` (`IDashboardRepository` + `SqliteDashboardRepository`) | Interface segregation; history paging and analytics evolve independently. |
| D3 | Date presets use local calendar bounds converted to UTC, matching History | Consistent user expectations across pages. |
| D4 | Defect Type filter uses `DefectClass`; Severity filter maps selected severities to the class codes in `DefectKnowledgeBase` and applies `Defects.ClassCode IN (...)` | Severity is metadata, not stored in SQLite; mapping from the single knowledge source avoids schema changes. |
| D5 | When class/severity filters are active, an inspection is included only if it has at least one defect matching both active sets; PASS inspections are excluded because they have no defects | One consistent filter scope across all four cards. |
| D6 | Total PCBs = included inspections; Total Defects = matching defect rows; each is also projected as a per-local-day time series for its card chart. Distribution = matching defects grouped by class with all nine classes represented (zero included); PASS/FAIL chart = included inspections grouped by local day | Explicit, testable metric definitions; every card carries a visualization rather than a standalone number. |
| D7 | PASS/FAIL uses a dual-series line chart; Total PCBs and Total Defects use single-series time charts over the same local-day buckets; Distribution uses all nine classes in taxonomy order | Keeps all four cards visual, avoids fabricated empty-day points between recorded days, and keeps class comparison complete. |
| D8 | Build line/bar charts with WPF shapes and custom controls; no chart NuGet package | The project has no chart dependency; adding one requires approval and is unnecessary for four fixed cards. |
| D9 | Metric cards share a common card shell; chart content is composed via templates, not a single over-configured card control | Keeps card structure consistent and chart rendering focused. |
| D10 | Drag-and-drop reorders the four cards in memory only for the session | Roadmap does not require persistence; avoids expanding user settings/schema. |
| D11 | 2-column layout is the default; 1-column layout stacks cards at full width | Preserves the dashboard's analytics density on desktop while supporting narrow/windowed use. |
| D12 | PDF Export command raises an `ExportRequested` event carrying a snapshot of the active filter; Phase 9 subscribes and generates the report | Provides a real integration seam without shipping a fake PDF. |
| D13 | Notification modal binds to `IDashboardProgressService` state for inference and data-export operations; Phase 8 wires the service and displays idle/running/completed states | Keeps progress presentation testable and decoupled from the pages that produce progress. |
| D14 | Startup checkbox reads/writes the existing `SkipDashboardOnStartup` setting | Roadmap requests the preference on Dashboard after its removal from Settings. |
| D15 | Defect information images resolve from `Assets/Images/dashb-info-{abbreviation}.png` using lowercase abbreviations | Matches the nine existing assets exactly, independent of display-name spelling. |
| D16 | Missing/corrupt defect images render the existing image placeholder; they never throw | Consistent graceful-degradation policy. |
| D17 | No new NuGet packages and no database schema changes in Phase 8 | The feature is aggregation and presentation over existing tables. |
| D18 | The time unit for the Total PCBs and Total Defects charts is the local calendar day for the active date range | Matches grouping elsewhere in History and the PASS/FAIL chart; adaptive week/month bucketing is deferred until large-range performance requires it. |

---

## 4. Metric Definitions

Let **base inspections** be inspections inside the selected date range, further restricted as follows:

- No class or severity selection: all inspections in the date range.
- One or both selections active: only inspections containing at least one defect whose class is in the selected class set (or any class if none selected) **and** whose severity is in the selected severity set (or any severity if none selected).

| Metric | Definition |
|---|---|
| Total PCBs Inspected | Count of base inspections, plus a per-local-day series of inspection counts for the card chart. |
| Total Defects Detected | Count of defects belonging to base inspections that match the active class/severity sets, plus a per-local-day series of matching defect counts for the card chart. |
| PASS/FAIL Inspection Rate | Per local calendar day, count base inspections with zero defects (PASS) and with one or more defects (FAIL). With defect filters active, the PASS series is naturally zero. |
| Defect Distribution | Matching defects grouped by class, ordered `ClassId`, all nine classes present. |

The date filter is inclusive. Custom range start > end is rejected and does not query.

---

## 5. Defect Information Asset Contract

The Dashboard Defect Information accordion MUST use the existing files under `Assets/Images/`:

| Class | Abbreviation | Asset |
|---|---|---|
| Short | SH | `Assets/Images/dashb-info-sh.png` |
| Open | OP | `Assets/Images/dashb-info-op.png` |
| Mouse bite | MB | `Assets/Images/dashb-info-mb.png` |
| Spur | SP | `Assets/Images/dashb-info-sp.png` |
| Spurious copper | SC | `Assets/Images/dashb-info-sc.png` |
| Hole breakout | HB | `Assets/Images/dashb-info-hb.png` |
| Conductor scratch | CS | `Assets/Images/dashb-info-cs.png` |
| Conductor foreign object | CFO | `Assets/Images/dashb-info-cfo.png` |
| Base material foreign object | BMFO | `Assets/Images/dashb-info-bmfo.png` |

- File name pattern: `dashb-info-{defect_name_abbreviation}.png`.
- Abbreviations are lowercase in the filename.
- Resolve through a pack URI or repository-relative asset path; do not hardcode a machine-specific absolute path.
- Images render as 1:1 squares, `UniformToFill`, clipped; missing files show the placeholder and log a warning.

---

## 6. UI Contract

### 6.1 Row 1 — Filters & Actions

- Date: Day / Week / Month toggles + Custom flyout, identical interaction semantics to History.
- Defect Type: multi-select flyout with all nine classes.
- Severity: multi-select flyout with Critical, High, Medium-High, Medium, Low-Medium.
- Clear filters resets date, class, and severity selections and reloads.
- Actions: **Export Data**, **Notifications**, and a segmented **1-column / 2-column** layout toggle.
- Filters compose with AND across categories and OR within a category.

### 6.2 Row 2 — Metric Cards

- Four cards in the chosen order; each card has a compact title, primary value, and supporting visualization/caption.
- **Total PCBs:** count + period label + single-series per-local-day scan volume chart.
- **Total Defects:** count + period label + single-series per-local-day matching-defect volume chart.
- **PASS/FAIL Rate:** dual-line chart (green PASS, red FAIL) with day labels and hover values.
- **Defect Distribution:** vertical bars with abbreviated labels, class colors from `DefectClassInfo.ColorHex`, hover counts.
- Drag handle on each card; drag reorders within the grid; keyboard alternative via Move Left/Right commands or menu.
- Clicking any chart (PCBs, Defects, PASS/FAIL, Distribution) opens a larger modal chart; `Esc` and the close button dismiss it.

### 6.3 Row 3 — Defect Class Information

- One accordion, initially collapsed or first item expanded.
- Nine items from `DefectKnowledgeBase.GetAll()` ordered by `ClassId`.
- Each item uses the asset contract in Section 5.
- Severity is shown with text and color token; explanation/root cause/impact/action remain selectable text.

### 6.4 Row 4 — Startup Preference

- "Skip dashboard on startup" checkbox bound to the persisted setting.
- Saving the checkbox persists immediately or on toggle without closing the page.
- The checkbox state survives restart.

### 6.5 Notification Modal

- Displays two labeled progress bars: **Inference** and **Data export**.
- Shows Idle / Running / Completed / Failed status and a percentage when known.
- Accessible name for the dialog and each bar; close on `Esc` or close button.
- No fake progress animation; values come from `IDashboardProgressService`.

---

## 7. Technical Constraints

- MVVM: no `System.Windows.Controls` in view models; chart rendering belongs in controls/converters.
- All queries and settings persistence asynchronous and cancellable.
- Existing Fluent theme tokens and `DefectClassInfo.ColorHex` only; no new palette.
- Chart controls must expose accessible summaries and remain readable in Light/Dark themes.
- No new NuGet packages; no schema migrations in Phase 8.
- Reuse existing `HistoryFilterViewModel` date logic where practical, without coupling the pages.

---

## 8. Edge Cases

| Case | Expected behavior |
|---|---|
| No inspections in range | Zero values; charts show empty-state copy, no error |
| Custom start after end | Validation message; no query |
| Class/severity filter selects no matching defects | Zero metrics and no-matches chart state |
| All nine classes selected | Equivalent to no class filter when combined with no severity filter |
| Missing defect image | Placeholder; accordion still usable |
| Chart with one data point | Line renders a visible point/segment without divide-by-zero |
| Chart with many days | Chart remains readable; horizontal labels thin out |
| Window too narrow for two columns | Layout toggle available; card content wraps without clipping |
| Drag card over non-drop target | No reorder; original order retained |
| Notification modal while idle | Both bars at 0% with Idle status; no timer runs |
| PDF export clicked before Phase 9 | Command raises `ExportRequested`; no file is fabricated |

---

## 9. Open Questions

1. Should card order persist across restarts? Phase 8 default: session-only (D10).
2. Should the startup checkbox remain on Dashboard long-term, or be removed entirely? Phase 8 follows the roadmap (D14).
3. Should the time-series cards bucket by day when the date range spans years? Phase 8 uses local-day buckets (D18); adaptive bucketing is deferred.
