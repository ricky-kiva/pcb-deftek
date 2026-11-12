# Requirements & Context: Phase 6 — Live Camera Inspection Module

## 1. Executive Summary & Context

Phase 6 implements the **Live Camera Inspection Module** for `pcb-deftek`. It provides real-time, on-line video defect detection and classification directly from connected camera hardware (built-in webcams, external USB inspection cameras, or industrial UVC video inputs).

This phase builds upon the ONNX inference pipeline (Phase 3), persistence infrastructure (Phase 2), and application shell navigation (Phase 4), fulfilling the core requirements specified in:
- `specs/roadmap.md` (Phase 6: Live Camera Inspection Module)
- `specs/mission.md` (Section 3: Assembly Line Technicians, Section 5.1: Live Camera Detection)
- `specs/tech-stack.md` (Section 2: OpenCvSharp4 camera capture and async video processing)
- `UI-INITIAL.md` (Section 3: Live Camera / Live Detection Page)

Upon completion, assembly line operators and QA technicians can view a live video feed of printed circuit boards, observe real-time localized defect bounding boxes, monitor inspection telemetry (latency ms and FPS), adjust detection sensitivity on the fly, and capture inspection snapshots to SQLite history with a single tap or keyboard shortcut (`Space`).

---

## 2. Architectural Decisions & Technical Scope

### 2.1. Camera Device Abstraction (`ICameraService` & `CameraDeviceInfo`)
- **OpenCV Integration:** Utilize `OpenCvSharp4` (`VideoCapture`) for video frame capture from DirectShow / Media Foundation video capture drivers on Windows.
- **Device Enumeration:** Enumerate available video capture hardware devices with friendly display names and indices.
- **Dynamic Device Switching:** Allow the operator to select and switch between connected cameras via a dropdown selector without restarting the application.
- **Graceful Lifecycle Management:** Explicit `StartCaptureAsync`, `StopCaptureAsync`, and `Dispose` routines that cleanly release camera hardware handles and avoid device lockups when switching pages.
- **Testability & Decoupling:** Abstract all camera interaction behind `ICameraService` with event/stream-based frame delivery (`FrameReceived`) so ViewModels and inference engines can be tested with mock frame streams.

### 2.2. Real-Time Inference & Telemetry Engine (`ILiveInspectionEngine`)
- **Off-UI-Thread Execution:** Frame acquisition and ONNX neural network inference run exclusively on dedicated background worker tasks, never blocking WPF's Dispatcher UI thread.
- **Frame Drop / Throttling Mechanism:** If ONNX model inference takes longer than camera frame acquisition (e.g. running on lower-powered CPU), stale frames are immediately discarded and only the most recent frame is evaluated. This guarantees zero frame lag and sub-second visual responsiveness.
- **Hardware Acceleration:** Leverage `IPcbDefectDetector` with DirectML GPU acceleration and automatic CPU fallback.
- **Live Telemetry Tracking:**
  - **Inference Latency (ms):** Instantaneous and rolling-average model execution time per frame.
  - **FPS (Frames Per Second):** Live frame rate calculated over a 1-second rolling window.
  - **Live PASS/FAIL Classification:** Real-time state determination: `PASS` when defect count is 0; `FAIL` when 1 or more defects are detected.
  - **Defect Counter:** Live count of detected defects on the active frame.

### 2.3. Annotation & Video Viewport Rendering
- **Annotation Pipeline:** Draw bounding boxes, class abbreviations (`SH`, `OP`, `MB`, `SP`, `SC`, `HB`, `CS`, `CFO`, `BMFO`), and confidence labels using the established color-coding system (`ImageAnnotationService`).
- **High-Performance Bitmap Conversion:** Convert annotated OpenCV `Mat` frames to thread-safe frozen `BitmapSource` instances (utilizing uncompressed BMP memory buffers for sub-millisecond conversion latency).
- **Responsive Viewport:** Full-screen video viewport scaling with `Stretch="Uniform"` to maintain native camera aspect ratio without stretching or distortion.

### 2.4. Floating Controls HUD (Heads-Up Display)
Following `UI-INITIAL.md` (Section 3), floating controls are overlayed on top of the live video viewport:
1. **Top-Right 3-Card Vertical Stack:**
   - **Card 1 (Inspection Status):**
     - Live PASS/FAIL status badge (solid forest green for PASS, solid crimson red for FAIL).
     - Detected defect counter badge (`0 Defects` or `N Defects`).
   - **Card 2 (Telemetry):**
     - Toggle button for enabling/disabling telemetry metrics.
     - Telemetry readouts: Inference latency in milliseconds (`XX ms`) and current framerate (`XX FPS`).
   - **Card 3 (Camera Selector):**
     - Dropdown `ComboBox` listing available camera input devices.
2. **Bottom-Left Floating Control:**
   - Horizontal slider for dynamic confidence threshold adjustment (`0.10` to `0.95`, default `0.25`).
   - Real-time percentage readout (`25%`).
3. **Bottom-Right Floating Control:**
   - Prominent Capture button committing the current frame to persistent inspection history.
   - Distinct keyboard shortcut hint badge displaying `[Space]`.

### 2.5. Stationary Cursor Auto-Hide Behavior
- **Idle Detection:** A lightweight timer detects when the operator's mouse cursor remains stationary over the live viewport for a configured threshold (default 3 seconds).
- **Gradual Fade Out:** Configuration controls (camera selector dropdown, confidence slider, capture button, and card 2 telemetry if toggle is disabled) gradually fade out (`Opacity` animates from `1.0` to `0.0`).
- **Instant Restore:** Any mouse movement or touch/keyboard interaction immediately restores controls to full visibility (`Opacity = 1.0`).
- **Inviolable Persistent Elements (NEVER Hidden):**
  - Live PASS/FAIL status badge (Card 1).
  - Detected defect counter (Card 1).
  - Telemetry display (Card 2), **only when its toggle is enabled**.

### 2.6. Frame Snapshot & Inspection History Persistence
- **One-Touch Capture:** Triggered by clicking the Capture button or pressing `Space` while on the Live Camera page.
- **Storage Workflow:**
  - Encodes the current unannotated raw frame and annotated inspection image to local application storage.
  - Creates an `InspectionRecord` in the SQLite database via `IInspectionRepository.InsertAsync` with `DetectionMode.LiveCamera`.
  - Persists all detected `BoundingBox` entities, defect classes, confidence scores, and diagnostic metadata.
- **Feedback:** Displays immediate visual confirmation (subtle toast notification or temporary status indicator).

### 2.7. Shell Navigation & Sidebar Integration
- **Sidebar Auto-Collapse:** In accordance with `UI-INITIAL.md` (Section 3), when navigating to `LiveDetectionViewModel`, the main application shell collapses the left sidebar sheet by default to maximize the video viewport area.
- **Page Deactivation Cleanup:** When navigating away from `LiveDetectionViewModel`, the background camera stream and inference worker are cancelled and disposed immediately to release hardware resources.

---

## 3. Defect Taxonomy Reference (9 Classes)
All 9 classes are detected, color-coded, and labeled according to the established project standard:
1. `SH` — Short (Blue)
2. `OP` — Open (Fern)
3. `MB` — Mouse bite (Dark Blue)
4. `SP` — Spur (Cyan)
5. `SC` — Spurious copper (White)
6. `HB` — Hole breakout (Pink)
7. `CS` — Conductor scratch (Red)
8. `CFO` — Conductor foreign object (Yellow)
9. `BMFO` — Base material foreign object (Green)

---

## 4. Out-of-Scope (Deferred to Later Phases)
- **Historical Gallery View & Thumbnails:** Belongs to Phase 7 (`HistoryView`).
- **Interactive Deep-Zoom Image Viewer & Defect Details Sidebar:** Belongs to Phase 7 (`InteractiveDefectViewer`).
- **Aggregate Quality Analytics & Trend Charts:** Belongs to Phase 8 (`DashboardView`).
- **Audit-Ready PDF Export Engine:** Belongs to Phase 9 (`QuestPDF` export).
