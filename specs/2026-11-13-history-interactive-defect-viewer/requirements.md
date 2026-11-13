# Requirements & Context: Phase 7 — History & Interactive Defect Viewer

## 1. Executive Summary & Context

Phase 7 delivers the **Inspection History** and **Interactive Defect Viewer** for `pcb-deftek`. It turns the persisted inspection records written by Phases 5 and 6 into a browsable, date-grouped gallery with inline rename, plus a high-performance viewer for panning, zooming, focusing individual defect bounding boxes, reading root-cause diagnostics, and exporting the visible viewport as a new image.

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
| Status filter | Applied in SQL via `DefectCount = 0` / `> 0`, which works | Reuse filter semantics |
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
   - Groups cards by **local calendar date** with date headers.
   - Card shows a square (1:1) preview, PASS/FAIL badge in the top-left corner, record name, timestamp, mode, and defect count.
   - Double-click the name to rename inline; Enter commits, Escape cancels, losing focus commits.
   - Empty, loading, and error states.
   - "Load more" paging (no numbered pager).
2. **Interactive defect viewer**
   - Opens as an in-page modal overlay from a gallery card; closes via button or `Esc`.
   - Main viewing area: pan, smooth wheel/touchpad zoom, reset view, visible-viewport crop.
   - Defect bounding boxes rendered with the established class colors; clicking a box focuses (zooms/centers) that defect.
   - Information sidebar with two exclusive states:
     - **Default:** editable record name, detection timestamp, defect-class summary with confidence.
     - **Focused:** class name, confidence, severity, root cause, impact, recommended action.
   - Missing/corrupt image produces a clear unavailable state, not a crash.
3. **Enablers**
   - `InspectionSummary` read projection so the gallery gets accurate defect counts without loading defect rows.
   - Shared defect color source usable from both OpenCV annotation and WPF overlays.
   - `IViewportCropService` using OpenCvSharp to crop the source image at native resolution.
4. **Verification** — unit, integration, and Reqnroll BDD coverage; DI registration assertions.

### 2.2 Out of Scope (Deferred)

- Deleting inspections or gallery multi-select (not in mission/UI spec).
- History filtering by date/status/mode in the UI (SQL support exists, but Phase 7 UI does not expose filters).
- Copying or relocating source images; Phase 7 never modifies original files.
- Creating a new inspection record from a cropped image.
- Dashboard metrics/charts (Phase 8) and PDF export (Phase 9).
- Large-video handling, custom capture paths, and branding (future enhancements).
- Persisting thumbnails to disk; thumbnails are in-memory only.
- Custom crop export location; the crop uses the standard Downloads default.

---

## 3. Decisions & Rationale

| # | Decision | Rationale |
|---|---|---|
| D1 | Add a new `InspectionSummary` projection plus `GetSummariesPagedAsync`; leave `GetPagedAsync` unchanged | Additive API (no caller breakage). The existing paged query cannot report `DefectCount` because the aggregate computes it from an unloaded `Defects` collection. Loading defects per page would be an N+1. |
| D2 | Gallery previews are decoded from the raw image **without** annotation overlays | UI spec only requires a square preview. Annotated thumbnails would require loading defect rows per card (N+1) or re-running OpenCV annotation for each card. Boxes belong in the viewer. |
| D3 | Thumbnails decode at 240 px via `BitmapImage.DecodePixelWidth`, on a background thread, then `Freeze()` | WPF decoding is faster than OpenCV for previews, avoids native buffers, and frozen bitmaps are safe to hand to the UI thread. |
| D4 | Square previews use `Stretch="UniformToFill"` inside a clipped square container | Delivers the 1:1 requirement without a second crop pass; deterministic and cheap. |
| D5 | Viewer draws bounding boxes as WPF elements over the **raw** image, using normalized coordinates | Avoids double-drawing on `_annotated.png` and enables hit-testing/focus per defect. |
| D6 | One shared defect palette: add `DefectClassInfo.ColorHex` and make `ImageAnnotationService` derive its OpenCV scalar from it | Hyrum's Law / single source of truth: two independent color tables would drift. |
| D7 | Crop uses the **normalized** visible rectangle and crops the source image at **native** resolution into the Windows Downloads folder | Decouples crop correctness from display decode size, preserves quality, and matches the project's existing export-location convention. No new DB record. |
| D8 | Extract `KnownFolderPaths.GetDownloadsDirectory()` and reuse it in `SqliteDatabaseExportService` and the crop service | Avoids duplicating the current Downloads-resolution fallback logic. |
| D9 | Group by `CreatedAtUtc.ToLocalTime().Date`; storage stays UTC | "Date-grouped" for a user means the local date they saw on the day of inspection. |
| D10 | Paging: page size 60, append on "Load more", stable order `CreatedAtUtc DESC, Id DESC` | Prevents duplicates when multiple records share a timestamp; avoids loading the entire history. |
| D11 | Viewer is an in-page overlay within `HistoryView`, not a separate WPF `Window` | Keeps navigation, theming, and lifecycle consistent with the shell; no extra window chrome. |
| D12 | Rename changes only the record label in SQLite; source files are never renamed | `ImagePath` points at user-owned files (file mode); renaming them would be destructive and surprising. |
| D13 | Viewer decodes the source at native resolution (no downscale) | Defect focus must stay crisp at high zoom. Files are loaded one at a time. |
| D14 | Crop output name: `{recordName}_crop_{yyyyMMdd-HHmmss}.png`, sanitized and collision-suffixed | Human-readable exports; no overwrites; names are user-editable so sanitization is required. |
| D15 | Zoom range 0.5×–10× of fit; pan is clamped so the image cannot leave the viewport | Predictable interaction and no "lost image" state. |
| D16 | No new NuGet packages; no database schema changes | All required capability is present (`OpenCvSharp4`, WPF, existing repository). Ask-first constraints satisfied. |

---

## 4. UI Contract (from `UI-INITIAL.md`)

### 4.1 History Page

- Date-grouped, gallery-style layout.
- Each card: square 1:1 image preview; PASS/FAIL badge top-left; filename below the thumbnail.
- Default filename format `Date-Number` (already produced by capture/batch flows; the gallery does not generate names).
- Double-click filename starts inline editing.
- Clicking a thumbnail opens the Interactive Image Viewer.

### 4.2 Interactive Image Viewer

- Main area + right-hand information sidebar.
- Pan and zoom via mouse scroll and touchpad gestures.
- Interactive defect bounding boxes; clicking one zooms/focuses it.
- Floating Reset View button bottom-right; floating Crop button bottom-right.
- **Default sidebar state:** editable filename, detection timestamp, summary of defect classes with confidence scores.
- **Focused sidebar state:** class name, confidence, severity, root cause, impact, recommended action.

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
- Persisted record names are the only mutable field in this phase; no schema changes.

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

---

## 7. Open Questions (non-blocking defaults chosen)

1. **Crop destination** — not specified. Default chosen: Windows Downloads (D7/D8), toast with the full path. Revisit if users want "next to source".
2. **Gallery preview annotations** — not specified. Default chosen: raw preview (D2). Revisit if QA asks to see boxes in the gallery.
3. **Delete from history** — not specified for this phase; excluded. Would need a confirmation dialog and file-retention policy in a later phase.

---

## 8. Success Definition

Phase 7 is complete when a QA engineer can open History, immediately recognize boards by their previews and PASS/FAIL badges, rename a record, open any record, inspect every defect by clicking its box, read the prescribed diagnostics, crop what they see to a PNG, and find the export notification — all without blocking the UI, touching source files, or regressing the Phase 6 suite.
