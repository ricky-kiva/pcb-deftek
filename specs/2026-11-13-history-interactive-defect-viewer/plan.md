# Implementation Plan: Phase 7 — History & Interactive Defect Viewer

## Overview

Phase 7 turns persisted inspections into a **filterable, date-grouped history gallery** with inline renaming and **confirmed multi-select deletion**, plus a **high-performance interactive defect viewer** featuring pan/zoom, click-to-focus bounding boxes, split default/focused informational sidebar states, viewport cropping to PNG, and a sidebar **Delete Image** action. Deletion follows the approved policy: remove database rows and app-owned capture files; never touch user-owned originals.

> **Task list target:** tasks are tracked directly in this `plan.md`, per the repository convention in `AGENTS.md` §1 (feature specs use `plan.md`, `requirements.md`, `validation.md`). No separate `tasks/todo.md` is used.

---

## Architecture & Dependency Graph

```
┌─────────────────────────────────────────────────────────────┐
│ Domain / Contracts                                          │
│  • InspectionSummary (Id, Name, timestamps, Mode,           │
│    DefectCount, ImagePath, Status)                          │
│  • InspectionQueryFilter (+ Statuses, DefectClasses)        │
│  • DefectClassInfo.ColorHex (single palette source)         │
└─────────────────────────────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────┐
│ Infrastructure                                              │
│  • IInspectionRepository.GetSummariesPagedAsync + filters   │
│  • IInspectionRepository.DeleteManyAsync / lookup by IDs    │
│  • IInspectionDeletionService (DB-first + capture cleanup)  │
│  • IHistoryThumbnailService / WPF thumbnail decoder         │
│  • IViewportCropService / OpenCvSharp crop exporter         │
│  • KnownFolderPaths (shared Downloads resolution)           │
└─────────────────────────────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────┐
│ Presentation — ViewModels                                   │
│  • HistoryViewModel (paging, grouping, filters, selection)  │
│  • HistoryItemViewModel (thumbnail, rename, IsSelected)     │
│  • InspectionViewerViewModel (record, markers, delete)      │
│  • ViewportTransform (pure zoom/pan/crop math)              │
│  • IConfirmationService (WPF modal confirmation)            │
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

### Task Group 1: History Data, Filters & Deletion Foundations

#### Task 1.1: `InspectionSummary` Projection & Repository Query

- **Description:** Add an additive read model that carries the stored `DefectCount`, and a repository query that returns it without loading defect rows. `GetPagedAsync` keeps its signature and return shape; filter behavior is extended in Task 1.3.
- **Acceptance criteria:**
  - [x] `InspectionSummary` exposes `Id`, `Name`, `CreatedAtUtc`, `UpdatedAtUtc`, `Mode`, `DefectCount`, `ImagePath`, plus computed `Status`/`IsPass`.
  - [x] `GetSummariesPagedAsync(InspectionQueryFilter)` returns `PagedResult<InspectionSummary>` selecting `DefectCount` from `Inspections`.
  - [x] Honors `StartDateUtc`, `EndDateUtc`, `Status`, `Mode`, `PageNumber`/`PageSize`; orders by `CreatedAtUtc DESC, Id DESC`.
  - [x] Exactly one SQL query for counts and one for the page; no per-item defect queries.
- **Verification:**
  - [x] `dotnet test tests/pcb-deftek.Tests/ --filter InspectionSummaryTests`
  - [x] Existing `SqliteRepositoryIntegrationTests` still pass
  - [x] Build succeeds with 0 warnings
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
  - [x] Groups by `CreatedAtUtc.ToLocalTime().Date`, groups ordered newest-first.
  - [x] Items inside each group preserve repository order (stable).
  - [x] Group exposes a culture-aware display label and a `Date` key.
  - [x] Empty input returns an empty collection; duplicate timestamps do not merge or drop items.
- **Verification:**
  - [x] `dotnet test tests/pcb-deftek.Tests/ --filter HistoryGroupingTests`
  - [x] Build succeeds with 0 warnings
- **Dependencies:** Task 1.1
- **Files:**
  - `Presentation/Models/HistoryDateGroup.cs`
  - `tests/pcb-deftek.Tests/HistoryGroupingTests.cs`
- **Estimated scope:** XS (2 files)

#### Task 1.3: Composable History Filter Query Support

- **Description:** Extend the query contract additively for the History filter bar and apply the predicates to both record and summary queries through one shared builder.
- **Acceptance criteria:**
  - [x] `InspectionQueryFilter` gains `IReadOnlyCollection<InspectionStatus>? Statuses` and `IReadOnlyCollection<DefectClass>? DefectClasses`; existing `Status`, `Mode`, and date properties keep their behavior.
  - [x] Shared SQL predicate builder is used by `GetPagedAsync` and `GetSummariesPagedAsync`; totals and pages respect the same filters.
  - [x] Status set matches any selected status; empty/null adds no multi-status clause and falls back to the legacy single `Status`. When populated, `Statuses` takes precedence; class set matches records having **any** selected class via `EXISTS` against `Defects`.
  - [x] Date bounds remain inclusive and applied to `CreatedAtUtc`; all values are parameterized (class codes from `DefectClassInfo.Code`).
  - [x] Ordering stays `CreatedAtUtc DESC, Id DESC`.
- **Verification:**
  - [x] `dotnet test tests/pcb-deftek.Tests/ --filter InspectionFilterQueryTests`
  - [x] Existing `InspectionSummaryTests` and `SqliteRepositoryIntegrationTests` pass
  - [x] Build succeeds with 0 warnings
- **Dependencies:** Task 1.1
- **Files:**
  - `Domain/Contracts/InspectionQueryFilter.cs`
  - `Infrastructure/Persistence/Repositories/SqliteInspectionRepository.cs`
  - `tests/pcb-deftek.Tests/InspectionFilterQueryTests.cs`
- **Estimated scope:** S (3 files)

#### Task 1.4: `IInspectionDeletionService` & Batch Deletion

- **Description:** Implement the approved deletion policy: remove database rows transactionally, then clean up only app-owned capture files, never user originals.
- **Acceptance criteria:**
  - [x] Repository adds `DeleteManyAsync(IReadOnlyCollection<Guid>)` executing one transaction and returning the deleted count; defects cascade.
  - [x] Repository adds one-query lookup of `Id`, `Mode`, and `ImagePath` for the requested IDs (no N+1).
  - [x] `IInspectionDeletionService` exposes batch and single-record deletion; it deletes DB rows **first**, then files.
  - [x] Files are deleted only for `DetectionMode.LiveCamera` paths that resolve under the configured Captures root; the `_annotated` sibling is also removed when present.
  - [x] `SingleFile` / `BatchFolder` paths are never deleted; paths outside the root are skipped and logged.
  - [x] Missing files are ignored; cleanup failures are returned/logged without rolling back the committed row deletion.
  - [x] Result reports deleted record count, deleted file count, and file failures.
- **Verification:**
  - [x] `dotnet test tests/pcb-deftek.Tests/ --filter InspectionDeletionServiceTests`
  - [x] Integration test proves a user original still exists after deleting its record
  - [x] Build succeeds with 0 warnings
- **Dependencies:** Task 1.1
- **Files:**
  - `Infrastructure/Persistence/Repositories/IInspectionRepository.cs`
  - `Infrastructure/Persistence/Repositories/SqliteInspectionRepository.cs`
  - `Infrastructure/Services/IInspectionDeletionService.cs`
  - `Infrastructure/Services/InspectionDeletionService.cs`
  - `tests/pcb-deftek.Tests/InspectionDeletionServiceTests.cs`
- **Estimated scope:** M (5 files)

#### Task 1.5: `IConfirmationService`

- **Description:** Thin confirmation abstraction so destructive workflows are testable and always explicit.
- **Acceptance criteria:**
  - [x] `IConfirmationService.ConfirmAsync(title, message, confirmText, cancellationToken)` returns `Task<bool>`.
  - [x] WPF implementation shows a native modal dialog owned by the active window and returns `true` only on explicit confirmation.
  - [x] The default/cancel action is **Cancel**; closing the dialog returns `false`.
  - [x] No view model references `System.Windows.MessageBox` directly.
- **Verification:**
  - [x] Implementation fails closed (returns `false`) when no application host is present
  - [x] Build succeeds with 0 warnings
- **Dependencies:** None
- **Files:**
  - `Presentation/Services/IConfirmationService.cs`
  - `Presentation/Services/WpfConfirmationService.cs`
- **Estimated scope:** XS (2 files)

---

### Checkpoint 1: Correct History Data, Filters & Deletion
- [x] Summaries report accurate PASS/FAIL counts without loading defects.
- [x] Grouping matches local calendar dates and is deterministic.
- [x] Date/status/class filters compose correctly and apply to totals and pages.
- [x] Deletion removes rows and app-owned captures only; user originals survive.
- [x] Confirmation contract exists and fails closed when no UI host is available.
- [x] Build and focused tests are green.

---

### Task Group 2: Gallery ViewModel, Filters & Thumbnails

#### Task 2.1: Reusable Name Editor & `HistoryItemViewModel`

- **Description:** Per-card view model wrapping an `InspectionSummary`, its lazy thumbnail, and inline rename — built on a small reusable `InspectionNameEditor` state object so the viewer does not duplicate the edit/validate/revert logic.
- **Acceptance criteria:**
  - [x] `InspectionNameEditor` implements `BeginRename`, `CancelRename`, `CommitRenameAsync()` with repository injected in the constructor, trim/empty rejection, and revert-on-failure; it owns no UI types.
  - [x] `HistoryItemViewModel` exposes `Name`, local `DisplayDate`/`DisplayTime`, `StatusText` (`PASS`/`FAIL`), `IsPass`, `ModeText`, `DefectCount`, `Thumbnail`, `HasThumbnail`, `IsImageUnavailable`, and `IsSelected`.
  - [x] `IsSelected` is observable and toggled only by the selection checkbox/commands; opening the viewer does not change selection.
  - [x] `BeginRename()` seeds `EditedName` and enters edit mode; `CancelRename()` restores the persisted name.
  - [x] Committing an empty/whitespace name reverts without a write; a successful commit persists via `UpdateNameAsync` and updates `Name`; `false`/exception reverts and surfaces failure.
  - [x] Rename does not touch `ImagePath` or any file on disk.
- **Verification:**
  - [x] `dotnet test tests/pcb-deftek.Tests/ --filter HistoryItemViewModelTests`
  - [x] Build succeeds with 0 warnings
- **Dependencies:** Task 1.1
- **Files:**
  - `Presentation/Models/InspectionNameEditor.cs`
  - `Presentation/ViewModels/HistoryItemViewModel.cs`
  - `tests/pcb-deftek.Tests/HistoryItemViewModelTests.cs`
- **Estimated scope:** S (3 files)

#### Task 2.2: `IHistoryThumbnailService` & WPF Decoder

- **Description:** Background thumbnail decoder producing frozen square-ready previews from `ImagePath`, with bounded concurrency and cancellation.
- **Acceptance criteria:**
  - [x] Decodes with `DecodePixelWidth = 240`, `BitmapCacheOption.OnLoad`, then `Freeze()`; no UI-thread file I/O.
  - [x] Returns `null` for missing, corrupt, or unreadable files without throwing.
  - [x] Concurrency capped (e.g., 4 concurrent decodes) and honors `CancellationToken`.
  - [x] Rejects empty paths via argument validation.
- **Verification:**
  - [x] `dotnet test tests/pcb-deftek.Tests/ --filter HistoryThumbnailServiceTests`
  - [x] Build succeeds with 0 warnings
- **Dependencies:** None
- **Files:**
  - `Infrastructure/Services/IHistoryThumbnailService.cs`
  - `Infrastructure/Services/HistoryThumbnailService.cs`
  - `tests/pcb-deftek.Tests/HistoryThumbnailServiceTests.cs`
- **Estimated scope:** S (3 files)

#### Task 2.3: `HistoryViewModel` Gallery State & Filters

- **Description:** Replace the placeholder with the gallery orchestration: filter state, initial page, load-more, grouping, refresh on navigation, thumbnail hydration, rename delegation, and cancellation.
- **Acceptance criteria:**
  - [x] Constructor depends only on interfaces (`IInspectionRepository`, `IHistoryThumbnailService`, `INotificationService`, `ILogger<HistoryViewModel>`, plus deletion/confirmation dependencies added in Task 2.4).
  - [x] `HistoryFilterViewModel` (or equivalent state object) exposes date preset (`None`/`Day`/`Week`/`Month`/`Custom`), custom start/end, `SelectedStatuses`, `SelectedClasses`, and `ClearFilters`.
  - [x] Presets compute inclusive local bounds (culture first day of week) and are converted to UTC before querying; custom range validates start ≤ end.
  - [x] Any filter change cancels the in-flight load, clears selection, and reloads page 1; results and `TotalCount` reflect the active filter.
  - [x] `OnNavigatedToAsync` loads page 1 (size 60) with the current filters, clears previous state, and hydrates thumbnails asynchronously.
  - [x] `LoadMoreCommand` appends the next page without duplicate IDs and stops at `TotalItems`; commands are disabled while loading.
  - [x] Exposes `Groups`, `Items`, `IsLoading`, `HasMore`, `IsEmpty`, `HasNoMatches`, `ErrorMessage`, and `TotalCount`.
  - [x] `RefreshCommand` and `ClearFiltersCommand` reload from page 1.
  - [x] `OnNavigatedFromAsync` cancels in-flight loads/cancels thumbnails; late completions do not mutate state.
  - [x] Empty result produces the empty state; filters yielding no matches produce the clear-filters state, not an error.
- **Verification:**
  - [x] `dotnet test tests/pcb-deftek.Tests/ --filter HistoryViewModelTests`
  - [x] `dotnet test tests/pcb-deftek.Tests/ --filter HistoryFilterViewModelTests`
  - [x] Build succeeds with 0 warnings
- **Dependencies:** Tasks 1.1, 1.2, 1.3, 2.1, 2.2
- **Files:**
  - `Presentation/ViewModels/HistoryViewModel.cs`
  - `Presentation/ViewModels/HistoryFilterViewModel.cs`
  - `tests/pcb-deftek.Tests/HistoryViewModelTests.cs`
  - `tests/pcb-deftek.Tests/HistoryFilterViewModelTests.cs`
- **Estimated scope:** M (4 files)

#### Task 2.4: Selection & Batch Delete Commands

- **Description:** Gallery-level selection state and the confirmed batch-deletion workflow.
- **Acceptance criteria:**
  - [x] Exposes `SelectedCount`, `SelectLoadedCommand`, `ClearSelectionCommand`, and `DeleteSelectedCommand`; delete is disabled at zero selection or while busy.
  - [x] `SelectLoadedCommand` selects only loaded cards; filter changes/reloads clear selection; selection survives `LoadMore`.
  - [x] `DeleteSelectedCommand` calls `IConfirmationService` with the count and retention-policy message; cancellation performs no writes.
  - [x] On confirmation it calls `IInspectionDeletionService`, removes deleted items from `Items`/`Groups`, updates `TotalCount`, reports a toast with deleted records and cleaned files, and raises a deletion result/event for the host to close the viewer if it showed a deleted record (wired in Task 5.3).
  - [x] Deletion failure keeps selection intact, surfaces an error, and logs.
  - [x] No direct `File.Delete` or repository delete calls from the view model.
- **Verification:**
  - [x] `dotnet test tests/pcb-deftek.Tests/ --filter HistoryViewModelTests`
  - [x] `dotnet test tests/pcb-deftek.Tests/ --filter InspectionDeletionServiceTests`
  - [x] Build succeeds with 0 warnings
- **Dependencies:** Tasks 1.4, 1.5, 2.3
- **Files:**
  - `Presentation/ViewModels/HistoryViewModel.cs`
  - `Presentation/ViewModels/HistoryItemViewModel.cs`
  - `tests/pcb-deftek.Tests/HistoryViewModelTests.cs`
- **Estimated scope:** M (3 files)

---

### Checkpoint 2: Gallery Behaviour
- [x] Paging is duplicate-free and bounded by `TotalItems`.
- [x] Filters compose, reload from page 1, and clear selection.
- [x] Selection is limited to loaded cards and batch delete requires confirmation.
- [x] Renames persist and revert correctly on failure.
- [x] Thumbnails load off-thread and degrade to placeholders.
- [x] Navigation away cancels all work.

---

### Task Group 3: History Gallery View

#### Task 3.1: Filter Bar, Grouped Gallery Layout & Card Template

- **Description:** Build the History page: filter/action bar, header/count, loading/empty/error states, date-grouped cards, selection checkboxes, load-more footer.
- **Acceptance criteria:**
  - [x] Filter bar mirrors the Dashboard Row 1 structure: left side date preset buttons (Day/Week/Month) + Custom date range, Status multi-select, Defect Class multi-select with all nine abbreviations; right side result count and selection/delete actions.
  - [x] Active date presets are visually distinct; clicking an active Day/Week/Month preset clears it; Custom opens its range flyout and is cleared via Clear Filters.
  - [x] Custom range exposes start/end pickers and an Apply action; invalid ranges show validation and do not query.
  - [x] Gallery uses grouped items with a wrapping panel; the 60-record page size bounds loaded cards (WPF has no built-in virtualizing wrap panel).
  - [x] Date header renders the group label; cards render in a wrapping gallery layout.
  - [x] Card has a clipped 1:1 preview (`Stretch="UniformToFill"`), PASS/FAIL badge in the top-left using `StatusPass*`/`StatusFail*` brushes, a selection checkbox top-right, name, local timestamp, mode, and defect count.
  - [x] Selected cards use a visible highlight/border; missing thumbnail shows `IconImage` placeholder with no layout jump.
  - [x] Load-more button visible only when `HasMore`; disabled while loading.
  - [x] Empty history shows guidance to run File or Live detection; filtered zero-results show "No inspections match these filters" with Clear Filters.
  - [x] Reuses existing Fluent theme tokens only; no new hardcoded colors.
- **Verification:**
  - [x] View compiles and renders with sample data
  - [ ] Manual check at 100% and 150% DPI, Light and Dark themes
  - [x] Build succeeds with 0 warnings
- **Dependencies:** Task 2.3
- **Files:**
  - `Presentation/Views/HistoryView.xaml`
  - `Presentation/Views/HistoryView.xaml.cs`
- **Estimated scope:** M (2 files)

#### Task 3.2: Inline Rename, Selection & Delete Interaction

- **Description:** Wire double-click-to-rename, Enter/Escape/lost-focus semantics, keyboard open, selection checkboxes, delete confirmation, and viewer launch from a card.
- **Acceptance criteria:**
  - [x] Double-clicking the name enters edit mode with the text selected and focused.
  - [x] Enter commits; Escape cancels; losing focus commits (no duplicate commits).
  - [x] Empty/whitespace input reverts without a DB write.
  - [x] Enter/Space on a focused card raises `RecordActivated` (viewer host wired in Task 5.3); `F2` starts rename.
  - [x] Clicking a card (outside edit mode and outside the checkbox) raises `RecordActivated`; clicking the checkbox toggles selection only.
  - [x] Select Loaded / Clear Selection / Delete Selected (N) buttons bind to their commands with correct enabled states.
  - [x] Delete Selected shows the confirmation dialog; Cancel leaves data and selection untouched.
  - [x] Focus visuals remain visible in both themes; selection and edit states are visually distinct.
- **Verification:**
  - [ ] Manual interaction check (mouse + keyboard, confirm + cancel)
  - [x] `dotnet test tests/pcb-deftek.Tests/ --filter HistoryViewModelTests`
  - [x] Build succeeds with 0 warnings
- **Dependencies:** Task 3.1
- **Files:**
  - `Presentation/Views/HistoryView.xaml`
  - `Presentation/Views/HistoryView.xaml.cs`
- **Estimated scope:** M (2 files)

---

### Checkpoint 3: History Page Usable
- [x] A user can filter, browse, group, rename, select, delete, and raise the viewer-open request from the gallery.
- [x] Delete confirmation and cancellation behave correctly.
- [ ] No UI stalls while scrolling or loading thumbnails (manual performance check pending).
- [ ] Theme and DPI checks pass (100% dark render verified; 150%/light manual check pending).

---

### Task Group 4: Viewer Logic & Crop Pipeline

#### Task 4.1: Shared Defect Palette

- **Description:** Make the defect color table a single source usable by both OpenCV annotation and WPF overlays.
- **Acceptance criteria:**
  - [x] `DefectClassInfo.ColorHex` returns the established color for all nine classes (e.g. `SH` blue, `OP` fern, `SC` white).
  - [x] `ImageAnnotationService.GetDefectColor` derives its BGR `Scalar` from `ColorHex`; rendered output is unchanged and the existing exact-BGR color theory in `ImageAnnotationServiceTests` passes without modification.
  - [x] A WPF converter turns `ColorHex` into a frozen `SolidColorBrush` with no WPF types in Domain/Infrastructure.
  - [x] All nine classes produce distinct, valid `#RRGGBB` values.
- **Verification:**
  - [x] `dotnet test tests/pcb-deftek.Tests/ --filter DefectPaletteTests`
  - [x] Existing `ImageAnnotationServiceTests` pass unchanged
  - [x] Build succeeds with 0 warnings
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
  - [x] Computes fit scale/offset from image and viewport sizes (letterboxed, centered).
  - [x] `ZoomAt(point, factor)` keeps the image point under the cursor stationary and clamps to `0.5×–10×` of fit.
  - [x] `Pan(dx, dy)` clamps so no viewport edge exposes empty space beyond the image.
  - [x] `FocusOn(normalizedRect, padding)` returns a transform that centers the defect at a bounded zoom.
  - [x] `GetVisibleNormalizedRect()` returns the visible image region clamped to `[0,1]`, suitable for cropping.
  - [x] Reset returns the exact fit transform.
- **Verification:**
  - [x] `dotnet test tests/pcb-deftek.Tests/ --filter ViewportTransformTests`
  - [x] Build succeeds with 0 warnings
- **Dependencies:** None
- **Files:**
  - `Domain/Models/NormalizedRect.cs`
  - `Presentation/Models/ViewportTransform.cs`
  - `tests/pcb-deftek.Tests/ViewportTransformTests.cs`
- **Estimated scope:** M (3 files)

#### Task 4.3: `InspectionViewerViewModel`

- **Description:** Viewer state: record loading, markers, sidebar states, rename, crop command, sidebar deletion, and close.
- **Acceptance criteria:**
  - [x] Loads the full record with defects via `IInspectionRepository.GetByIdAsync` and raises an unavailable state when the image cannot be decoded.
  - [x] Exposes `ImageSource`, `DefectMarkers` (normalized + pixel rects), `SelectedMarker`, `IsDefectFocused`, `ClassSummaries`, `FormattedTimestamp`, `CurrentName`, `IsLoading`, and `IsDeleting`.
  - [x] Focus state exposes class name, confidence, severity, root cause, impact, and recommended action from `DetectedDefect`.
  - [x] Default state summary groups defects by class with count and highest confidence, ordered deterministically.
  - [x] `CropViewportCommand(NormalizedRect)` calls `IViewportCropService`, emits a completion toast with the full path, and reports failures without crashing.
  - [x] `DeleteImageCommand` asks `IConfirmationService` (count = 1, policy message), then calls `IInspectionDeletionService.DeleteAsync(Id)`; on success it raises a `Deleted` event with the record ID, closes/clears state, and emits a toast; on failure it keeps state and reports the error.
  - [x] Delete is disabled while loading, while already deleting, or when no record is loaded.
  - [x] `CloseCommand` clears selection and record state.
  - [x] Rename reuses `InspectionNameEditor` from Task 2.1 (no duplicated edit/validate/revert logic).
- **Verification:**
  - [x] `dotnet test tests/pcb-deftek.Tests/ --filter InspectionViewerViewModelTests`
  - [x] Build succeeds with 0 warnings
- **Dependencies:** Tasks 1.4, 1.5, 4.1, 4.2, plus rename logic from Task 2.1
- **Files:**
  - `Presentation/ViewModels/InspectionViewerViewModel.cs`
  - `Presentation/ViewModels/DefectMarkerViewModel.cs`
  - `Presentation/ViewModels/DefectClassSummaryViewModel.cs`
  - `tests/pcb-deftek.Tests/InspectionViewerViewModelTests.cs`
- **Estimated scope:** M (4 files)

#### Task 4.4: `IViewportCropService` & Shared Downloads Path

- **Description:** Crop the currently visible normalized region from the source image at native resolution and export it as PNG.
- **Acceptance criteria:**
  - [x] `KnownFolderPaths.GetDownloadsDirectory()` contains the existing Downloads resolution/fallback; `SqliteDatabaseExportService` delegates to it with unchanged behavior.
  - [x] Crop clamps the normalized rect to `[0,1]`, converts to native pixels, rejects empty results, and writes a PNG.
  - [x] Output name is sanitized `{recordName}_crop_{yyyyMMdd-HHmmss}.png` with a numeric suffix on collision; no overwrites.
  - [x] Honors an optional directory override for tests; defaults to Downloads.
  - [x] Throws/returns a typed failure (e.g. `ImageProcessingException`) for unreadable sources; never swallows silently.
- **Verification:**
  - [x] `dotnet test tests/pcb-deftek.Tests/ --filter ViewportCropServiceTests`
  - [x] Existing `SqliteDatabaseExportServiceTests` pass unchanged
  - [x] Build succeeds with 0 warnings
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
- [x] All viewer state transitions are unit-tested without WPF.
- [x] Crop output dimensions match the visible region at native resolution.
- [x] Palette is single-sourced; annotation output unchanged.

---

### Task Group 5: Interactive Viewer View

#### Task 5.1: `DefectImageViewer` Control

- **Description:** Custom control hosting the image, overlay markers, transform interaction, and viewport reporting.
- **Acceptance criteria:**
  - [x] Dependency properties: `ImageSource`, `Markers`, `SelectedMarker`, `Transform`; events `MarkerSelected` and `ViewportChanged`.
  - [x] Wheel zoom anchors at the cursor via `ViewportTransform.ZoomAt`; drag pans with left button; click threshold distinguishes click from drag.
  - [x] Marker hit-testing selects the topmost box and raises `MarkerSelected`.
  - [x] Focus and reset animate smoothly (≈180 ms, cubic ease) and cancel in-flight animation on new input.
  - [x] Pointer leaves/returns and DPI changes recompute fit without losing state.
  - [x] Rendering uses one transformed layer (image + markers), so overlays stay aligned at every zoom.
- **Verification:**
  - [ ] Manual pointer check: wheel, drag, click box, reset, focus (control animation covered by `DefectImageViewerTests`; pointer pass pending)
  - [x] `dotnet test tests/pcb-deftek.Tests/ --filter ViewportTransformTests`
  - [x] `dotnet test tests/pcb-deftek.Tests/ --filter DefectImageViewerTests`
  - [x] Build succeeds with 0 warnings
- **Dependencies:** Task 4.2
- **Files:**
  - `Presentation/Controls/DefectImageViewer.xaml`
  - `Presentation/Controls/DefectImageViewer.xaml.cs`
  - `tests/pcb-deftek.Tests/DefectImageViewerTests.cs`
- **Estimated scope:** M (3 files)

#### Task 5.2: Viewer Overlay & Sidebar

- **Description:** Compose the viewer modal: main area, floating Reset/Crop buttons, close affordance, the two sidebar states, and the sidebar `Delete Image` action.
- **Acceptance criteria:**
  - [x] Overlay covers the History content area with a dim backdrop; `Esc` closes, as does the close button.
  - [x] Reset and Crop float bottom-right with `IconZoomReset`/`IconCrop`; Crop is disabled until the image is loaded.
  - [x] Default sidebar shows editable name, local timestamp, and class summary with confidence.
  - [x] Focused sidebar swaps to class name, confidence, severity, root cause, impact, and recommended action.
  - [x] `Delete Image` button appears below the sidebar content in **both** states, uses destructive styling, and is disabled while loading or deleting.
  - [x] Clicking a marker focuses it and updates the sidebar; clicking empty image space returns to the default state.
  - [x] Image-unavailable state is explicit (icon + message), with crop/reset/delete disabled.
  - [x] Sidebar is keyboard reachable; theme tokens only.
- **Verification:**
  - [x] Visual check in Light and Dark at 100% DPI, both sidebar states (offscreen renders reviewed)
  - [ ] Manual 150% DPI check pending
  - [x] Build succeeds with 0 warnings
- **Dependencies:** Tasks 4.3, 4.4, 5.1
- **Files:**
  - `Presentation/Views/DefectViewerView.xaml`
  - `Presentation/Views/DefectViewerView.xaml.cs`
- **Estimated scope:** M (2 files)

#### Task 5.3: Host Integration & Lifecycle

- **Description:** Host the viewer inside `HistoryView`, coordinate open/close and deletion with `HistoryViewModel`, and feed viewport changes into the crop command.
- **Acceptance criteria:**
  - [x] Selecting a card loads the record and opens the viewer; closing clears the active record.
  - [x] `ViewportChanged` supplies the normalized visible rect used by Crop.
  - [x] `Esc` closes the viewer but cancels an in-progress rename first.
  - [x] Viewer `Deleted` event removes the card from the gallery, updates `TotalCount`/groups, and closes the viewer.
  - [x] Batch deletion of the record open in the viewer also closes the viewer and clears its state.
  - [x] Navigation away closes the viewer and cancels loads.
  - [x] Focus returns to the originating card after close (or the gallery/empty state after deletion).
- **Verification:**
  - [ ] Manual end-to-end check: gallery → viewer → focus → crop → delete → close
  - [x] `dotnet test tests/pcb-deftek.Tests/ --filter HistoryViewModelTests`
  - [x] Build succeeds with 0 warnings
- **Dependencies:** Task 5.2
- **Files:**
  - `Presentation/Views/HistoryView.xaml`
  - `Presentation/Views/HistoryView.xaml.cs`
  - `Presentation/ViewModels/HistoryViewModel.cs`
  - `tests/pcb-deftek.Tests/HistoryViewModelTests.cs`
- **Estimated scope:** S (4 files)

---

### Checkpoint 5: Viewer Interaction
- [x] Gallery → viewer → focus → crop → sidebar delete → close is implemented and component-tested; manual pointer/crop pass remains.
- [x] Zoom/pan stay clamped; overlays remain aligned.
- [x] Missing-image and empty-history states are graceful.
- [x] Delete confirmation, cancellation, and post-delete gallery refresh behave correctly.

---

### Task Group 6: DI, BDD & Full Verification

#### Task 6.1: Dependency Injection Wiring & Tests

- **Description:** Register the new services and viewer view model; assert registrations.
- **Acceptance criteria:**
  - [ ] `IHistoryThumbnailService`, `IViewportCropService`, `IInspectionDeletionService`, and `IConfirmationService` registered as singletons; `InspectionViewerViewModel` transient.
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
    7. Date preset, status, and defect-class filters compose and return the expected records with accurate totals.
    8. Confirmed multi-select deletion removes rows and app-owned captures while keeping a user original on disk.
    9. Cancelling the confirmation leaves rows, captures, and selection untouched.
    10. Viewer sidebar `Delete Image` removes the record, closes the viewer, and refreshes the gallery.
  - [ ] Step definitions implemented in `HistoryAndViewerStepDefinitions.cs`, reusing `TestSqliteConnectionFactory`, the real repository/services, and mocked `IConfirmationService` outcomes.
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
| Destructive delete targets a non-owned file | High | `IInspectionDeletionService` only deletes `LiveCamera` paths resolved under the Captures root; containment tests; user originals covered by integration test |
| Filter query performance degrades on large histories | Medium | Shared predicate builder with parameterized `EXISTS` on indexed `Defects.ClassCode`; 1,000-record query test |
| Filter/paging race shows stale results | Medium | Cancel previous load on every filter change; generation/token guard before mutating state |
| Confirmation bypass or accidental confirm | Medium | All destructive paths go through `IConfirmationService`; default button is Cancel; view-model tests for both outcomes |
| Partial delete (rows removed, file cleanup failed) confuses users | Medium | Result object + explicit toast: records deleted, files cleaned/failed; log details |
| Selection vs. open click ambiguity | Medium | Explicit checkbox hit area (D26); card click opens only outside the checkbox |
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
3. Confirmation UI defaults to a native modal dialog (D24); confirm if a fully themed in-app dialog is required.
