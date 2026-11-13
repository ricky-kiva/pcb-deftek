# Validation Criteria: Phase 7 — History & Interactive Defect Viewer

## 1. Build & Quality Gates

- **Build command:** `dotnet build pcb-deftek.slnx`
- **Pass condition:** exit code 0, 0 errors, 0 warnings.
- **Nullable:** nullable reference types enabled across all new code; no `#nullable disable`, no suppression attributes added.
- **Architecture:** no `System.Windows.Controls` references in ViewModels; no SQL outside `Infrastructure/Persistence`; no direct file-system writes outside Infrastructure services.
- **Dependencies:** no new NuGet packages; `pcb-deftek.csproj` package list unchanged.
- **Database:** no schema migration added. Migration count remains 1.
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
dotnet test tests/pcb-deftek.Tests/ --filter HistoryGroupingTests
dotnet test tests/pcb-deftek.Tests/ --filter HistoryItemViewModelTests
dotnet test tests/pcb-deftek.Tests/ --filter HistoryThumbnailServiceTests
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
- **Grouping:** local-date buckets, newest-first groups, stable in-group order, empty input, duplicate timestamps.
- **Item rename:** begin/cancel/commit, trim, empty rejection, persistence failure rollback.
- **Thumbnail service:** frozen bitmap, 240 px decode cap, concurrency cap, cancellation, null on missing/corrupt file.
- **Gallery view model:** initial load, load-more append without duplicates, stop at total, error state, cancellation on navigation away, refresh.
- **Palette:** nine distinct valid colors; annotation output unchanged; converter produces matching brush.
- **Viewport math:** fit, zoom-at-cursor anchoring, clamp `0.5×–10×`, pan clamps, focus centering, visible normalized rect, reset exactness.
- **Viewer view model:** record load with defects, class summary grouping, focus fields, missing image state, crop command invocation, close cleanup.
- **Crop service:** normalized clamping, native-resolution dimensions, sanitized/collision-safe names, directory override, failure exception.

### 2.2 Required BDD Coverage

`tests/pcb-deftek.Bdd/Features/HistoryAndDefectViewer.feature`:

1. Gallery groups records by date and shows accurate PASS/FAIL badges.
2. Rename persists and survives a reload.
3. Viewer default sidebar shows name, timestamp, and class summary.
4. Selecting a defect marker exposes focused diagnostics.
5. Crop writes a PNG matching the requested normalized region.
6. Missing source image yields the unavailable state without exceptions.

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

### 3.2 Inline Rename

- Double-clicking a name enters edit mode with text selected.
- Enter commits; Escape cancels; focus loss commits once.
- Whitespace-only or empty input reverts and writes nothing.
- Successful rename updates the card and survives navigating away and back.
- Repository failure reverts the name, shows an error notification, and logs.
- Source image files on disk are never renamed or moved.

### 3.3 Viewer Launch & Layout

- Clicking a card (or Enter/Space on a focused card) opens the viewer for that record.
- The viewer is an in-page modal overlay with a dim backdrop; `Esc` and the close button dismiss it.
- Focus returns to the originating card after close.
- Reset and Crop controls float bottom-right; Crop is disabled until the image loads.
- Sidebar occupies the right side in both default and focused states.

### 3.4 Viewer Default Sidebar

- Shows an editable record name, the detection timestamp in local time, and a class summary list.
- Summary groups by defect class, showing abbreviation/name, count, and highest confidence.
- Zero defects shows a clear "No defects detected" summary rather than an empty list.

### 3.5 Viewer Focused Sidebar

- Clicking a bounding box selects it, visually highlights it, zooms/centers it, and swaps the sidebar.
- Focused state shows: class name, confidence, severity, root cause, impact, recommended action.
- Clicking empty image space returns to the default sidebar and deselects the box.
- Overlapping boxes: the topmost rendered box receives the click.

### 3.6 Pan, Zoom & Reset

- Mouse wheel zooms anchored at the cursor; touchpad scrolling zooms the same way.
- Zoom range is 0.5×–10× of fit; the image cannot be panned outside the viewport.
- Dragging pans smoothly; a click without movement selects instead of panning.
- Reset returns exactly to the fit transform.
- Bounding boxes remain pixel-aligned with the image at every zoom level.

### 3.7 Crop

- Crop exports exactly the visible viewport region, at native source resolution.
- Output is a valid PNG in the Windows Downloads folder by default.
- File name is `{recordName}_crop_{yyyyMMdd-HHmmss}.png`, sanitized; collisions get a numeric suffix; existing files are never overwritten.
- A completion toast reports the full path; failure produces a failure notification and does not crash.
- Cropping does not create an inspection record or modify the source file.

### 3.8 Missing / Corrupt Image

- Viewer shows an explicit unavailable state (icon + message) with Reset/Crop disabled.
- Gallery card shows the placeholder thumbnail.
- No unhandled exceptions in either surface.

### 3.9 Keyboard & Accessibility

- `Tab` reaches cards, Load More, sidebar fields, and floating buttons.
- Enter/Space opens a card; `F2` renames; `Esc` cancels editing, then closes the viewer.
- Visible focus indicators exist in Light and Dark themes.
- Sidebar labels are associated with their controls; images expose accessible names.

### 3.10 Theming & DPI

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
| Paging | 1,000 seeded records: no duplicate IDs, group order stable, scroll remains virtualized | Seeded integration/manual check |

Stability: navigate to History, open/close 20 records, rename 10, crop 5, navigate away mid-load — no crash, no stale UI mutation, no leaked file handles.

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

---

## 6. Manual Verification Script

1. Seed history by running one File-Based batch and one Live capture (or use BDD-created data).
2. Open History; confirm date order, grouping, square previews, PASS/FAIL badges, counts.
3. Rename a record inline; restart the app; confirm the new name persists.
4. Open a FAIL record; confirm boxes align; click each box and verify diagnostics change.
5. Zoom to maximum and minimum; pan to each corner; confirm clamps.
6. Reset view; confirm exact fit.
7. Crop at a zoomed position; open the PNG from the toast path; confirm it matches the viewport.
8. Delete/move a source image, reopen its record; confirm the unavailable state.
9. Switch theme to Light and repeat steps 2–7; repeat at 150% DPI.

---

## 7. Merge Readiness & Definition of Done

Phase 7 may merge into `main` only when:

1. All six task groups in `plan.md` are implemented and checked.
2. `dotnet build pcb-deftek.slnx` succeeds with 0 errors and 0 warnings.
3. `dotnet test pcb-deftek.slnx` passes 100% (no skipped tests), with no regression against the 452-test Phase 6 baseline.
4. All functional acceptance criteria in §3 are verified; performance gates in §4 are met.
5. No new dependencies, no schema migration, no hardcoded user paths.
6. Clean Git history following the 6-step lifecycle in `AGENTS.md`.
7. Pull Request is linked to its issue and passes the 4-step Feature Review and Quality Improvement pipeline (`code-review-and-quality`, `code-simplification`, `performance-optimization`, `security-and-hardening`).
8. Human review approval is recorded before merge; the PR is not self-merged.

---

## 8. Failure Triage

| Symptom | First check |
|---|---|
| All gallery badges show PASS | `InspectionSummary.DefectCount` projection / SQL column selection |
| Gallery stalls while scrolling | Thumbnails generated on UI thread or missing concurrency cap |
| Duplicate cards after Load More | Off-by-one `PageNumber` or missing loaded-ID guard |
| Boxes offset from image | Overlay not inside the same transformed layer, or normalized/pixel mix-up |
| Crop captures wrong region | `GetVisibleNormalizedRect` clamp or DPI scaling error |
| Rename disappears after reload | `UpdateNameAsync` not called or name updated only in memory |
| UI freezes on viewer open | Image decoded synchronously on the UI thread |
| Viewer shows doubled boxes | Rendering `_annotated.png` instead of the raw `ImagePath` |
