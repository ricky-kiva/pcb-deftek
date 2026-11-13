# Implementation Plan: Phase 7 — History & Interactive Defect Viewer

## Overview

Phase 7 turns persisted inspections into a **date-grouped history gallery** with inline renaming and a **high-performance interactive defect viewer** featuring pan/zoom, click-to-focus bounding boxes, split default/focused informational sidebar states, viewport cropping to PNG, and graceful handling of missing source images.

> **Task list target:** tasks are tracked directly in this `plan.md`, per the repository convention in `AGENTS.md` §1 (feature specs use `plan.md`, `requirements.md`, `validation.md`). No separate `tasks/todo.md` is used.

---

## Architecture & Dependency Graph

```
┌─────────────────────────────────────────────────────────────┐
│ Domain / Contracts                                          │
│  • InspectionSummary (Id, Name, timestamps, Mode,           │
│    DefectCount, ImagePath, Status)                          │
│  • DefectClassInfo.ColorHex (single palette source)         │
└─────────────────────────────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────┐
│ Infrastructure                                              │
│  • IInspectionRepository.GetSummariesPagedAsync             │
│  • IHistoryThumbnailService / WPF thumbnail decoder         │
│  • IViewportCropService / OpenCvSharp crop exporter         │
│  • KnownFolderPaths (shared Downloads resolution)           │
└─────────────────────────────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────┐
│ Presentation — ViewModels                                   │
│  • HistoryViewModel (paging, grouping, refresh, viewer)     │
│  • HistoryItemViewModel (thumbnail, rename state)           │
│  • InspectionViewerViewModel (record, markers, sidebar)     │
│  • ViewportTransform (pure zoom/pan/crop math)              │
└─────────────────────────────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────┐
│ Presentation — Views                                        │
│  • HistoryView.xaml (grouped gallery, inline rename)        │
│  • DefectViewerView.xaml (modal overlay + sidebar)          │
│  • DefectImageViewer control (wheel zoom, drag pan, focus)  │
└─────────────────────────────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────┐
│ Integration & Verification                                  │
│  • DI registrations + DependencyInjectionTests              │
│  • Reqnroll BDD scenarios                                   │
│  • Full build / test gates                                  │
└─────────────────────────────────────────────────────────────┘
```

Data flow: SQLite → `InspectionSummary` page → `HistoryItemViewModel` (thumbnail decode) → gallery card → `InspectionViewerViewModel` loads full record via `GetByIdAsync` → markers from normalized boxes → view renders transforms → crop service reads source file at native resolution.

---

## Task Breakdown

### Task Group 1: History Data Projection & Grouping

#### Task 1.1: `InspectionSummary` Projection & Repository Query

- **Description:** Add an additive read model that carries the stored `DefectCount`, and a repository query that returns it without loading defect rows. `GetPagedAsync` remains unchanged for existing callers.
- **Acceptance criteria:**
  - [ ] `InspectionSummary` exposes `Id`, `Name`, `CreatedAtUtc`, `UpdatedAtUtc`, `Mode`, `DefectCount`, `ImagePath`, plus computed `Status`/`IsPass`.
  - [ ] `GetSummariesPagedAsync(InspectionQueryFilter)` returns `PagedResult<InspectionSummary>` selecting `DefectCount` from `Inspections`.
  - [ ] Honors `StartDateUtc`, `EndDateUtc`, `Status`, `Mode`, `PageNumber`/`PageSize`; orders by `CreatedAtUtc DESC, Id DESC`.
  - [ ] Exactly one SQL query for counts and one for the page; no per-item defect queries.
- **Verification:**
  - [ ] `dotnet test tests/pcb-deftek.Tests/ --filter InspectionSummaryTests`
  - [ ] Existing `SqliteRepositoryIntegrationTests` still pass
  - [ ] Build succeeds with 0 warnings
- **Dependencies:** None
- **Files:**
  - `Domain/Contracts/InspectionSummary.cs`
  - `Infrastructure/Persistence/Repositories/IInspectionRepository.cs`
  - `Infrastructure/Persistence/Repositories/SqliteInspectionRepository.cs`
  - `tests/pcb-deftek.Tests/InspectionSummaryTests.cs`
- **Estimated scope:** S (4 files)

#### Task 1.2: Local-Date Grouping Helper

- **Description:** Pure grouping utility that converts summaries into ordered local-date groups for the gallery, independent of WPF collection views.
- **Acceptance criteria:**
  - [ ] Groups by `CreatedAtUtc.ToLocalTime().Date`, groups ordered newest-first.
  - [ ] Items inside each group preserve repository order (stable).
  - [ ] Group exposes a culture-aware display label and a `Date` key.
  - [ ] Empty input returns an empty collection; duplicate timestamps do not merge or drop items.
- **Verification:**
  - [ ] `dotnet test tests/pcb-deftek.Tests/ --filter HistoryGroupingTests`
  - [ ] Build succeeds with 0 warnings
- **Dependencies:** Task 1.1
- **Files:**
  - `Presentation/ViewModels/HistoryDateGroup.cs`
  - `tests/pcb-deftek.Tests/HistoryGroupingTests.cs`
- **Estimated scope:** XS (2 files)

---

### Checkpoint 1: Correct History Data
- [ ] Summaries report accurate PASS/FAIL counts without loading defects.
- [ ] Grouping matches local calendar dates and is deterministic.
- [ ] Build and focused tests are green.

---

### Task Group 2: Gallery ViewModel & Thumbnails

#### Task 2.1: Reusable Name Editor & `HistoryItemViewModel`

- **Description:** Per-card view model wrapping an `InspectionSummary`, its lazy thumbnail, and inline rename — built on a small reusable `InspectionNameEditor` state object so the viewer does not duplicate the edit/validate/revert logic.
- **Acceptance criteria:**
  - [ ] `InspectionNameEditor` implements `BeginRename`, `CancelRename`, `CommitRenameAsync(IInspectionRepository)`, trim/empty rejection, and revert-on-failure; it owns no UI types.
  - [ ] `HistoryItemViewModel` exposes `Name`, local `DisplayDate`/`DisplayTime`, `StatusText` (`PASS`/`FAIL`), `IsPass`, `ModeText`, `DefectCount`, `Thumbnail`, `HasThumbnail`, `IsImageUnavailable`.
  - [ ] `BeginRename()` seeds `EditedName` and enters edit mode; `CancelRename()` restores the persisted name.
  - [ ] Committing an empty/whitespace name reverts without a write; a successful commit persists via `UpdateNameAsync` and updates `Name`; `false`/exception reverts and surfaces failure.
  - [ ] Rename does not touch `ImagePath` or any file on disk.
- **Verification:**
  - [ ] `dotnet test tests/pcb-deftek.Tests/ --filter HistoryItemViewModelTests`
  - [ ] Build succeeds with 0 warnings
- **Dependencies:** Task 1.1
- **Files:**
  - `Presentation/ViewModels/InspectionNameEditor.cs`
  - `Presentation/ViewModels/HistoryItemViewModel.cs`
  - `tests/pcb-deftek.Tests/HistoryItemViewModelTests.cs`
- **Estimated scope:** S (3 files)

#### Task 2.2: `IHistoryThumbnailService` & WPF Decoder

- **Description:** Background thumbnail decoder producing frozen square-ready previews from `ImagePath`, with bounded concurrency and cancellation.
- **Acceptance criteria:**
  - [ ] Decodes with `DecodePixelWidth = 240`, `BitmapCacheOption.OnLoad`, then `Freeze()`; no UI-thread file I/O.
  - [ ] Returns `null` for missing, corrupt, or unreadable files without throwing.
  - [ ] Concurrency capped (e.g., 4 concurrent decodes) and honors `CancellationToken`.
  - [ ] Rejects empty paths via argument validation.
- **Verification:**
  - [ ] `dotnet test tests/pcb-deftek.Tests/ --filter HistoryThumbnailServiceTests`
  - [ ] Build succeeds with 0 warnings
- **Dependencies:** None
- **Files:**
  - `Infrastructure/Services/IHistoryThumbnailService.cs`
  - `Infrastructure/Services/HistoryThumbnailService.cs`
  - `tests/pcb-deftek.Tests/HistoryThumbnailServiceTests.cs`
- **Estimated scope:** S (3 files)

#### Task 2.3: `HistoryViewModel` Gallery State

- **Description:** Replace the placeholder with the gallery orchestration: initial page, load-more, grouping, refresh on navigation, thumbnail hydration, rename delegation, and cancellation.
- **Acceptance criteria:**
  - [ ] Constructor depends only on interfaces (`IInspectionRepository`, `IHistoryThumbnailService`, `INotificationService`, `ILogger<HistoryViewModel>`).
  - [ ] `OnNavigatedToAsync` loads page 1 (size 60), clears previous state, and hydrates thumbnails asynchronously.
  - [ ] `LoadMoreCommand` appends the next page without duplicate IDs and stops at `TotalItems`; commands are disabled while loading.
  - [ ] Exposes `Groups`, `Items`, `IsLoading`, `HasMore`, `IsEmpty`, `ErrorMessage`, and `TotalCount`.
  - [ ] `RefreshCommand` reloads from page 1.
  - [ ] `OnNavigatedFromAsync` cancels in-flight loads/cancels thumbnails; late completions do not mutate state.
  - [ ] Empty result produces the empty state, not an error.
- **Verification:**
  - [ ] `dotnet test tests/pcb-deftek.Tests/ --filter HistoryViewModelTests`
  - [ ] Build succeeds with 0 warnings
- **Dependencies:** Tasks 1.1, 1.2, 2.1, 2.2
- **Files:**
  - `Presentation/ViewModels/HistoryViewModel.cs`
  - `tests/pcb-deftek.Tests/HistoryViewModelTests.cs`
- **Estimated scope:** M (2 files)

---

### Checkpoint 2: Gallery Behaviour
- [ ] Paging is duplicate-free and bounded by `TotalItems`.
- [ ] Renames persist and revert correctly on failure.
- [ ] Thumbnails load off-thread and degrade to placeholders.
- [ ] Navigation away cancels all work.

---

### Task Group 3: History Gallery View

#### Task 3.1: Grouped Gallery Layout & Card Template

- **Description:** Build the History page: header/count, loading/empty/error states, date-grouped cards, load-more footer.
- **Acceptance criteria:**
  - [ ] Uses a `ListBox` + `CollectionViewSource` grouping with `VirtualizingPanel.IsVirtualizingWhenGrouping="True"` and item recycling.
  - [ ] Date header renders the group label; cards render in a wrapping gallery layout.
  - [ ] Card has a clipped 1:1 preview (`Stretch="UniformToFill"`), PASS/FAIL badge in the top-left using `StatusPass*`/`StatusFail*` brushes, name, local timestamp, mode, and defect count.
  - [ ] Missing thumbnail shows `IconImage` placeholder; no layout jump.
  - [ ] Load-more button visible only when `HasMore`; disabled while loading.
  - [ ] Empty state includes guidance to run File or Live detection.
  - [ ] Reuses existing Fluent theme tokens only; no new hardcoded colors.
- **Verification:**
  - [ ] View compiles and renders with sample data
  - [ ] Manual check at 100% and 150% DPI, Light and Dark themes
  - [ ] Build succeeds with 0 warnings
- **Dependencies:** Task 2.3
- **Files:**
  - `Presentation/Views/HistoryView.xaml`
  - `Presentation/Views/HistoryView.xaml.cs`
- **Estimated scope:** M (2 files)

#### Task 3.2: Inline Rename & Card Interaction

- **Description:** Wire double-click-to-rename, Enter/Escape/lost-focus semantics, keyboard open, and viewer launch from a card.
- **Acceptance criteria:**
  - [ ] Double-clicking the name enters edit mode with the text selected and focused.
  - [ ] Enter commits; Escape cancels; losing focus commits (no duplicate commits).
  - [ ] Empty/whitespace input reverts without a DB write.
  - [ ] Enter/Space on a focused card opens the viewer; `F2` starts rename.
  - [ ] Clicking a card (outside edit mode) opens the viewer for that record.
  - [ ] Focus visuals remain visible in both themes.
- **Verification:**
  - [ ] Manual interaction check (mouse + keyboard)
  - [ ] `dotnet test tests/pcb-deftek.Tests/ --filter HistoryViewModelTests` (rename/edit state)
  - [ ] Build succeeds with 0 warnings
- **Dependencies:** Task 3.1
- **Files:**
  - `Presentation/Views/HistoryView.xaml`
  - `Presentation/Views/HistoryView.xaml.cs`
- **Estimated scope:** S (2 files)

---

### Checkpoint 3: History Page Usable
- [ ] A user can browse, group, rename, and launch the viewer from the gallery.
- [ ] No UI stalls while scrolling or loading thumbnails.
- [ ] Theme and DPI checks pass.

---

### Task Group 4: Viewer Logic & Crop Pipeline

#### Task 4.1: Shared Defect Palette

- **Description:** Make the defect color table a single source usable by both OpenCV annotation and WPF overlays.
- **Acceptance criteria:**
  - [ ] `DefectClassInfo.ColorHex` returns the established color for all nine classes (e.g. `SH` blue, `OP` fern, `SC` white).
  - [ ] `ImageAnnotationService.GetDefectColor` derives its BGR `Scalar` from `ColorHex`; rendered output is unchanged and the existing exact-BGR color theory in `ImageAnnotationServiceTests` passes without modification.
  - [ ] A WPF converter turns `ColorHex` into a frozen `SolidColorBrush` with no WPF types in Domain/Infrastructure.
  - [ ] All nine classes produce distinct, valid `#RRGGBB` values.
- **Verification:**
  - [ ] `dotnet test tests/pcb-deftek.Tests/ --filter DefectPaletteTests`
  - [ ] Existing `ImageAnnotationServiceTests` pass unchanged
  - [ ] Build succeeds with 0 warnings
- **Dependencies:** None
- **Files:**
  - `Domain/Models/DefectClassInfo.cs`
  - `Infrastructure/Vision/ImageAnnotationService.cs`
  - `Presentation/Converters/HexColorToBrushConverter.cs`
  - `tests/pcb-deftek.Tests/DefectPaletteTests.cs`
- **Estimated scope:** S (4 files)

#### Task 4.2: `ViewportTransform` Pure Math

- **Description:** Extract all zoom/pan/focus/crop geometry into a pure, testable model used by the viewer control and view model.
- **Acceptance criteria:**
  - [ ] Computes fit scale/offset from image and viewport sizes (letterboxed, centered).
  - [ ] `ZoomAt(point, factor)` keeps the image point under the cursor stationary and clamps to `0.5×–10×` of fit.
  - [ ] `Pan(dx, dy)` clamps so no viewport edge exposes empty space beyond the image.
  - [ ] `FocusOn(normalizedRect, padding)` returns a transform that centers the defect at a bounded zoom.
  - [ ] `GetVisibleNormalizedRect()` returns the visible image region clamped to `[0,1]`, suitable for cropping.
  - [ ] Reset returns the exact fit transform.
- **Verification:**
  - [ ] `dotnet test tests/pcb-deftek.Tests/ --filter ViewportTransformTests`
  - [ ] Build succeeds with 0 warnings
- **Dependencies:** None
- **Files:**
  - `Presentation/ViewModels/ViewportTransform.cs`
  - `tests/pcb-deftek.Tests/ViewportTransformTests.cs`
- **Estimated scope:** M (2 files)

#### Task 4.3: `InspectionViewerViewModel`

- **Description:** Viewer state: record loading, markers, sidebar states, rename, crop command, and close.
- **Acceptance criteria:**
  - [ ] Loads the full record with defects via `IInspectionRepository.GetByIdAsync` and raises an unavailable state when the image cannot be decoded.
  - [ ] Exposes `ImageSource`, `DefectMarkers` (normalized + pixel rects), `SelectedMarker`, `IsDefectFocused`, `ClassSummaries`, `FormattedTimestamp`, `CurrentName`, and `IsLoading`.
  - [ ] Focus state exposes class name, confidence, severity, root cause, impact, and recommended action from `DetectedDefect`.
  - [ ] Default state summary groups defects by class with count and highest confidence, ordered deterministically.
  - [ ] `CropViewportCommand(NormalizedRect)` calls `IViewportCropService`, emits a completion toast with the full path, and reports failures without crashing.
  - [ ] `CloseCommand` clears selection and record state.
  - [ ] Rename reuses `InspectionNameEditor` from Task 2.1 (no duplicated edit/validate/revert logic).
- **Verification:**
  - [ ] `dotnet test tests/pcb-deftek.Tests/ --filter InspectionViewerViewModelTests`
  - [ ] Build succeeds with 0 warnings
- **Dependencies:** Tasks 4.1, 4.2, plus rename logic from Task 2.1
- **Files:**
  - `Presentation/ViewModels/InspectionViewerViewModel.cs`
  - `tests/pcb-deftek.Tests/InspectionViewerViewModelTests.cs`
- **Estimated scope:** M (2 files)

#### Task 4.4: `IViewportCropService` & Shared Downloads Path

- **Description:** Crop the currently visible normalized region from the source image at native resolution and export it as PNG.
- **Acceptance criteria:**
  - [ ] `KnownFolderPaths.GetDownloadsDirectory()` contains the existing Downloads resolution/fallback; `SqliteDatabaseExportService` delegates to it with unchanged behavior.
  - [ ] Crop clamps the normalized rect to `[0,1]`, converts to native pixels, rejects empty results, and writes a PNG.
  - [ ] Output name is sanitized `{recordName}_crop_{yyyyMMdd-HHmmss}.png` with a numeric suffix on collision; no overwrites.
  - [ ] Honors an optional directory override for tests; defaults to Downloads.
  - [ ] Throws/returns a typed failure (e.g. `ImageProcessingException`) for unreadable sources; never swallows silently.
- **Verification:**
  - [ ] `dotnet test tests/pcb-deftek.Tests/ --filter ViewportCropServiceTests`
  - [ ] Existing `SqliteDatabaseExportServiceTests` pass unchanged
  - [ ] Build succeeds with 0 warnings
- **Dependencies:** None
- **Files:**
  - `Common/Storage/KnownFolderPaths.cs`
  - `Infrastructure/Services/IViewportCropService.cs`
  - `Infrastructure/Services/ViewportCropService.cs`
  - `Infrastructure/Persistence/DatabaseExport/SqliteDatabaseExportService.cs`
  - `tests/pcb-deftek.Tests/ViewportCropServiceTests.cs`
- **Estimated scope:** M (5 files)

---

### Checkpoint 4: Viewer Logic Complete
- [ ] All viewer state transitions are unit-tested without WPF.
- [ ] Crop output dimensions match the visible region at native resolution.
- [ ] Palette is single-sourced; annotation output unchanged.

---

### Task Group 5: Interactive Viewer View

#### Task 5.1: `DefectImageViewer` Control

- **Description:** Custom control hosting the image, overlay markers, transform interaction, and viewport reporting.
- **Acceptance criteria:**
  - [ ] Dependency properties: `ImageSource`, `Markers`, `SelectedMarker`, `Transform`; events `MarkerSelected` and `ViewportChanged`.
  - [ ] Wheel zoom anchors at the cursor via `ViewportTransform.ZoomAt`; drag pans with left button; click threshold distinguishes click from drag.
  - [ ] Marker hit-testing selects the topmost box and raises `MarkerSelected`.
  - [ ] Focus and reset animate smoothly (≈180 ms, cubic ease) and cancel in-flight animation on new input.
  - [ ] Pointer leaves/returns and DPI changes recompute fit without losing state.
  - [ ] Rendering uses one transformed layer (image + markers), so overlays stay aligned at every zoom.
- **Verification:**
  - [ ] Manual interaction check: wheel, drag, click box, reset, focus
  - [ ] `dotnet test tests/pcb-deftek.Tests/ --filter ViewportTransformTests`
  - [ ] Build succeeds with 0 warnings
- **Dependencies:** Task 4.2
- **Files:**
  - `Presentation/Controls/DefectImageViewer.xaml`
  - `Presentation/Controls/DefectImageViewer.xaml.cs`
- **Estimated scope:** M (2 files)

#### Task 5.2: Viewer Overlay & Sidebar

- **Description:** Compose the viewer modal: main area, floating Reset/Crop buttons, close affordance, and the two sidebar states.
- **Acceptance criteria:**
  - [ ] Overlay covers the History content area with a dim backdrop; `Esc` closes, as does the close button.
  - [ ] Reset and Crop float bottom-right with `IconZoomReset`/`IconCrop`; Crop is disabled until the image is loaded.
  - [ ] Default sidebar shows editable name, local timestamp, and class summary with confidence.
  - [ ] Focused sidebar swaps to class name, confidence, severity, root cause, impact, and recommended action.
  - [ ] Clicking a marker focuses it and updates the sidebar; clicking empty image space returns to the default state.
  - [ ] Image-unavailable state is explicit (icon + message), with crop/reset disabled.
  - [ ] Sidebar is keyboard reachable; theme tokens only.
- **Verification:**
  - [ ] Manual visual check in Light/Dark at 100% and 150% DPI
  - [ ] Build succeeds with 0 warnings
- **Dependencies:** Tasks 4.3, 4.4, 5.1
- **Files:**
  - `Presentation/Views/DefectViewerView.xaml`
  - `Presentation/Views/DefectViewerView.xaml.cs`
- **Estimated scope:** M (2 files)

#### Task 5.3: Host Integration & Lifecycle

- **Description:** Host the viewer inside `HistoryView`, coordinate open/close with `HistoryViewModel`, and feed viewport changes into the crop command.
- **Acceptance criteria:**
  - [ ] Selecting a card loads the record and opens the viewer; closing clears the active record.
  - [ ] `ViewportChanged` supplies the normalized visible rect used by Crop.
  - [ ] `Esc` closes the viewer but cancels an in-progress rename first.
  - [ ] Navigation away closes the viewer and cancels loads.
  - [ ] Focus returns to the originating card after close.
- **Verification:**
  - [ ] Manual end-to-end check: gallery → viewer → focus → crop → close
  - [ ] `dotnet test tests/pcb-deftek.Tests/ --filter HistoryViewModelTests`
  - [ ] Build succeeds with 0 warnings
- **Dependencies:** Task 5.2
- **Files:**
  - `Presentation/Views/HistoryView.xaml`
  - `Presentation/Views/HistoryView.xaml.cs`
  - `Presentation/ViewModels/HistoryViewModel.cs`
- **Estimated scope:** S (3 files)

---

### Checkpoint 5: Viewer Interaction
- [ ] Gallery → viewer → defect focus → crop → close works in one flow.
- [ ] Zoom/pan stay clamped; overlays remain aligned.
- [ ] Missing-image and empty-history states are graceful.

---

### Task Group 6: DI, BDD & Full Verification

#### Task 6.1: Dependency Injection Wiring & Tests

- **Description:** Register the new services and viewer view model; assert registrations.
- **Acceptance criteria:**
  - [ ] `IHistoryThumbnailService` and `IViewportCropService` registered as singletons; `InspectionViewerViewModel` transient.
  - [ ] `HistoryViewModel` resolves with all new dependencies.
  - [ ] `DependencyInjectionTests` asserts lifetimes, implementations, and successful resolution.
- **Verification:**
  - [ ] `dotnet test tests/pcb-deftek.Tests/ --filter DependencyInjectionTests`
  - [ ] Build succeeds with 0 warnings
- **Dependencies:** Task Groups 1–5
- **Files:**
  - `App.xaml.cs`
  - `tests/pcb-deftek.Tests/DependencyInjectionTests.cs`
- **Estimated scope:** S (2 files)

#### Task 6.2: Reqnroll BDD Acceptance Scenarios

- **Description:** End-to-end acceptance scenarios backed by an isolated SQLite database and temporary images.
- **Acceptance criteria:**
  - [ ] `HistoryAndDefectViewer.feature` covers:
    1. Gallery groups records by date and shows accurate PASS/FAIL badges.
    2. Rename persists and survives a reload.
    3. Viewer default sidebar shows name, timestamp, and class summary.
    4. Selecting a defect marker exposes focused diagnostics (severity/root cause/impact/action).
    5. Crop writes a PNG whose dimensions match the requested normalized region.
    6. A missing source image yields the unavailable state without exceptions.
  - [ ] Step definitions implemented in `HistoryAndViewerStepDefinitions.cs`, reusing `TestSqliteConnectionFactory` and the real repository/services.
- **Verification:**
  - [ ] `dotnet test tests/pcb-deftek.Bdd/ --filter HistoryAndDefectViewer`
  - [ ] Full suite passes: `dotnet test pcb-deftek.slnx`
  - [ ] Build succeeds with 0 warnings
- **Dependencies:** Task 6.1
- **Files:**
  - `tests/pcb-deftek.Bdd/Features/HistoryAndDefectViewer.feature`
  - `tests/pcb-deftek.Bdd/StepDefinitions/HistoryAndViewerStepDefinitions.cs`
- **Estimated scope:** M (2 files)

#### Task 6.3: Documentation & Plan Status

- **Description:** Mark task groups/checkpoints complete once verified; record any deviations here.
- **Acceptance criteria:**
  - [ ] Plan checkboxes reflect implemented and verified tasks.
  - [ ] No unchecked item is silently skipped; deferred items are listed under requirements.
- **Verification:**
  - [ ] `git diff` shows only task-status edits in this file
- **Dependencies:** Task 6.2
- **Files:**
  - `specs/2026-11-13-history-interactive-defect-viewer/plan.md`
- **Estimated scope:** XS (1 file)

---

### Checkpoint 6: Merge Readiness
- [ ] All six task groups complete.
- [ ] Solution builds with 0 warnings.
- [ ] All unit, integration, and BDD tests pass (`dotnet test pcb-deftek.slnx`).
- [ ] PR created and passes the 4-step Feature Review and Quality Improvement pipeline.
- [ ] Human review approved; PR not merged without approval.

---

## Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Paged records expose no defect count (current repository gap) | High | Dedicated `InspectionSummary` projection (Task 1.1); test counts explicitly |
| Thumbnail decoding stalls the UI or exhausts memory | High | Background decode + `Freeze()`, 240 px cap, bounded concurrency, cancellation on navigation |
| Grouped `ListBox` disables virtualization | Medium | `IsVirtualizingWhenGrouping="True"` + recycling; validate with 1,000+ seeded records |
| Missing/moved source files (file mode references originals) | Medium | Placeholder thumbnail and explicit unavailable viewer state; no exceptions |
| Zoom/pan math drifts or letterboxes incorrectly | Medium | Pure `ViewportTransform` with unit tests; single transformed layer for image + markers |
| Crop coordinates mismatch the visible viewport | Medium | Normalized-rect contract between viewer and crop service; integration test asserting pixel dimensions |
| DPI scaling misaligns overlays | Medium | Work in device-independent units with `ActualWidth/Height`; verify 100%/150% |
| Rename corrupts history or files | Medium | Trim/validate, revert on failure, never rename files, repository-only update |
| Duplicate items across pages | Low | Stable `CreatedAtUtc DESC, Id DESC` ordering + loaded-ID guard on append |
| Date grouping shifts across time zones | Low | Explicit local-time contract (D9) documented and tested |
| Large images slow to decode | Low | Load one image at a time; decode cancellable; document native-resolution memory expectation |

---

## Open Questions

1. Crop destination defaults to Windows Downloads (D7/D8); confirm if users expect the source folder instead.
2. Gallery previews intentionally omit bounding boxes (D2); confirm QA does not require annotated thumbnails.
3. No delete action in this phase; confirm retention requirements stay out of scope until a later phase.
