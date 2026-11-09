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
- Build Fluent shell window with modular view navigation and ViewModel-DataTemplate mapping.
- Implement Settings modal: confidence threshold, 9 defect class toggles, CPU/GPU selector, startup page selector, and model/developer metadata.
- Persist settings to local SQLite configuration.

### Phase 5: File & Batch Detection Engine
- Implement file service for single image, multi-image, video, and recursive folder inspection.
- Asynchronous batch worker with `IProgress<T>` reporting and cancellation support.
- Windows completion toast notification.
- Persistence of batch inspection sessions and defect bounding boxes.

### Phase 6: Live Camera Inspection Module
- Implement camera frame acquisition service (built-in/USB webcam).
- Background real-time inference loop off UI thread with live FPS and latency meters.
- Live inspection view: PASS/FAIL status banner, live defect count, real-time confidence slider.
- Frame capture on `Space` shortcut or capture button, committing snapshot to inspection history.

### Phase 7: History & Interactive Defect Viewer
- History view with date-based grouping and thumbnail cards (PASS/FAIL badge, editable filename).
- High-performance viewer: pan, smooth zoom (scroll & touchpad), and reset view button.
- Bounding box selection: click-to-select defect with auto-zoom and gradual zoom-out.
- Defect detail panel: Class, Confidence, Root Cause, Impact, Severity, Recommended Action.
- Region cropping tool with user zoom-out guidance notice.

### Phase 8: Defect Statistics Dashboard
- Dashboard view (default startup option).
- Aggregate metrics: total PCBs inspected, total defects detected, PASS/FAIL rate, mode breakdown (camera vs file).
- Defect distribution charts and expandable accordion view for defect class details.
- Filter bar: Day, Week, Month, Custom Date Range, and Severity filter.

### Phase 9: Audit-Ready PDF Export Engine
- QuestPDF report layout: inspection summary, defect distribution charts, inspection images with bounding box overlays, and defect metadata table.
- Dashboard "Export Data" integration with save-file dialog and background generation.

### Phase 10: Hardening, BDD Verification & Launch Readiness
- End-to-end Reqnroll BDD scenarios verification across all critical workflows.
- Performance verification: memory footprint, UI responsiveness under continuous camera inference.
- CI/CD build and test automation.
- Execute launch readiness checklist using `shipping-and-launch` skill.
