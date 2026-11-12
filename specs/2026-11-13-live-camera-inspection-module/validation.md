# Validation Criteria: Phase 6 — Live Camera Inspection Module

## 1. Build & Quality Gates
- **Build Command:** `dotnet build pcb-deftek.csproj`
- **Pass Condition:** Exit code 0, 0 compiler errors, 0 compiler warnings.
- **Nullable Reference Types:** Nullable strictly enabled across all new services, models, ViewModels, and views with zero warnings.
- **Dependency Audit:** Socket CLI (`socket`) verification for any new dependency (none required — `OpenCvSharp4` is already installed and verified).

---

## 2. Automated Test Suite Gates
- **Test Command:** `dotnet test pcb-deftek.slnx`
- **Pass Condition:**
  - 100% tests passing, 0 failed, 0 skipped.
  - All existing 405 tests (Persistence, Inference, Application Shell, Settings, File Detection, BDD) continue to pass without regression.
  - All new unit, integration, and BDD tests for Phase 6 pass cleanly.

---

## 3. Functional Acceptance Criteria

### 3.1. Camera Hardware Abstraction & Discovery
- **Enumeration:** `ICameraService.GetAvailableCamerasAsync()` returns a list of connected camera devices with friendly names and valid device indices.
- **Switching:** Selecting a different camera from the dropdown reconnects the video capture stream to the chosen device without error.
- **Graceful Fallback:** If no physical camera hardware is connected (e.g. running in CI or headless VM), the service safely falls back to a simulated frame test mode or reports an empty device list without throwing unhandled exceptions.
- **Resource Disposal:** Navigating away from the Live Camera page cleanly calls `StopCaptureAsync()` and disposes the `VideoCapture` handle, releasing hardware locks immediately.

### 3.2. Real-Time Video Streaming & Viewport Rendering
- **Background Execution:** Video capture and ONNX neural network inference execute exclusively off the UI thread.
- **Frame Dropping / Throttling:** When inference execution time exceeds camera frame interval, stale frames are discarded so the operator experiences sub-second live video with zero frame queue latency.
- **Aspect Ratio:** Video stream renders in the full-screen viewport with `Stretch="Uniform"`, preserving the camera's native aspect ratio against a dark background.

### 3.3. Live Defect Classification & Bounding Box Overlays
- **Defect Detection:** Detects and localizes defects across all 9 defect classes (`SH`, `OP`, `MB`, `SP`, `SC`, `HB`, `CS`, `CFO`, `BMFO`).
- **Color-Coded Overlays:** Bounding boxes and badges render using defect-specific colors (e.g. Blue for Short, Fern for Open, Pink for Hole breakout) with high-contrast text.
- **Real-Time Classification:**
  - Status badge displays solid green `PASS` when defect count is 0.
  - Status badge displays solid red `FAIL` when 1 or more defects are detected.
  - Defect counter displays exact number of detected defects on the active frame.

### 3.4. Telemetry Readouts & Toggle Behavior
- **Telemetry Card:** Second card in the top-right HUD stack displays live inference latency (in milliseconds) and live FPS.
- **Rolling FPS:** FPS reflects a 1-second rolling window.
- **Toggle Control:** Clicking the telemetry toggle enables or disables the display of latency and FPS numbers.
- **Visibility Invariance:** When the telemetry toggle is enabled, telemetry readouts remain visible even during cursor idle auto-hide.

### 3.5. Dynamic Confidence Threshold Adjustment
- **Slider Control:** Bottom-left floating slider adjusts the detection threshold from `0.10` to `0.95` (default `0.25`).
- **Live Response:** Adjusting the slider immediately updates model inference filtering on subsequent video frames without restarting the camera or stream.
- **Readout:** Slider is accompanied by a clear numerical/percentage readout.

### 3.6. Frame Snapshot Capture & SQLite Persistence
- **One-Touch Trigger:** Pressing the `Space` key or clicking the bottom-right Capture button triggers a frame snapshot.
- **Persistence:**
  - Saves the raw and annotated frame images to local application data storage.
  - Creates an `InspectionRecord` in SQLite with `DetectionMode.LiveCamera`.
  - Persists all detected bounding boxes, defect classes, confidence scores, and diagnostic metadata.
- **Operator Feedback:** Shows immediate visual feedback upon capture (e.g. status flash or brief toast notification).

### 3.7. Stationary Cursor Auto-Hide Interaction
- **Idle Detection:** When the operator's mouse cursor remains stationary over the video viewport for 3 seconds, configuration controls smoothly fade out to `Opacity = 0.0`.
- **Restoration:** Any mouse movement, touch gesture, or keypress instantly restores controls to `Opacity = 1.0`.
- **Persistent Elements (NEVER Hidden):**
  - Live PASS/FAIL status badge (Card 1).
  - Detected defect counter (Card 1).
  - Telemetry display (Card 2), **only when its toggle is enabled**.

### 3.8. Sidebar Auto-Collapse Integration
- **Navigation Signal:** When navigating to `LiveDetectionViewModel`, the main shell window automatically collapses the left sidebar sheet to maximize viewing area.
- **Manual Control:** The sidebar remains accessible via the top-left hamburger menu.

---

## 4. Performance & Stability Gates
- **UI Responsiveness:** The WPF UI thread must maintain a steady 50–60 FPS during continuous background inference and camera streaming.
- **Memory Footprint:** Continuous streaming over a 5-minute run must exhibit a flat memory curve (no GC leaks from OpenCV `Mat` buffers or unmanaged `BitmapSource` allocations).
- **Sub-Second Latency:** Total end-to-end latency from camera sensor capture to on-screen overlay display must remain <100ms on DirectML GPU and <250ms on CPU fallback.

---

## 5. Merge Readiness & Definition of Done
A feature branch for Phase 6 may only be merged into `main` when:
1. All 6 task groups in `plan.md` have been fully implemented.
2. `dotnet build` succeeds with 0 errors and 0 warnings.
3. `dotnet test pcb-deftek.slnx` passes 100% of all unit, integration, and Reqnroll BDD tests.
4. Clean Git history following the 6-step lifecycle in `AGENTS.md`.
5. Pull Request passes the 4-step Feature Review and Quality Improvement pipeline (`code-review-and-quality`, `code-simplification`, `performance-optimization`, `security-and-hardening`).
