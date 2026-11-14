# Validation Criteria: Phase 7 — History & Interactive Defect Viewer

## 1. Build & Quality Gates

- **Build command:** `dotnet build pcb-deftek.slnx`
- **Pass condition:** exit code 0, 0 errors, 0 warnings.
- **Nullable:** nullable reference types enabled across all new code; no `#nullable disable`, no suppression attributes added.
- **Architecture:** no `System.Windows.Controls` references in ViewModels; no SQL outside `Infrastructure/Persistence`; no direct file-system writes or deletes outside Infrastructure services.
- **Destructive operations:** record/file deletion only through `IInspectionDeletionService` after `IConfirmationService` approval; deletion targets must be app-owned crops under the configured image output root or live captures under the legacy/image roots.
- **Dependencies:** no new NuGet packages; `pcb-deftek.csproj` package list unchanged.
- **Database:** migration version 2 adds `Inspections.Origin` to distinguish app-generated crops/captures from external imports.
- **Files:** no hardcoded absolute user paths; Downloads resolved through `KnownFolderPaths`; app data rooted under `%LOCALAPPDATA%\PcbDeftek\pcb-deftek`.

---

## 2. Automated Test Gates

- **Full suite:** `dotnet test pcb-deftek.slnx`
- **Pass condition:**
  - 100% passing, 0 failed, 0 skipped.
  - Phase 6 baseline of **452 tests** does not regress.
  - All new history/viewer tests execute in the suite.
- **Focused commands during implementation:**

```powershell
dotnet test tests/pcb-deftek.Tests/ --filter InspectionSummaryTests
dotnet test tests/pcb-deftek.Tests/ --filter InspectionFilterQueryTests
dotnet test tests/pcb-deftek.Tests/ --filter InspectionDeletionServiceTests
dotnet test tests/pcb-deftek.Tests/ --filter HistoryGroupingTests
dotnet test tests/pcb-deftek.Tests/ --filter HistoryItemViewModelTests
dotnet test tests/pcb-deftek.Tests/ --filter HistoryThumbnailServiceTests
dotnet test tests/pcb-deftek.Tests/ --filter HistoryFilterViewModelTests
dotnet test tests/pcb-deftek.Tests/ --filter HistoryViewModelTests
dotnet test tests/pcb-deftek.Tests/ --filter DefectPaletteTests
dotnet test tests/pcb-deftek.Tests/ --filter ViewportTransformTests
dotnet test tests/pcb-deftek.Tests/ --filter InspectionViewerViewModelTests
dotnet test tests/pcb-deftek.Tests/ --filter ViewportCropServiceTests
dotnet test tests/pcb-deftek.Tests/ --filter DependencyInjectionTests
dotnet test tests/pcb-deftek.Bdd/ --filter HistoryAndDefectViewer
```

### 2.1 Required Unit Coverage

- **Summary projection:** counts, PASS/FAIL derivation, paging, date/status/mode filters, stable ordering, no defect-row loading.
- **Filter query:** each filter alone and combined; empty selections = no filter; both statuses = no status clause; class filter matches any selected class via `EXISTS`; defectless records excluded by class filters; filtered totals/pages consistent.
- **Deletion service:** transactional row delete; capture-root containment; `_annotated` sibling cleanup; user originals untouched; missing files ignored; cleanup failure reported without rolling back rows; single and batch paths.
- **Grouping:** local-date buckets, newest-first groups, stable in-group order, empty input, duplicate timestamps.
- **Item rename:** begin/cancel/commit, trim, empty rejection, persistence failure rollback; selection flag independent of rename.
- **Thumbnail service:** frozen bitmap, 240 px decode cap, concurrency cap, cancellation, null on missing/corrupt file.
- **Gallery view model:** initial load, load-more append without duplicates, stop at total, error state, cancellation on navigation away, refresh.
- **Filter view model:** preset date bounds (local→UTC), custom-range validation, status/class sets, clear filters, reload-on-change.
- **Selection/deletion view model:** select loaded, clear selection, count, confirmation cancel (no writes), confirmation accept (removal + totals + toast), failure retaining selection, open-viewer closure.
- **Palette:** nine distinct valid colors; annotation output unchanged; converter produces matching brush.
- **Viewport math:** fit, zoom-at-cursor anchoring, clamp `0.5×–10×`, pan clamps, focus centering, visible normalized rect, reset exactness.
- **Viewer view model:** record load with defects, class summary grouping, focus fields, missing image state, crop command invocation, sidebar delete confirm/accept/failure, close cleanup.
- **Crop service:** normalized clamping, native-resolution dimensions, sanitized/collision-safe names, directory override, failure exception.

### 2.2 Required BDD Coverage

`tests/pcb-deftek.Bdd/Features/HistoryAndDefectViewer.feature`:

1. Gallery groups records by date and shows accurate PASS/FAIL badges.
2. Rename persists and survives a reload.
3. Viewer default sidebar shows name, timestamp, and class summary.
4. Selecting a defect marker exposes focused diagnostics.
5. Crop writes a PNG matching the requested normalized region.
6. Missing source image yields the unavailable state without exceptions.
7. Date preset, status, and defect-class filters compose and return expected records/totals.
8. Confirmed multi-select deletion removes rows and app-owned captures while a user original remains on disk.
9. Cancelling the confirmation changes nothing (rows, captures, selection).
10. Sidebar `Delete Image` deletes the record, closes the viewer, and refreshes the gallery.

Scenarios must run against an isolated SQLite database (reusing `TestSqliteConnectionFactory`) and temporary image files; no dependency on the developer's real `%LOCALAPPDATA%` history.

---

## 3. Functional Acceptance Criteria

### 3.1 History Gallery

- **Loading:** opening History shows a loading state, then the newest records; no blank flash after data arrives.
- **Grouping:** cards are grouped by local calendar date with newest dates first; each record appears exactly once.
- **Card content:** square 1:1 preview, PASS/FAIL badge in the top-left, record name, local timestamp, detection mode, defect count.
- **Badges:** a record with 0 defects shows green `PASS`; 1+ defects shows red `FAIL` — including records whose defects are not loaded into the gallery page.
- **Thumbnails:** generated off the UI thread, ≤240 px, frozen; missing/corrupt files show the image placeholder.
- **Paging:** "Load more" appends the next 60 records until `TotalItems`; no duplicates; button hides at the end.
- **Empty state:** no records shows guidance to run File-Based or Live detection, not an error.
- **Error state:** repository failure shows a retry action and logs the exception.

### 3.2 History Filters

- Filter bar uses the Dashboard Row 1 structure: date preset buttons (Day/Week/Month) + Custom range on the left; status and defect-class multi-selects; result count and selection/delete actions on the right.
- Default state is **all time**, no status selection, no class selection, showing all records.
- Day/Week/Month presets filter to the current local day/week/month; clicking the active preset clears it; Clear Filters resets all three categories.
- **Week boundary** follows the current culture's first day of week; bounds are inclusive and converted from local time to UTC for the query.
- **Custom range** applies inclusive start/end dates; start after end is rejected with visible validation and does not query.
- **Status filter** supports PASS and/or FAIL; empty selection means both.
- **Class filter** matches records containing at least one selected class; defectless records never match a class filter; selecting all nine classes matches every record with at least one defect (still excluding defectless records).
- Filters compose with AND across categories and OR within a category; totals and Load More respect the active filter.
- Changing any filter reloads from page 1, cancels the in-flight load, and clears selection; stale responses never overwrite newer results.
- Filtered zero-results show "No inspections match these filters" with a Clear Filters action, distinct from the empty-history state.

### 3.3 Multi-Select Selection & Deletion

- Each card exposes a selection checkbox; clicking it toggles selection without opening the viewer; selected cards have a visible highlight.
- "Select Loaded" selects all currently loaded cards; "Clear Selection" deselects all; selection survives Load More and is cleared by filter changes, refresh, and navigation.
- "Delete Selected (N)" is disabled at `N = 0` and while deletion runs; the count matches selected cards.
- Invoking delete shows a confirmation dialog naming the record count and the policy: app-owned crops and captures are deleted, user originals are kept. Default action is Cancel.
- **Cancel** closes the dialog and changes nothing (no database writes, no file deletions, selection intact).
- **Confirm** deletes the selected database rows and their defects in one transaction, then deletes only app-owned files: `Crop` records remove their image under the configured image output root; `LiveCamera` records remove raw and `_annotated` files under the legacy captures root or image output root. `SingleFile`/`BatchFolder` imports remain on disk.
- After success: deleted cards disappear, date groups are recomputed, `TotalCount` updates, selection clears, and a toast reports records deleted and files cleaned.
- If the deleted set includes the record open in the viewer, the viewer closes and its state clears.
- A database failure leaves all rows/files intact, keeps selection, and shows an error; file-cleanup failures after a committed row delete are reported as warnings without resurrecting rows.
- Repeated clicks while running do not start a second deletion.

### 3.4 Inline Rename

- Double-clicking a name enters edit mode with text selected.
- Enter commits; Escape cancels; focus loss commits once.
- Whitespace-only or empty input reverts and writes nothing.
- Successful rename updates the card and survives navigating away and back.
- Repository failure reverts the name, shows an error notification, and logs.
- Source image files on disk are never renamed or moved.

### 3.5 Viewer Launch & Layout

- Clicking a card (or Enter/Space on a focused card) opens the viewer for that record.
- The viewer is an in-page modal overlay with a dim backdrop; `Esc` and the close button dismiss it.
- Focus returns to the originating card after close.
- Reset and Crop controls float bottom-right; Crop is disabled until the image loads.
- Sidebar occupies the right side in both default and focused states.

### 3.6 Viewer Default Sidebar

- Shows an editable record name, the detection timestamp in local time, and a class summary list.
- Summary groups by defect class, showing abbreviation/name, count, and highest confidence.
- Zero defects shows a clear "No defects detected" summary rather than an empty list.

### 3.7 Viewer Focused Sidebar

- Clicking a bounding box selects it, visually highlights it, zooms/centers it, and swaps the sidebar.
- Focused state shows: class name, confidence, severity, root cause, impact, recommended action.
- Clicking empty image space returns to the default sidebar and deselects the box.
- Overlapping boxes: the topmost rendered box receives the click.

### 3.8 Sidebar Delete Image

- `Delete Image` appears below the sidebar content in both default and focused states, with destructive styling and an accessible name.
- The button is disabled while the record is loading or a deletion is running.
- Invoking it shows the confirmation dialog with the single-record policy (app-owned captures deleted, user originals kept).
- **Cancel** leaves the record, captures, viewer state, and gallery untouched.
- **Confirm** deletes the record and its app-owned captures, closes the viewer, removes the card from the gallery, updates `TotalCount`/groups, and emits a completion toast.
- Deleting a `SingleFile` / `BatchFolder` record removes the database row but leaves the user's original image on disk.
- On failure the viewer stays open with its state intact, the error is reported, and the gallery is unchanged.

### 3.9 Pan, Zoom & Reset

- Mouse wheel zooms anchored at the cursor; touchpad scrolling zooms the same way.
- Zoom range is 0.5×–10× of fit; the image cannot be panned outside the viewport.
- Dragging pans smoothly; a click without movement selects instead of panning.
- Reset returns exactly to the fit transform.
- Bounding boxes remain pixel-aligned with the image at every zoom level.

### 3.10 Crop

- Crop exports exactly the visible viewport region, at native source resolution.
- Output is a valid PNG in the configured crop directory (default `Downloads/PCB Deftek/Images`).
- File name is `{recordName}_crop_{yyyyMMdd-HHmmss}.png`, sanitized; collisions get a numeric suffix; existing files are never overwritten.
- A completion toast reports the full path and confirms the new History entry.
- The crop is inserted into History as a new inspection with the defects intersecting the crop; each box is clipped and re-normalized to the crop dimensions.
- Cropping does not modify the source image or create a PDF/report.

### 3.11 Missing / Corrupt Image

- Viewer shows an explicit unavailable state (icon + message) with Reset/Crop/Delete disabled.
- Gallery card shows the placeholder thumbnail.
- No unhandled exceptions in either surface.

### 3.12 Keyboard & Accessibility

- `Tab` reaches filter controls, cards, checkboxes, selection/delete actions, Load More, sidebar fields, and floating buttons.
- Enter/Space opens a card; `Space` on a focused checkbox toggles selection; `F2` renames; `Esc` cancels editing/dialogs, then closes the viewer.
- Delete actions require an explicit confirmation; keyboard focus starts on Cancel.
- Visible focus indicators exist in Light and Dark themes.
- Sidebar labels are associated with their controls; images expose accessible names.

### 3.13 Theming & DPI

- Light and Dark themes render correctly using existing Fluent tokens and status/severity brushes.
- 100%, 125%, and 150% DPI: previews stay square, badges stay top-left, overlays stay aligned, no clipped text.
- White `SC` boxes remain distinguishable from light test images.

---

## 4. Performance & Stability Gates

| Gate | Threshold | Method |
|---|---|---|
| Thumbnail decode | ≤ 100 ms per image, ≤ 4 concurrent | Timed unit/integration test with sample images |
| Gallery first page (60 records) | Loaded and thumbnails hydrated ≤ 1.5 s on the dev machine | Stopwatch instrumentation / manual timing |
| UI thread blocking | No single stall > 100 ms during scroll/load | Manual scroll + responsiveness check; all decode/crop off-thread |
| Viewer zoom/pan | Interactive, no visible jank; ≥ 50 FPS illusion under continuous drag | Manual drag/scroll while observing overlay alignment |
| Full-size image open | ≤ 500 ms for the test asset; loading state visible beyond that | Timed manual check |
| Crop | ≤ 300 ms for test asset; no UI freeze | Timed manual check |
| Memory | 200 thumbnails + one open full-size image stays within a reasonable bound; no unbounded growth after open/close cycles | Task Manager observation over 10 open/close cycles |
| Paging | 1,000 seeded records: no duplicate IDs, group order stable, loaded pages remain responsive without virtualization (60-record page bound) | Seeded integration/manual check |
| Filtered query | 1,000 seeded records with class + status + date filters: p95 ≤ 150 ms, correct totals | Seeded integration timing test |
| Batch delete | 100 records: DB transaction + capture cleanup ≤ 500 ms on the dev machine; UI remains responsive | Seeded integration/manual timing |
| Confirmation | No DB/file work occurs before confirmation; dialog does not freeze the app | Manual check |

Stability: navigate to History, filter repeatedly, open/close 20 records, rename 10, select/delete 10 batches, crop 5, navigate away mid-load — no crash, no stale UI mutation, no leaked file handles, no orphaned rows.

---

## 5. Edge-Case Verification

- [ ] Empty history renders the empty state.
- [ ] Exactly 1 record and exactly 60 records behave correctly (no Load More at 60 if total is 60).
- [ ] Records across midnight boundaries group by local date.
- [ ] Duplicate `CreatedAtUtc` values sort deterministically and never duplicate on Load More.
- [ ] Record with 0 defects shows PASS and "No defects detected".
- [ ] Record with all nine classes renders all nine colors and summary entries.
- [ ] Missing source path, directory, zero-byte file, and non-image file each degrade gracefully.
- [ ] Rename to a 200-character name does not break layout (ellipsis/trim in card).
- [ ] Crop at maximum zoom and minimum zoom produces a valid, non-empty PNG.
- [ ] Navigation away during thumbnail/image load produces no exceptions or stale updates.
- [ ] Viewer opened for a LiveCamera record uses the raw capture, not the annotated sibling.
- [ ] Each date preset (Day/Week/Month) returns the expected local range; toggling the active preset restores all time.
- [ ] Custom range with start after end is rejected and issues no query.
- [ ] Status = PASS combined with any class filter returns no matches with the clear-filters state.
- [ ] Selecting all nine classes matches every record that has at least one defect, while PASS records remain excluded.
- [ ] Filter change during an in-flight load cancels the old request; no stale rows or totals appear.
- [ ] Delete with zero selection is disabled; "Select Loaded" then Load More selects only pre-existing loaded cards.
- [ ] Cancel on the delete confirmation leaves rows, capture files, and selection unchanged.
- [ ] Confirmed delete of a `SingleFile` record removes the row and leaves the original file on disk.
- [ ] Confirmed delete of a `LiveCamera` record removes the raw and `_annotated` captures.
- [ ] A capture path outside the captures root is skipped (row deleted, file untouched, warning logged).
- [ ] Cleanup failure after a committed row delete reports a warning and does not crash.
- [ ] Deleting the record open in the viewer closes the viewer and removes the card.
- [ ] Sidebar `Delete Image` cancel keeps the viewer open with state intact.

---

## 6. Manual Verification Script

1. Seed history by running one File-Based batch and one Live capture (or use BDD-created data).
2. Open History; confirm date order, grouping, square previews, PASS/FAIL badges, counts.
3. Apply Day, Week, and Month presets; toggle each off; apply a custom range; confirm result counts and group headers change accordingly.
4. Select a status and one or more defect classes; combine with a date preset; confirm AND/OR behavior and the no-matches state; use Clear Filters.
5. Change filters while a page is loading; confirm no stale rows or duplicate cards appear.
6. Rename a record inline; restart the app; confirm the new name persists.
7. Select two records; open Delete Selected; cancel; confirm rows still present. Repeat and confirm; verify rows disappear, totals update, and the toast reports cleanup.
8. For a deleted `SingleFile` record, confirm the original file still exists at its path. For a deleted LiveCamera record, confirm its captures are gone.
9. Open a FAIL record; confirm boxes align; click each box and verify diagnostics change.
10. Use sidebar `Delete Image`; cancel first, then confirm; verify the viewer closes, the card is removed, and a `SingleFile` original remains on disk.
11. Zoom to maximum and minimum; pan to each corner; confirm clamps.
12. Reset view; confirm exact fit.
13. Crop at a zoomed position; open the PNG from the toast path; confirm it matches the viewport.
14. Delete/move a source image, reopen its record; confirm the unavailable state and disabled actions.
15. Switch theme to Light and repeat steps 2–13; repeat at 150% DPI.

---

## 7. Merge Readiness & Definition of Done

Phase 7 may merge into `main` only when:

1. All six task groups in `plan.md` are implemented and checked.
2. `dotnet build pcb-deftek.slnx` succeeds with 0 errors and 0 warnings.
3. `dotnet test pcb-deftek.slnx` passes 100% (no skipped tests), with no regression against the 452-test Phase 6 baseline.
4. All functional acceptance criteria in §3 are verified; performance gates in §4 are met.
5. Deletion policy is proven by tests: database rows and app-owned crops/captures are deleted, user originals survive, and out-of-root paths are never deleted.
6. No new dependencies, no hardcoded user paths; the `Origin` migration is the only schema addition.
7. Clean Git history following the 6-step lifecycle in `AGENTS.md`.
8. Pull Request is linked to its issue and passes the 4-step Feature Review and Quality Improvement pipeline (`code-review-and-quality`, `code-simplification`, `performance-optimization`, `security-and-hardening`).
9. Human review approval is recorded before merge; the PR is not self-merged.

---

## 8. Failure Triage

| Symptom | First check |
|---|---|
| All gallery badges show PASS | `InspectionSummary.DefectCount` projection / SQL column selection |
| Filters ignored or totals wrong | Shared predicate builder; query parameters; status/class set wiring |
| Class filter shows PASS records | `EXISTS` predicate missing or applied outside the `Defects` join |
| Stale results after changing filters | Cancellation/generation guard before mutating `Items` |
| Gallery stalls while scrolling | Thumbnails generated on UI thread or missing concurrency cap |
| Duplicate cards after Load More | Off-by-one `PageNumber` or missing loaded-ID guard |
| Delete button enabled at zero selection | `SelectedCount` notification/`CanExecute` wiring |
| Confirmation cancel still deletes | Service call placed before/independent of the confirm result |
| User original deleted | Deletion service path/ownership check; `DetectionMode` gate |
| Orphaned capture files after delete | Cleanup skipped for `LiveCamera` or `_annotated` sibling |
| Viewer stays open after deleting its record | `Deleted` event not wired to gallery/viewer close |
| Boxes offset from image | Overlay not inside the same transformed layer, or normalized/pixel mix-up |
| Crop captures wrong region | `GetVisibleNormalizedRect` clamp or DPI scaling error |
| Rename disappears after reload | `UpdateNameAsync` not called or name updated only in memory |
| UI freezes on viewer open | Image decoded synchronously on the UI thread |
| Viewer shows doubled boxes | Rendering `_annotated.png` instead of the raw `ImagePath` |
