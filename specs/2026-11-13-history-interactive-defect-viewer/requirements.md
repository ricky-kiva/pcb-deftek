# Requirements & Context: Phase 7 — History & Interactive Defect Viewer

## 1. Executive Summary & Context

Phase 7 delivers the **Inspection History** and **Interactive Defect Viewer** for `pcb-deftek`. It turns the persisted inspection records written by Phases 5 and 6 into a browsable, filterable, date-grouped gallery with inline rename and safe multi-select deletion, plus a high-performance viewer for panning, zooming, focusing individual defect bounding boxes, reading root-cause diagnostics, exporting the visible viewport as a new image, and deleting the current inspection from the sidebar.

This phase builds directly on:

- `specs/roadmap.md` — Phase 7: History & Interactive Defect Viewer.
- `specs/mission.md` — Section 3 (QA Engineers & Inspectors), Section 5.1 (Inspection History, Interactive Defect Viewer).
- `specs/tech-stack.md` — Section 2 (OpenCvSharp4 viewport cropping, WPF Fluent UI, SQLite repository), Section 4 (MVVM purity, repository pattern), Section 5 (async off-UI-thread, no hardcoded user folders).
- `UI-INITIAL.md` — Sections 5 (Detection History Page) and 6 (Interactive Image Viewer).
- Completed Phase 6 (`specs/2026-11-13-live-camera-inspection-module/`) — live snapshots now produce `DetectionMode.LiveCamera` records and capture images.

### Current State (verified in code)

| Area | Current state | Phase 7 impact |
|---|---|---|
| `HistoryViewModel` | Empty placeholder (`Title => "History"`) | Full rewrite |
| `HistoryView.xaml` | Static "Phase 7 delivery" placeholder panel | Full rewrite |
| `IInspectionRepository.GetPagedAsync` | Returns `InspectionRecord` **without defects**; `DefectCount` and `Status` are computed from the empty collection, so every paged record reports `PASS`/0 defects | History cannot use this as-is; add a summary projection |
| `InspectionQueryFilter` | Supports date range, a single `Status`, `Mode`, and paging; no defect-class filter and no multi-status selection | Extend additively for History filters |
| `IInspectionRepository.DeleteAsync` | Deletes a single record; defects cascade | Add a transactional multi-record delete for batch selection |
| Deletion support | No deletion service, confirmation abstraction, or capture-file cleanup; deleting a LiveCamera row would orphan its images | Add `IInspectionDeletionService`, `IConfirmationService`, and capture-root containment |
| Status filter | Applied in SQL via `DefectCount = 0` / `> 0`, which works | Reuse and extend to a multi-select set |
| Record names | Mutable through `IInspectionRepository.UpdateNameAsync`; no uniqueness constraint | Inline rename uses this API |
| `ImagePath` (file/batch) | Points at the **original user file** | Viewer must handle moved/deleted files |
| `ImagePath` (live camera) | Points at `%LOCALAPPDATA%\PcbDeftek\pcb-deftek\Captures\{id}_raw.png`; annotated sibling `{id}_annotated.png` exists | Viewer must use the raw image so boxes are not drawn twice |
| Bounding boxes | Normalized `0.0–1.0` in `BoundingBox` (already rendered by `ImageAnnotationService`) | Overlay geometry is resolution-independent |
| Defect colors | Private switch in `ImageAnnotationService.GetDefectColor` (OpenCV BGR `Scalar`) | Viewer needs the same colors in WPF; extract one source of truth |
| Viewer primitives | None (no zoom/pan/crop code); `IconZoomReset` and `IconCrop` already exist in `Icons.xaml` | Build the viewer from scratch |
| Gallery primitives | `StatusPass*` / `StatusFail*` brushes and `IconImage` placeholder exist | Reuse theme tokens |
| Test baseline | 452 tests passing (432 unit + 20 BDD) | New tests must not regress this |

---

## 2. Scope

### 2.1 In Scope

1. **History gallery**
   - Loads persisted inspections from SQLite, newest first, paginated.
   - Filter bar mirroring the Dashboard filter structure:
     - Date Range Selector: Day / Week / Month buttons plus a custom date-range option; default is all time.
     - Status filter: multi-select PASS/FAIL; empty selection means both.
     - Defect Class filter: multi-select across all nine classes; a record matches when it contains at least one selected class.
     - Filters compose (AND across categories, OR within a category) and drive server-side paged queries.
   - Groups cards by **local calendar date** with date headers.
   - Card shows a square (1:1) preview, PASS/FAIL badge in the top-left corner, record name, timestamp, mode, and defect count.
   - Double-click the name to rename inline; Enter commits, Escape cancels, losing focus commits.
   - Empty, loading, error, and "no matches for filters" states.
   - "Load more" paging (no numbered pager).
   - Multi-select selection via card checkboxes, "Select loaded" and "Clear selection" actions.
   - Confirmed batch deletion of selected inspections, applying the deletion policy in D22.
2. **Interactive defect viewer**
   - Opens as an in-page modal overlay from a gallery card; closes via button or `Esc`.
   - Main viewing area: pan, smooth wheel/touchpad zoom, reset view, visible-viewport crop.
   - Defect bounding boxes rendered with the established class colors; clicking a box focuses (zooms/centers) that defect.
   - Information sidebar with two exclusive states:
     - **Default:** editable record name, detection timestamp, defect-class summary with confidence.
     - **Focused:** class name, confidence, severity, root cause, impact, recommended action.
   - Sidebar `Delete Image` action, available in both states, with confirmation (deletes the inspection under the same policy).
   - Missing/corrupt image produces a clear unavailable state, not a crash.
   - Cropping writes the visible viewport to the configured crop directory and inserts a new inspection record containing the defects intersecting that viewport, with boxes re-normalized to the crop.
3. **Enablers**
   - `InspectionSummary` read projection so the gallery gets accurate defect counts without loading defect rows.
   - Query-filter extension for date presets, multi-status, and defect-class matching.
   - `IInspectionDeletionService` for transactional multi-record deletion and app-owned capture cleanup.
   - `IConfirmationService` for mandatory destructive-action confirmation.
   - Shared defect color source usable from both OpenCV annotation and WPF overlays.
   - `IViewportCropService` using OpenCvSharp to crop the source image at native resolution.
4. **Verification** — unit, integration, and Reqnroll BDD coverage; DI registration assertions.

### 2.2 Out of Scope (Deferred)

- Undo/trash/soft-delete; deletion is permanent after confirmation.
- Filtering by detection mode or by individual defect severity in the History UI (SQL supports mode; not requested).
- "Select all matching filters" across unloaded pages; selection is limited to loaded cards.
- Copying or relocating source images; Phase 7 never modifies user-owned originals.
- Dashboard metrics/charts (Phase 8) and PDF export (Phase 9).
- Large-video handling, custom capture paths, and branding (future enhancements).
- Persisting thumbnails to disk; thumbnails are in-memory only.

---

## 3. Decisions & Rationale

| # | Decision | Rationale |
|---|---|---|
| D1 | Add a new `InspectionSummary` projection plus `GetSummariesPagedAsync`; keep `GetPagedAsync`'s signature and return shape unchanged | Additive API (no caller breakage). The existing paged query cannot report `DefectCount` because the aggregate computes it from an unloaded `Defects` collection. Loading defects per page would be an N+1. Filter semantics are extended for both queries in D18. |
| D2 | Gallery previews are decoded from the raw image **without** annotation overlays | UI spec only requires a square preview. Annotated thumbnails would require loading defect rows per card (N+1) or re-running OpenCV annotation for each card. Boxes belong in the viewer. |
| D3 | Thumbnails decode at 240 px via `BitmapImage.DecodePixelWidth`, on a background thread, then `Freeze()` | WPF decoding is faster than OpenCV for previews, avoids native buffers, and frozen bitmaps are safe to hand to the UI thread. |
| D4 | Square previews use `Stretch="UniformToFill"` inside a clipped square container | Delivers the 1:1 requirement without a second crop pass; deterministic and cheap. |
| D5 | Viewer draws bounding boxes as WPF elements over the **raw** image, using normalized coordinates | Avoids double-drawing on `_annotated.png` and enables hit-testing/focus per defect. |
| D6 | One shared defect palette: add `DefectClassInfo.ColorHex` and make `ImageAnnotationService` derive its OpenCV scalar from it | Hyrum's Law / single source of truth: two independent color tables would drift. |
| D7 | Crop uses the **normalized** visible rectangle and crops the source image at **native** resolution into the configured crop directory (default `Downloads/PCB Deftek/Images`). The exported crop is inserted into History as a new inspection whose bounding boxes are clipped to the crop and re-normalized against the cropped dimensions. | Decouples crop correctness from display decode size, preserves quality, and makes the crop directly inspectable from History. The inserted record keeps the diagnostic context instead of producing an orphan image. |
| D8 | Add `KnownFolderPaths` helpers for the app-owned `Downloads/PCB Deftek/{Images,Report,Backup}` roots and reuse the Downloads resolver in the export, backup, and crop services | One path convention for all generated artifacts; avoids duplicating the Downloads fallback logic. Settings can override the crop, report, and database paths. |
| D9 | Group by `CreatedAtUtc.ToLocalTime().Date`; storage stays UTC | "Date-grouped" for a user means the local date they saw on the day of inspection. |
| D10 | Paging: page size 60, append on "Load more", stable order `CreatedAtUtc DESC, Id DESC` | Prevents duplicates when multiple records share a timestamp; avoids loading the entire history. |
| D11 | Viewer is an in-page overlay within `HistoryView`, not a separate WPF `Window` | Keeps navigation, theming, and lifecycle consistent with the shell; no extra window chrome. |
| D12 | Rename changes only the record label in SQLite; source files are never renamed | `ImagePath` points at user-owned files (file mode); renaming them would be destructive and surprising. |
| D13 | Viewer decodes the source at native resolution (no downscale) | Defect focus must stay crisp at high zoom. Files are loaded one at a time. |
| D14 | Crop output name: `{recordName}_crop_{yyyyMMdd-HHmmss}.png`, sanitized and collision-suffixed | Human-readable exports; no overwrites; names are user-editable so sanitization is required. |
| D15 | Zoom range 0.5×–10× of fit; pan is clamped so the image cannot leave the viewport | Predictable interaction and no "lost image" state. |
| D16 | No new NuGet packages; no database schema changes | All required capability is present (`OpenCvSharp4`, WPF, existing repository). Ask-first constraints satisfied. |
| D17 | Extend `InspectionQueryFilter` additively with `Statuses` (set) and `DefectClasses` (set); keep the existing single `Status` property, with `Statuses` taking precedence when non-empty | Existing callers keep working; History gains multi-select semantics without a breaking change. |
| D18 | Apply History filtering in a shared SQL predicate builder used by both `GetPagedAsync` and `GetSummariesPagedAsync` | Prevents the two query paths from drifting; filter totals and pages stay consistent. |
| D19 | Class filter uses `EXISTS` against `Defects` and matches **any** selected class; defectless (PASS) records never match a class filter | Correct "contains any selected class" semantics without duplicating rows in the page query. |
| D20 | Date presets: Day = local today, Week = current local week (culture first day), Month = current local month, Custom = inclusive local start/end; no preset selected = all time | Matches user expectations; storage remains UTC and bounds are converted to UTC for SQL. |
| D21 | Filter changes reload from page 1 and clear selection; "Select loaded" selects only cards currently in the list | Predictable selection scope; no hidden server-side select-all across unloaded pages. |
| D22 | Deletion policy: delete database rows and their defects in one transaction first, then delete **only** app-owned capture files under the configured Captures root (raw + `_annotated` sibling). User-owned originals for `SingleFile`/`BatchFolder` are never modified or deleted. Missing files are ignored; cleanup failures are logged and reported, while the committed row deletion stands. | Matches the approved "delete app-owned captures, keep user originals" policy; DB-first ordering avoids orphaned rows pointing at already-deleted files. |
| D23 | One `IInspectionDeletionService` serves both batch gallery deletion and the viewer sidebar action; repository gains `DeleteManyAsync` (single transaction) and a single-query path lookup for the requested IDs | One behavior, one policy, one test surface; no duplicated deletion logic. |
| D24 | Destructive actions require confirmation through `IConfirmationService`; the WPF implementation is a native modal dialog whose default button is Cancel | Irreversible data loss must be explicit; a thin interface keeps view models testable. |
| D25 | The viewer's `Delete Image` deletes the **current inspection** (record + app-owned captures) under D22; on success it closes the viewer and removes the card, on failure it keeps the viewer open and reports the error | "Delete image" in the sidebar refers to the viewed inspection; leaving a record pointing at a deleted file would be inconsistent. |
| D26 | Gallery selection uses explicit per-card checkboxes; clicking the thumbnail still opens the viewer, clicking the checkbox only toggles selection | Avoids destructive ambiguity between "open" and "select". |
| D27 | Sidebar `Delete Image` is rendered in both sidebar states below the state content, with destructive styling and disabled state while deletion runs | The action must remain available when a defect is focused; the label and styling signal irreversibility. |

---

## 4. UI Contract (from `UI-INITIAL.md`)

### 4.1 History Page

- **Filter & Action Bar (Row 1, mirroring the Dashboard structure):**
  - **Left — Filters:**
    - **Date Range Selector:** Day, Week, Month button group plus a **Custom** date-range button. No button selected means **all time**; clicking the active button again clears the preset.
    - **Status Filter:** multi-select dropdown with `PASS` and `FAIL`; empty selection means both.
    - **Defect Class Filter:** multi-select combobox listing all nine classes with abbreviations (`SH`, `OP`, `MB`, `SP`, `SC`, `HB`, `CS`, `CFO`, `BMFO`); empty selection means all classes.
  - **Right — Actions:**
    - Result count (`N results`).
    - **Select loaded** and **Clear selection** actions.
    - **Delete Selected (N)** button, destructive styling, disabled when `N = 0` or deletion is running.
- **Selection:** each card has a checkbox (top-right); checking it selects the card without opening the viewer. Selected cards have a visible highlight. Selecting is limited to loaded cards.
- **Gallery:** date-grouped, gallery-style layout.
- Each card: square 1:1 image preview; PASS/FAIL badge top-left; filename below the thumbnail.
- Default filename format `Date-Number` (already produced by capture/batch flows; the gallery does not generate names).
- Double-click filename starts inline editing.
- Clicking a thumbnail opens the Interactive Image Viewer.
- **States:** no history at all → guidance to run File or Live detection; no matches for active filters → "No inspections match these filters" with a **Clear filters** action.
- **Delete confirmation:** modal stating how many inspections will be deleted and the retention policy — app-owned capture images are deleted, original source images are kept. Default action is Cancel.

### 4.2 Interactive Image Viewer

- Main area + right-hand information sidebar.
- Pan and zoom via mouse scroll and touchpad gestures.
- Interactive defect bounding boxes; clicking one zooms/focuses it.
- Floating Reset View button bottom-right; floating Crop button bottom-right.
- **Default sidebar state:** editable filename, detection timestamp, summary of defect classes with confidence scores.
- **Focused sidebar state:** class name, confidence, severity, root cause, impact, recommended action.
- **`Delete Image` action:** rendered in both sidebar states below the state content, with destructive styling. Requires confirmation; on success the inspection and its app-owned captures are deleted, the viewer closes, and the gallery refreshes. On failure the viewer stays open and an error notification is shown. Disabled while deletion runs or when the record is still loading.

### 4.3 Defect Taxonomy & Colors

All nine classes (`SH`, `OP`, `MB`, `SP`, `SC`, `HB`, `CS`, `CFO`, `BMFO`) are supported. Colors remain those established by `ImageAnnotationService` (Blue, Fern, Navy, Cyan, White, Pink, Red, Yellow, Green). White boxes must remain visible against light PCB imagery by using the existing contrasting border treatment (crisp outline; no change to palette values).

---

## 5. Technical Constraints

- MVVM: ViewModels hold no `System.Windows.Controls` references. Pure interaction (mouse capture, transforms, animations) lives in view code-behind or a custom control.
- All image decoding, cropping, and thumbnail work runs off the UI thread and is cancellable.
- UI thread must never block on file I/O or SQLite.
- Nullable reference types enabled; zero build warnings.
- Reuse existing theme brushes (`StatusPassBrush`, `StatusFailBrush`, `Severity*Brush`, `HudCard*Brush`), icons, and the `ImageAnnotationService` palette.
- Repository access only through `IInspectionRepository`; no SQL in ViewModels.
- Destructive operations go through `IInspectionDeletionService` and `IConfirmationService`; no direct `File.Delete` from views or view models.
- Capture-file deletion is restricted to paths that resolve under the configured Captures root; any other path is skipped and logged.
- Record deletion and rename are the only mutations in this phase; no schema changes.

---

## 6. Edge Cases & Error Handling

| Case | Expected behavior |
|---|---|
| Empty history | Empty state with guidance to run File or Live detection |
| Source image missing/moved (file mode) | Card placeholder + viewer "Image unavailable" state; no exception |
| Corrupt/unreadable image | Same as missing; log warning |
| Record name empty or whitespace | Revert to previous name; no DB write |
| Rename persistence failure | Revert UI, show error toast, log exception |
| Rename while a newer name is being typed elsewhere | Last committed write wins (no optimistic concurrency in schema); acceptable for single-user desktop |
| Date boundary (23:59 UTC) | Grouping uses local time; a record may shift date across time zones by design |
| Duplicate timestamps | Stable secondary sort by `Id` |
| Large image (e.g., 24 MP) | Viewer loads native; crop still correct; document memory expectation |
| Multiple defects overlapping | Boxes render in order; topmost hit-test wins; clicking selects the topmost |
| Crop with zero/edge viewport | Clamp to image bounds; refuse empty crop with a toast |
| Navigation away while loading | Cancellation token stops thumbnail/image work; no stale UI updates |
| `Esc` while renaming inline | Cancel rename, do not close viewer |
| Filter change while a page load is in flight | Cancel the previous load; only the latest filter result may update state |
| Custom range with start after end | Reject the range (disable Apply / show validation), keep the previous results |
| Filter combination with no matches | Show the "no matches" state with a Clear filters action |
| Status=PASS combined with a class filter | Valid filter, but returns no records (PASS records have no defects); show no-matches state |
| Delete with zero selected | Delete action disabled; no service call |
| Confirmation cancelled | No database or file changes; selection remains |
| Batch delete fails in the database | Transaction rolls back; no capture files are deleted; selection remains; error shown |
| Capture file missing or locked during delete | Row deletion stands; missing file ignored; cleanup failure logged and reported without failing the whole operation |
| Record path outside the Captures root | Never delete the file; log a warning and delete only the row (defensive ownership guard) |
| Deleting a `SingleFile` / `BatchFolder` record | Database row removed; the user's original image is untouched |
| Deleting the inspection currently open in the viewer (via gallery or sidebar) | Viewer closes without errors; card disappears from the gallery; count updates |
| Repeated delete clicks | Command disabled while running; no duplicate operations |
| Rename in progress while its card is deleted | Edit state is discarded; no write for a deleted record |

---

## 7. Open Questions (non-blocking defaults chosen)

1. **Crop destination** — configured in Settings, defaulting to `Downloads/PCB Deftek/Images`; completion toast reports the path and confirms the History entry.
2. **Gallery preview annotations** — not specified. Default chosen: raw preview (D2). Revisit if QA asks to see boxes in the gallery.
3. **Confirmation surface** — default chosen: native WPF modal dialog via `IConfirmationService` (D24). Revisit if a fully themed in-app dialog is required.

---

## 8. Success Definition

Phase 7 is complete when a QA engineer can open History, filter it by date preset or custom range, status, and defect class, recognize boards by their previews and PASS/FAIL badges, rename a record, select one or many inspections and delete them after confirming the retention policy, open any record, inspect every defect by clicking its box, read the prescribed diagnostics, crop what they see to a PNG, delete the inspected record from the viewer sidebar, and find the resulting notifications — all without blocking the UI, deleting user-owned originals, or regressing the Phase 6 suite.
