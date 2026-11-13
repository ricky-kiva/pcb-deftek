# Implementation Roadmap: PCB Deftek

## High-Level Phased Implementation

### Phase 1: Foundation & Core Infrastructure
- Setup DI container (`App.xaml.cs`) with service registration.
- Configure Serilog structured logging (file & debug sinks).
- Implement custom exception hierarchy (`/Common/Exceptions`) and centralized dispatcher.
- Establish xUnit test project and Reqnroll BDD project.

### Phase 2: Domain Models & Database Persistence
- Define domain entities: `DefectClass`, `DetectionResult`, `BoundingBox`, `InspectionRecord`, `ImageMetadata`.
- Propose and review SQLite database schema with human reviewer.
- Implement SQLite migrations runner, backup routine, and Repository pattern implementation.
- Unit and integration tests for data storage and retrieval.

### Phase 3: ONNX Inference Pipeline & Vision Utilities
- Implement ONNX wrapper around `Assets/Models/best.onnx`.
- Build preprocessing (letterbox resizing, RGB normalization) and postprocessing (NMS, confidence filtering, coordinate scaling).
- Configure DirectML GPU acceleration with automatic CPU fallback.
- Test inference accuracy and performance against benchmark PCB images.

### Phase 4: Application Shell, Navigation & Settings
- Build shell window using WPF's native Fluent Theme (`PresentationFramework.Fluent`, `ThemeMode="System"`, Mica backdrop) featuring:
  - Collapsible Sidebar Sheet on the left: Dashboard, Live Detection, File-Based Detection, History, and Settings (anchored at the bottom).
  - Compact footer with dot-separated (`·`) items: Social media icons (GitHub, LinkedIn, Instagram), Buy Me a Coffee link (Ko-fi), and developer portfolio link.
- Implement modular view navigation and ViewModel-DataTemplate mapping.
- Implement startup navigation routing: Default startup page selector and "Skip dashboard on startup" preference routing.
- Implement Database export service & routine (exporting SQLite database to user-configured path, default Windows Downloads folder, with completion Windows notification).
- Align defect class abbreviations (`SH`, `OP`, `MB`, `SP`, `SC`, `HB`, `CS`, `CFO`, `BMFO`) and diagnostic metadata contracts for UI view models.
- Implement Settings modal / panel:
  - Inference & Model Controls: Confidence threshold slider/input, dynamic detection device selector (CPU/GPU).
  - Application Preferences: Default startup page dropdown selector.
  - About & Attribution: Model metadata (`pcb-defect-yolov8m-dspcbsd` and developer links), Developer links (website, GitHub, Instagram, LinkedIn, Ko-fi).
  - Export: Report export location setting, Database export location setting, and manual Database Export button with Windows completion notification.
- Persist settings and user preferences to local SQLite configuration via repository.

### Phase 5: File & Batch Detection Engine
- Implement file service with drag-and-drop upload area supporting single-file, multi-file, and folder uploads (with Windows File Explorer dialog prompt).
- Asynchronous batch worker with `IProgress<T>` reporting, cancellation support, and inline estimated completion time (ETA) calculation.
- Send Windows completion toast notification when batch processing finishes.
- Persistence of batch inspection sessions and defect bounding boxes.

### Phase 6: Live Camera Inspection Module
- Implement camera frame acquisition service (built-in/USB webcam) with camera source selector dropdown.
- Background real-time inference loop off UI thread with live FPS and latency meters.
- Live inspection view: Full-screen video viewport displaying live camera feed with rendered defect bounding boxes (sidebar collapsed by default).
- Floating controls HUD:
  - Top-right 3-card vertical stack: First card (Live PASS/FAIL status badge, detected defect counter); Second card (Telemetry toggle + inference latency ms, FPS); Third card (Camera source selector dropdown).
  - Bottom-left: Confidence threshold slider.
  - Bottom-right: Capture button with `Space` keyboard shortcut hint, committing snapshot to inspection history.
- Auto-hide behavior: Gradually fade out configuration controls when cursor is stationary for a few seconds (retaining PASS/FAIL badge, defect counter, and telemetry if toggled).

### Phase 7: History & Interactive Defect Viewer
- History filter bar reusing the Dashboard filter structure:
  - Date Range Selector: Day, Week, and Month button group plus a custom date-range option.
  - Status filter: PASS/FAIL.
  - Defect Class filter: multi-select across all 9 classes; a record matches when it contains any selected class.
  - Filters compose and drive paged history queries.
- Multi-select gallery selection with confirmed batch deletion of inspection records, backed by transactional multi-record repository deletion.
  - Deletion policy: remove the inspection record and its defects; delete the associated app-owned capture files (`DetectionMode.LiveCamera`, under `%LOCALAPPDATA%\PcbDeftek\pcb-deftek\Captures`); never delete or modify user-owned source images referenced by `SingleFile` / `BatchFolder` records.
- History view with date-based grouping and gallery thumbnail cards (1:1 square preview, top-left PASS/FAIL badge, default `Date-Number` filename with double-click inline editing).
- High-performance Interactive Image Viewer:
  - Main viewing area: Pan and smooth zoom (scroll and touchpad gestures), interactive defect bounding boxes with click-to-focus/zoom, floating Reset View button, and floating Crop button (exports currently visible viewport as new image).
  - Image Information Sidebar on the right with two distinct states:
    - *Default State*: Editable filename field, detection timestamp, and summary list of detected defect classes with confidence scores.
    - *Focused State*: Selected defect details (Defect class name, confidence score, severity, root cause, impact, and recommended action).
  - Sidebar `Delete Image` action, available in both states: requires confirmation, deletes the inspection record plus its app-owned capture files under the policy above (user originals preserved), closes the viewer, and refreshes the gallery.

### Phase 8: Defect Statistics Dashboard
- Dashboard view (default startup view unless skipped):
  - Row 1 (Filters & Actions):
    - Left: Date Range Selector (Day, Week, Month button group + Custom date-range button), Defect Type multi-select Combobox (9 classes), Defect Severity multi-select dropdown (Critical, High, Medium-High, Medium, Low-Medium).
    - Right: PDF Export Data button, Notification button (modal displaying inference and data-export progress bars), and 1-column vs 2-column Metric Card Layout Toggle.
  - Row 2 (Metric Cards):
    - 4 metric cards: Total PCBs Inspected, Total Defects Detected, PASS/FAIL Inspection Rate (dual-line chart: green PASS line, red FAIL line), Defect Distribution Across Defect Classes (vertical bar chart with abbreviated labels `SH`, `OP`, etc.).
    - Card interactions: Drag-and-drop card reordering, click chart to open enlarged chart modal, and interactive hover feedback on data points.
  - Row 3 (Defect Class Information):
    - Single Accordion containing defect cards: Defect class name, two-column layout with severity + 1:1 square image (left) and 4 info sections (right: Explanation, Root Cause, Impact, Recommended Action).
  - Row 4 (Startup Preference):
    - "Skip dashboard on startup" checkbox navigating directly to Live Detection on subsequent launches.

### Phase 9: Audit-Ready PDF Export Engine
- QuestPDF report layout: All 4 metric cards arranged in a single column, followed by annotated PCB detection images with bounding boxes and corresponding diagnostic metadata.
- Dashboard "Export Data" integration: Asynchronous background PDF generation respecting currently applied filters, defaulting export path to Windows Downloads folder with Windows completion notification.

### Phase 10: Hardening, BDD Verification & Launch Readiness
- End-to-end Reqnroll BDD scenarios verification across all critical workflows (including batch ETA, camera auto-hide, viewer sidebar state transitions, dashboard drag-and-drop, and export notifications).
- Performance verification: memory footprint, UI responsiveness under continuous camera inference.
- CI/CD build and test automation.
- Execute launch readiness checklist using `shipping-and-launch` skill.
