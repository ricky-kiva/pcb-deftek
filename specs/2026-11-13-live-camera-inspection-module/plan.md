# Implementation Plan: Phase 6 — Live Camera Inspection Module

## Overview
Phase 6 implements the complete real-time **Live Camera Inspection Module** for `pcb-deftek`. It provides automated, on-line defect detection from connected video hardware (built-in webcams, external USB inspection cameras, or industrial UVC video inputs), real-time overlay annotation, live telemetry monitoring (latency ms and FPS), floating HUD controls, stationary cursor auto-hide behavior, and one-touch frame snapshot persistence into SQLite history.

---

## Architecture & Dependency Graph

```
┌────────────────────────────────────────────────────────┐
│  Infrastructure / Hardware Services                    │
│  • CameraDeviceInfo (Model)                            │
│  • ICameraService / OpenCvCameraService                │
│    └── OpenCvSharp4 VideoCapture (DirectShow/MF)       │
└────────────────────────────────────────────────────────┘
                           │
                           ▼
┌────────────────────────────────────────────────────────┐
│  Live Inference & Telemetry Engine                     │
│  • LiveInspectionTelemetry / LiveFrameResult           │
│  • ILiveInspectionEngine / LiveInspectionEngine        │
│    ├── IPcbDefectDetector (ONNX DirectML/CPU)          │
│    ├── IImageAnnotationService (OpenCV overlays)       │
│    └── Frame throttle / drop stale frames mechanism    │
└────────────────────────────────────────────────────────┘
                           │
                           ▼
┌────────────────────────────────────────────────────────┐
│  Snapshot Persistence Service                          │
│  • ILiveSnapshotService / LiveSnapshotService          │
│    ├── File system storage for raw/annotated images    │
│    └── IInspectionRepository (SQLite persistence)      │
└────────────────────────────────────────────────────────┘
                           │
                           ▼
┌────────────────────────────────────────────────────────┐
│  Presentation Layer (MVVM)                             │
│  • LiveDetectionViewModel                              │
│  • LiveDetectionView.xaml                              │
│    ├── Full-screen video viewport                      │
│    ├── Floating top-right 3-card HUD                   │
│    ├── Floating bottom controls (Slider + Capture)     │
│    └── Stationary cursor auto-hide animation           │
└────────────────────────────────────────────────────────┘
                           │
                           ▼
┌────────────────────────────────────────────────────────┐
│  End-to-End Verification & BDD                         │
│  • Dependency Injection Registration                   │
│  • Unit & Integration Test Suites                      │
│  • Reqnroll BDD Acceptance Scenarios                   │
└────────────────────────────────────────────────────────┘
```

---

## Task Breakdown

### Task Group 1: Camera Hardware Abstraction & Frame Capture Services

#### Task 1.1: Camera Device Info Model & `ICameraService` Contract
- **Description:** Define domain contracts and models for video capture device enumeration, state management, and frame delivery.
- **Acceptance criteria:**
  - [ ] `CameraDeviceInfo` records device index, unique identifier, and display name.
  - [ ] `ICameraService` defines asynchronous operations: `GetAvailableCamerasAsync()`, `StartCaptureAsync(int cameraIndex)`, `StopCaptureAsync()`.
  - [ ] Exposes state properties: `IsCapturing`, `CurrentCamera`, and frame event `event EventHandler<Mat> FrameReceived`.
  - [ ] Implements `IDisposable` / `IAsyncDisposable` for clean hardware handle release.
- **Verification:**
  - [ ] Unit tests verify mock interface operations.
  - [ ] `dotnet build pcb-deftek.csproj` succeeds with 0 warnings.
- **Dependencies:** None
- **Files:**
  - `Domain/Models/CameraDeviceInfo.cs`
  - `Infrastructure/Services/ICameraService.cs`
  - `tests/pcb-deftek.Tests/CameraServiceTests.cs`
- **Estimated scope:** S (2-3 files)

#### Task 1.2: OpenCV Camera Service Implementation
- **Description:** Implement `OpenCvCameraService` using `OpenCvSharp4.VideoCapture` to discover video input devices and stream live frames on a background thread.
- **Acceptance criteria:**
  - [ ] Enumerate connected cameras via DirectShow/OpenCV query.
  - [ ] Captures frames continuously on a dedicated background worker loop (`Task.Run`).
  - [ ] Emits clone or copy of `Mat` frames to listeners via `FrameReceived` event without memory corruption.
  - [ ] Handles camera disconnects or hardware exceptions gracefully without crashing.
  - [ ] Cleanly disposes `VideoCapture` handle on `StopCaptureAsync` and disposal.
- **Verification:**
  - [ ] Integration tests pass: `dotnet test tests/pcb-deftek.Tests/ --filter CameraServiceTests`
  - [ ] Build succeeds: `dotnet build pcb-deftek.csproj`
- **Dependencies:** Task 1.1
- **Files:**
  - `Infrastructure/Services/OpenCvCameraService.cs`
  - `tests/pcb-deftek.Tests/CameraServiceTests.cs`
- **Estimated scope:** M (2-3 files)

#### Task 1.3: Fast Bitmap Conversion & Direct Annotation
- **Description:** Extend `IImageAnnotationService` and `ImageAnnotationService` to annotate in-memory `Mat` frames and convert directly to frozen WPF `BitmapSource` using fast uncompressed BMP streaming (<1ms).
- **Acceptance criteria:**
  - [ ] `AnnotateMat(Mat sourceMat, IReadOnlyList<DetectedDefect> defects)` draws color-coded bounding boxes and badges on live frames.
  - [ ] `MatToBitmapSource(Mat sourceMat)` converts OpenCV `Mat` directly to frozen `BitmapSource` without disk I/O.
  - [ ] Benchmarked conversion latency is <2ms per frame.
- **Verification:**
  - [ ] Unit tests pass: `dotnet test tests/pcb-deftek.Tests/ --filter ImageAnnotationServiceTests`
  - [ ] Build succeeds: `dotnet build pcb-deftek.csproj`
- **Dependencies:** Task 1.2
- **Files:**
  - `Infrastructure/Vision/IImageAnnotationService.cs`
  - `Infrastructure/Vision/ImageAnnotationService.cs`
  - `tests/pcb-deftek.Tests/ImageAnnotationServiceTests.cs`
- **Estimated scope:** S (3 files)

---

### Checkpoint 1: Camera Hardware Abstraction & Frame Streaming
- [ ] Camera enumeration and frame acquisition services operational.
- [ ] Fast in-memory Mat-to-BitmapSource conversion verified with unit tests.
- [ ] Clean build with 0 warnings (`dotnet build`).

---

### Task Group 2: Real-Time Inference Loop, Annotation & Telemetry Engine

#### Task 2.1: Live Inspection Telemetry & Frame Models
- **Description:** Define data models representing live frame inference results, latency metrics, and real-time telemetry.
- **Acceptance criteria:**
  - [ ] `LiveFrameResult` holds annotated `BitmapSource`, raw `Mat` clone, `InferenceResult`, defect count, and `IsPass` flag.
  - [ ] `LiveInspectionTelemetry` records inference latency in milliseconds, moving-average latency, live FPS, and active device name (CPU/GPU).
  - [ ] Provides thread-safe snapshot representations for UI binding.
- **Verification:**
  - [ ] Unit tests verify telemetry metric calculations.
  - [ ] Build succeeds: `dotnet build pcb-deftek.csproj`
- **Dependencies:** Task Group 1
- **Files:**
  - `Domain/Models/LiveInspectionTelemetry.cs`
  - `Domain/Models/LiveFrameResult.cs`
  - `tests/pcb-deftek.Tests/LiveTelemetryTests.cs`
- **Estimated scope:** S (3 files)

#### Task 2.2: Live Inspection Engine with Frame Dropping
- **Description:** Implement `ILiveInspectionEngine` and `LiveInspectionEngine` orchestrating background ONNX inference, stale frame throttling, and live telemetry tracking.
- **Acceptance criteria:**
  - [ ] Consumes camera frames from `ICameraService.FrameReceived`.
  - [ ] Drops stale frames if previous inference is still executing (ensures zero frame backlog and sub-second latency).
  - [ ] Executes defect detection via `IPcbDefectDetector.DetectAsync` off the UI thread.
  - [ ] Calculates rolling FPS over a 1-second window.
  - [ ] Determines live PASS/FAIL status (`PASS` if defect count == 0; `FAIL` if count > 0).
  - [ ] Emits `LiveFrameResult` to subscribers on each processed frame.
- **Verification:**
  - [ ] Unit and mock pipeline tests pass: `dotnet test tests/pcb-deftek.Tests/ --filter LiveInspectionEngineTests`
  - [ ] Build succeeds: `dotnet build pcb-deftek.csproj`
- **Dependencies:** Task 2.1
- **Files:**
  - `Infrastructure/Inference/ILiveInspectionEngine.cs`
  - `Infrastructure/Inference/LiveInspectionEngine.cs`
  - `tests/pcb-deftek.Tests/LiveInspectionEngineTests.cs`
- **Estimated scope:** M (3 files)

---

### Checkpoint 2: Real-Time Inference & Telemetry Engine
- [ ] Background inference loop operational without blocking the UI thread.
- [ ] Stale frame dropping guarantees instantaneous response time.
- [ ] Rolling FPS and latency calculations verified.
- [ ] Clean build with 0 warnings (`dotnet build`).

---

### Task Group 3: Snapshot Capture, Storage & SQLite Persistence Integration

#### Task 3.1: Live Snapshot Service & History Persistence
- **Description:** Implement `ILiveSnapshotService` to capture the current live frame, encode images to disk, and persist an `InspectionRecord` into SQLite with `DetectionMode.LiveCamera`.
- **Acceptance criteria:**
  - [ ] Captures the active frame's raw image and annotated overlay image.
  - [ ] Saves image files to persistent app data directory (`%LOCALAPPDATA%\PcbDeftek\pcb-deftek\Captures`).
  - [ ] Inserts `InspectionRecord` into SQLite via `IInspectionRepository.InsertAsync` with `DetectionMode.LiveCamera`.
  - [ ] Persists all detected `BoundingBox` entities, defect classes, confidence scores, and diagnostic metadata.
  - [ ] Returns the newly created `InspectionRecord`.
- **Verification:**
  - [ ] Integration test persists and verifies live camera capture in SQLite.
  - [ ] Tests pass: `dotnet test tests/pcb-deftek.Tests/ --filter LiveSnapshotServiceTests`
- **Dependencies:** Task Group 2
- **Files:**
  - `Infrastructure/Services/ILiveSnapshotService.cs`
  - `Infrastructure/Services/LiveSnapshotService.cs`
  - `tests/pcb-deftek.Tests/LiveSnapshotServiceTests.cs`
- **Estimated scope:** M (3 files)

---

### Checkpoint 3: Snapshot Capture & History Persistence
- [ ] Frame snapshot captures unannotated raw image and annotated overlay image to disk.
- [ ] SQLite database stores complete inspection record with `DetectionMode.LiveCamera` and bounding boxes.
- [ ] Clean build with 0 warnings (`dotnet build`).

---

### Task Group 4: Presentation Layer — `LiveDetectionViewModel` & Commanding

#### Task 4.1: ViewModel State Machine & Live Camera Lifecycle
- **Description:** Implement `LiveDetectionViewModel` managing camera stream lifecycle, camera source switching, confidence threshold binding, and live telemetry updates.
- **Acceptance criteria:**
  - [ ] Subscribes to `ILiveInspectionEngine.FrameProcessed` event and dispatches UI updates thread-safely via WPF Dispatcher.
  - [ ] Binds `CurrentFrameImage` (`BitmapSource`), `StatusText`, `IsPass` (`PASS`/`FAIL`), and `DefectCount`.
  - [ ] Binds `InferenceLatencyMs`, `Fps`, and `IsTelemetryEnabled` (toggleable).
  - [ ] Provides `AvailableCameras` collection and `SelectedCamera` property with automatic stream reconnection on selection change.
  - [ ] Provides `ConfidenceThreshold` property (`0.10`–`0.95`, default `0.25`) dynamically passed to the detection engine.
  - [ ] Automatically starts camera capture on navigation to view (`OnNavigatedToAsync`) and cleanly stops capture on navigation away (`OnNavigatedFromAsync`).
- **Verification:**
  - [ ] Unit tests pass: `dotnet test tests/pcb-deftek.Tests/ --filter LiveDetectionViewModelTests`
  - [ ] Build succeeds: `dotnet build pcb-deftek.csproj`
- **Dependencies:** Task Group 3
- **Files:**
  - `Presentation/ViewModels/LiveDetectionViewModel.cs`
  - `tests/pcb-deftek.Tests/LiveDetectionViewModelTests.cs`
- **Estimated scope:** M (2 files)

#### Task 4.2: Snapshot Capture Command & Spacebar Shortcut
- **Description:** Implement `CaptureSnapshotCommand` in `LiveDetectionViewModel` and keyboard shortcut handling.
- **Acceptance criteria:**
  - [ ] `CaptureSnapshotCommand` triggers snapshot capture via `ILiveSnapshotService`.
  - [ ] Shows subtle operator feedback upon capture (e.g. temporary capture toast or badge).
  - [ ] Can be triggered via button click or `Space` keyboard shortcut.
  - [ ] Disabled when camera stream is not running.
- **Verification:**
  - [ ] Unit tests verify command execution and service invocation.
- **Dependencies:** Task 4.1
- **Files:**
  - `Presentation/ViewModels/LiveDetectionViewModel.cs`
  - `tests/pcb-deftek.Tests/LiveDetectionViewModelTests.cs`
- **Estimated scope:** S (2 files)

#### Task 4.3: Sidebar Collapse Interaction & Navigation Signal
- **Description:** Integrate sidebar collapse communication so that navigating to Live Detection automatically collapses the sidebar sheet to maximize the video viewport.
- **Acceptance criteria:**
  - [ ] When `LiveDetectionViewModel` is activated, signals the shell (`MainViewModel` / `WeakReferenceMessenger`) to collapse the sidebar sheet by default.
  - [ ] Sidebar remains manually expandable/collapsible by clicking the hamburger toggle.
  - [ ] Navigating away restores normal sidebar behavior.
- **Verification:**
  - [ ] Unit tests verify messenger signal on navigation.
- **Dependencies:** Task 4.1
- **Files:**
  - `Domain/Messages/SidebarCollapseMessage.cs`
  - `Presentation/ViewModels/MainViewModel.cs`
  - `Presentation/ViewModels/LiveDetectionViewModel.cs`
  - `tests/pcb-deftek.Tests/LiveDetectionViewModelTests.cs`
- **Estimated scope:** S (3 files)

---

### Checkpoint 4: ViewModel State Machine & Commanding
- [ ] `LiveDetectionViewModel` handles frame delivery, camera switching, and confidence updates.
- [ ] Snapshot capture command persists records and provides visual confirmation.
- [ ] Sidebar auto-collapse on navigation verified with unit tests.
- [ ] Clean build with 0 warnings (`dotnet build`).

---

### Task Group 5: Presentation Layer — `LiveDetectionView.xaml`, Floating HUD & Auto-Hide

#### Task 5.1: Video Viewport & Top-Right 3-Card Floating HUD Layout
- **Description:** Build `LiveDetectionView.xaml` with full-screen video viewport and top-right 3-card vertical stack HUD adhering to Fluent design tokens.
- **Acceptance criteria:**
  - [ ] Viewport displays `CurrentFrameImage` with `Stretch="Uniform"` against dark background.
  - [ ] Top-right 3-card vertical stack:
    - **Card 1:** Live PASS/FAIL badge (green/red) and defect counter (`0 defects` / `N defects`).
    - **Card 2:** Telemetry toggle switch, live inference latency (`XX ms`), and live FPS (`XX FPS`).
    - **Card 3:** Camera source selector `ComboBox` bound to `AvailableCameras`.
  - [ ] Uses semantic Fluent theme brush tokens and high-contrast status colors.
- **Verification:**
  - [ ] View compiles and renders with 0 errors.
  - [ ] Build succeeds: `dotnet build pcb-deftek.csproj`
- **Dependencies:** Task Group 4
- **Files:**
  - `Presentation/Views/LiveDetectionView.xaml`
- **Estimated scope:** M (1 file)

#### Task 5.2: Bottom Floating Controls (Confidence Slider & Capture Button)
- **Description:** Implement bottom-left and bottom-right floating controls overlaying the video viewport.
- **Acceptance criteria:**
  - [ ] **Bottom-left:** Horizontal confidence threshold slider (`0.10` to `0.95`) with live numerical percentage badge (`25%`).
  - [ ] **Bottom-right:** High-contrast Capture button with `Space` keyboard shortcut hint badge (`[Space] Capture Frame`).
  - [ ] Input bindings wire `KeyBinding Key="Space"` to `CaptureSnapshotCommand`.
- **Verification:**
  - [ ] Visual inspection and keyboard shortcut verified.
  - [ ] Build succeeds: `dotnet build pcb-deftek.csproj`
- **Dependencies:** Task 5.1
- **Files:**
  - `Presentation/Views/LiveDetectionView.xaml`
- **Estimated scope:** S (1 file)

#### Task 5.3: Stationary Cursor Auto-Hide Behavior
- **Description:** Implement cursor idle detection in `LiveDetectionView.xaml.cs` to gradually fade out configuration controls when stationary, keeping PASS/FAIL, defect count, and toggled telemetry visible.
- **Acceptance criteria:**
  - [ ] A dispatcher timer detects when the mouse has been stationary for 3 seconds over the viewport.
  - [ ] Gradually animates opacity of configuration controls (camera selector, confidence slider, capture button, and card 2 telemetry if toggle is off) to `0.0`.
  - [ ] Any mouse movement, click, or key press immediately restores opacity to `1.0`.
  - [ ] **Persistent Elements Rule:** Card 1 (PASS/FAIL badge + defect count) and Card 2 (telemetry if toggle is on) **never fade out**.
- **Verification:**
  - [ ] Unit tests for auto-hide state logic.
  - [ ] Build succeeds: `dotnet build pcb-deftek.csproj`
- **Dependencies:** Task 5.2
- **Files:**
  - `Presentation/Views/LiveDetectionView.xaml.cs`
  - `tests/pcb-deftek.Tests/LiveDetectionViewBehaviorTests.cs`
- **Estimated scope:** S (2 files)

---

### Checkpoint 5: View Layout, Floating HUD & Auto-Hide Interaction
- [ ] Viewport renders live video with correct aspect ratio.
- [ ] Top-right 3-card HUD and bottom controls display properly in both Light and Dark Fluent themes.
- [ ] Stationary cursor auto-hide verified: configuration controls fade out while status and active telemetry persist.
- [ ] Clean build with 0 warnings (`dotnet build`).

---

### Task Group 6: Dependency Injection, Integration & Reqnroll BDD Scenarios

#### Task 6.1: Dependency Injection Wiring & App Integration
- **Description:** Register `ICameraService`, `ILiveInspectionEngine`, `ILiveSnapshotService`, and update `LiveDetectionViewModel` registrations in `App.xaml.cs`.
- **Acceptance criteria:**
  - [ ] Register `ICameraService` as Singleton (`OpenCvCameraService`).
  - [ ] Register `ILiveInspectionEngine` as Singleton (`LiveInspectionEngine`).
  - [ ] Register `ILiveSnapshotService` as Singleton (`LiveSnapshotService`).
  - [ ] Register `LiveDetectionViewModel` as Transient with all dependencies.
  - [ ] Update `DependencyInjectionTests.cs` to verify all new service registrations.
- **Verification:**
  - [ ] Unit tests pass: `dotnet test tests/pcb-deftek.Tests/ --filter DependencyInjectionTests`
  - [ ] Build succeeds: `dotnet build pcb-deftek.csproj`
- **Dependencies:** Task Groups 1-5
- **Files:**
  - `App.xaml.cs`
  - `tests/pcb-deftek.Tests/DependencyInjectionTests.cs`
- **Estimated scope:** S (2 files)

#### Task 6.2: Reqnroll BDD Acceptance Scenarios
- **Description:** Implement end-to-end BDD acceptance scenarios for Live Camera inspection, telemetry monitoring, auto-hide, and snapshot capture.
- **Acceptance criteria:**
  - [ ] Feature file `LiveCameraInspection.feature` with Gherkin scenarios:
    - *Scenario 1: Live camera stream displays real-time defect bounding boxes and PASS/FAIL classification.*
    - *Scenario 2: Telemetry toggle displays inference latency and FPS meters.*
    - *Scenario 3: Stationary cursor auto-hides configuration controls while preserving inspection status.*
    - *Scenario 4: Pressing Space or clicking Capture saves snapshot and persists inspection record to SQLite.*
    - *Scenario 5: Switching camera source updates active video capture device cleanly.*
  - [ ] Step definitions implemented in `LiveCameraInspectionStepDefinitions.cs`.
- **Verification:**
  - [ ] Full test suite passes: `dotnet test pcb-deftek.slnx`
  - [ ] Build succeeds: `dotnet build pcb-deftek.csproj`
- **Dependencies:** Task 6.1
- **Files:**
  - `tests/pcb-deftek.Bdd/Features/LiveCameraInspection.feature`
  - `tests/pcb-deftek.Bdd/StepDefinitions/LiveCameraInspectionStepDefinitions.cs`
- **Estimated scope:** M (2 files)

---

### Checkpoint 6: Full Verification & Merge Readiness
- [ ] All 6 task groups complete.
- [ ] Solution compiles cleanly with zero warnings (`dotnet build`).
- [ ] All unit, integration, and BDD tests pass (`dotnet test pcb-deftek.slnx`).
- [ ] PR created and passes automated 4-step Feature Review and Quality Improvement pipeline.

---

## Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| **Camera Hardware Unavailability in CI / Headless Environments** | High | Abstract video capture behind `ICameraService` with a simulated frame generator fallback when no physical camera is detected. |
| **DirectML GPU Latency Spike / Memory Exhaustion Under Live Video** | Medium | Implement the frame-drop throttle mechanism in `LiveInspectionEngine`, discarding stale frames so the model only processes the latest frame. |
| **High Allocation Rate & GC Pressure from Continuous Frame Decoding** | Medium | Use uncompressed BMP memory buffers and reuse memory pools for BitmapSource conversion rather than allocating new file streams. |
| **WPF Dispatcher Saturation from Rapid Frame Updates** | Medium | Dispatch UI frame updates only when the previous frame has completed rendering or limit UI dispatch to max 60 FPS. |
| **Camera Device Lockup on App Shutdown or Navigation** | Medium | Implement robust `IDisposable` patterns on `OpenCvCameraService` ensuring `VideoCapture.Release()` is called synchronously on cancellation. |

---

## Open Questions
- *None:* All requirements and layout parameters are concrete and strictly governed by `UI-INITIAL.md` (Section 3), `specs/roadmap.md` (Phase 6), and `specs/tech-stack.md`.
