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
  - [x] `CameraDeviceInfo` records device index, unique identifier, and display name.
  - [x] `ICameraService` defines asynchronous operations: `GetAvailableCamerasAsync()`, `StartCaptureAsync(int cameraIndex)`, `StopCaptureAsync()`.
  - [x] Exposes state properties: `IsCapturing`, `CurrentCamera`, and frame event `event EventHandler<CameraFrameEventArgs> FrameReceived`.
  - [x] Implements `IDisposable` / `IAsyncDisposable` for clean hardware handle release.
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
  - [x] Enumerate connected cameras via DirectShow/OpenCV query.
  - [x] Captures frames continuously on a dedicated background worker loop (`Task.Run`).
  - [x] Emits clone or copy of `Mat` frames to listeners via `FrameReceived` event without memory corruption.
  - [x] Handles camera disconnects or hardware exceptions gracefully without crashing.
  - [x] Cleanly disposes `VideoCapture` handle on `StopCaptureAsync` and disposal.
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
  - [x] `AnnotateMat(Mat sourceMat, IReadOnlyList<DetectedDefect> defects)` draws color-coded bounding boxes and badges on live frames.
  - [x] `MatToBitmapSource(Mat sourceMat)` converts OpenCV `Mat` directly to frozen `BitmapSource` without disk I/O.
  - [x] Benchmarked conversion latency is <2ms per frame.
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
  - [x] Camera enumeration and frame acquisition services operational.
  - [x] Fast in-memory Mat-to-BitmapSource conversion verified with unit tests.
  - [x] Clean build with 0 warnings (`dotnet build`).

---

### Task Group 2: Real-Time Inference Loop, Annotation & Telemetry Engine

#### Task 2.1: Live Inspection Telemetry & Frame Models
- **Description:** Define data models representing live frame inference results, latency metrics, and real-time telemetry.
- **Acceptance criteria:**
  - [x] `LiveFrameResult` holds annotated `BitmapSource`, raw `Mat` clone, `InferenceResult`, defect count, and `IsPass` flag.
  - [x] `LiveInspectionTelemetry` records inference latency in milliseconds, moving-average latency, live FPS, and active device name (CPU/GPU).
  - [x] Provides thread-safe snapshot representations for UI binding.
- **Verification:**
  - [x] Unit tests verify telemetry metric calculations.
  - [x] Build succeeds: `dotnet build pcb-deftek.csproj`
- **Dependencies:** Task Group 1
- **Files:**
  - `Domain/Models/LiveInspectionTelemetry.cs`
  - `Domain/Models/LiveFrameResult.cs`
  - `tests/pcb-deftek.Tests/LiveTelemetryTests.cs`
- **Estimated scope:** S (3 files)

#### Task 2.2: Live Inspection Engine with Frame Dropping
- **Description:** Implement `ILiveInspectionEngine` and `LiveInspectionEngine` orchestrating background ONNX inference, stale frame throttling, and live telemetry tracking.
- **Acceptance criteria:**
  - [x] Consumes camera frames from `ICameraService.FrameReceived`.
  - [x] Drops stale frames if previous inference is still executing (ensures zero frame backlog and sub-second latency).
  - [x] Executes defect detection via `IPcbDefectDetector.DetectAsync` off the UI thread.
  - [x] Calculates rolling FPS over a 1-second window.
  - [x] Determines live PASS/FAIL status (`PASS` if defect count == 0; `FAIL` if count > 0).
  - [x] Emits `LiveFrameResult` to subscribers on each processed frame.
- **Verification:**
  - [x] Unit and mock pipeline tests pass: `dotnet test tests/pcb-deftek.Tests/ --filter LiveInspectionEngineTests`
  - [x] Build succeeds: `dotnet build pcb-deftek.csproj`
- **Dependencies:** Task 2.1
- **Files:**
  - `Infrastructure/Inference/ILiveInspectionEngine.cs`
  - `Infrastructure/Inference/LiveInspectionEngine.cs`
  - `tests/pcb-deftek.Tests/LiveInspectionEngineTests.cs`
- **Estimated scope:** M (3 files)

---

### Checkpoint 2: Real-Time Inference & Telemetry Engine
- [x] Background inference loop operational without blocking the UI thread.
- [x] Stale frame dropping guarantees instantaneous response time.
- [x] Rolling FPS and latency calculations verified.
- [x] Clean build with 0 warnings (`dotnet build`).

---

### Task Group 3: Snapshot Capture, Storage & SQLite Persistence Integration

#### Task 3.1: Live Snapshot Service & History Persistence
- **Description:** Implement `ILiveSnapshotService` to capture the current live frame, encode images to disk, and persist an `InspectionRecord` into SQLite with `DetectionMode.LiveCamera`.
- **Acceptance criteria:**
  - [x] Captures the active frame's raw image and annotated overlay image.
  - [x] Saves image files to persistent app data directory (`%LOCALAPPDATA%\PcbDeftek\pcb-deftek\Captures`).
  - [x] Inserts `InspectionRecord` into SQLite via `IInspectionRepository.InsertAsync` with `DetectionMode.LiveCamera`.
  - [x] Persists all detected `BoundingBox` entities, defect classes, confidence scores, and diagnostic metadata.
  - [x] Returns the newly created `InspectionRecord`.
- **Verification:**
  - [x] Integration test persists and verifies live camera capture in SQLite.
  - [x] Tests pass: `dotnet test tests/pcb-deftek.Tests/ --filter LiveSnapshotServiceTests`
- **Dependencies:** Task Group 2
- **Files:**
  - `Infrastructure/Services/ILiveSnapshotService.cs`
  - `Infrastructure/Services/LiveSnapshotService.cs`
  - `tests/pcb-deftek.Tests/LiveSnapshotServiceTests.cs`
- **Estimated scope:** M (3 files)

---

### Checkpoint 3: Snapshot Capture & History Persistence
- [x] Frame snapshot captures unannotated raw image and annotated overlay image to disk.
- [x] SQLite database stores complete inspection record with `DetectionMode.LiveCamera` and bounding boxes.
- [x] Clean build with 0 warnings (`dotnet build`).

---

### Task Group 4: Presentation Layer — `LiveDetectionViewModel` & Commanding

#### Task 4.1: ViewModel State Machine & Live Camera Lifecycle
- **Description:** Implement `LiveDetectionViewModel` managing camera stream lifecycle, camera source switching, confidence threshold binding, and live telemetry updates.
- **Acceptance criteria:**
  - [x] Subscribes to `ILiveInspectionEngine.FrameProcessed` event and dispatches UI updates thread-safely via WPF Dispatcher.
  - [x] Binds `CurrentFrameImage` (`BitmapSource`), `StatusText`, `IsPass` (`PASS`/`FAIL`), and `DefectCount`.
  - [x] Binds `InferenceLatencyMs`, `Fps`, and `IsTelemetryEnabled` (toggleable).
  - [x] Provides `AvailableCameras` collection and `SelectedCamera` property with automatic stream reconnection on selection change.
  - [x] Provides `ConfidenceThreshold` property (`0.10`–`0.95`, default `0.25`) dynamically passed to the detection engine.
  - [x] Automatically starts camera capture on navigation to view (`OnNavigatedToAsync`) and cleanly stops capture on navigation away (`OnNavigatedFromAsync`).
- **Verification:**
  - [x] Unit tests pass: `dotnet test tests/pcb-deftek.Tests/ --filter LiveDetectionViewModelTests`
  - [x] Build succeeds: `dotnet build pcb-deftek.csproj`
- **Dependencies:** Task Group 3
- **Files:**
  - `Presentation/ViewModels/LiveDetectionViewModel.cs`
  - `tests/pcb-deftek.Tests/LiveDetectionViewModelTests.cs`
- **Estimated scope:** M (2 files)

#### Task 4.2: Snapshot Capture Command & Spacebar Shortcut
- **Description:** Implement `CaptureSnapshotCommand` in `LiveDetectionViewModel` and keyboard shortcut handling.
- **Acceptance criteria:**
  - [x] `CaptureSnapshotCommand` triggers snapshot capture via `ILiveSnapshotService`.
  - [x] Shows subtle operator feedback upon capture (e.g. temporary capture toast or badge).
  - [x] Can be triggered via button click or `Space` keyboard shortcut.
  - [x] Disabled when camera stream is not running.
- **Verification:**
  - [x] Unit tests verify command execution and service invocation.
- **Dependencies:** Task 4.1
- **Files:**
  - `Presentation/ViewModels/LiveDetectionViewModel.cs`
  - `tests/pcb-deftek.Tests/LiveDetectionViewModelTests.cs`
- **Estimated scope:** S (2 files)

#### Task 4.3: Sidebar Collapse Interaction & Navigation Signal
- **Description:** Integrate sidebar collapse communication so that navigating to Live Detection automatically collapses the sidebar sheet to maximize the video viewport.
- **Acceptance criteria:**
  - [x] When `LiveDetectionViewModel` is activated, signals the shell (`MainViewModel` / `WeakReferenceMessenger`) to collapse the sidebar sheet by default.
  - [x] Sidebar remains manually expandable/collapsible by clicking the hamburger toggle.
  - [x] Navigating away restores normal sidebar behavior.
- **Verification:**
  - [x] Unit tests verify messenger signal on navigation.
- **Dependencies:** Task 4.1
- **Files:**
  - `Domain/Messages/SidebarCollapseMessage.cs`
  - `Presentation/ViewModels/MainViewModel.cs`
  - `Presentation/ViewModels/LiveDetectionViewModel.cs`
  - `tests/pcb-deftek.Tests/LiveDetectionViewModelTests.cs`
- **Estimated scope:** S (3 files)

---

### Checkpoint 4: ViewModel State Machine & Commanding
- [x] `LiveDetectionViewModel` handles frame delivery, camera switching, and confidence updates.
- [x] Snapshot capture command persists records and provides visual confirmation.
- [x] Sidebar auto-collapse on navigation verified with unit tests.
- [x] Clean build with 0 warnings (`dotnet build`).

---

### Task Group 5: Presentation Layer — `LiveDetectionView.xaml`, Floating HUD & Auto-Hide

#### Task 5.1: Video Viewport & Top-Right 3-Card Floating HUD Layout
- **Description:** Build `LiveDetectionView.xaml` with full-screen video viewport and top-right 3-card vertical stack HUD adhering to Fluent design tokens.
- **Acceptance criteria:**
  - [x] Viewport displays `CurrentFrameImage` with `Stretch="Uniform"` against dark background.
  - [x] Top-right 3-card vertical stack:
    - **Card 1:** Live PASS/FAIL badge (green/red) and defect counter (`0 defects` / `N defects`).
    - **Card 2:** Telemetry toggle switch, live inference latency (`XX ms`), and live FPS (`XX FPS`).
    - **Card 3:** Camera source selector `ComboBox` bound to `AvailableCameras`.
  - [x] Uses semantic Fluent theme brush tokens and high-contrast status colors.
- **Verification:**
  - [x] View compiles and renders with 0 errors.
  - [x] Build succeeds: `dotnet build pcb-deftek.csproj`
- **Dependencies:** Task Group 4
- **Files:**
  - `Presentation/Views/LiveDetectionView.xaml`
- **Estimated scope:** M (1 file)

#### Task 5.2: Bottom Floating Controls (Confidence Slider & Capture Button)
- **Description:** Implement bottom-left and bottom-right floating controls overlaying the video viewport.
- **Acceptance criteria:**
  - [x] **Bottom-left:** Horizontal confidence threshold slider (`0.10` to `0.95`) with live numerical percentage badge (`25%`).
  - [x] **Bottom-right:** High-contrast Capture button with `Space` keyboard shortcut hint badge (`[Space] Capture Frame`).
  - [x] Input bindings wire `KeyBinding Key="Space"` to `CaptureSnapshotCommand`.
- **Verification:**
  - [x] Visual inspection and keyboard shortcut verified.
  - [x] Build succeeds: `dotnet build pcb-deftek.csproj`
- **Dependencies:** Task 5.1
- **Files:**
  - `Presentation/Views/LiveDetectionView.xaml`
- **Estimated scope:** S (1 file)

#### Task 5.3: Stationary Cursor Auto-Hide Behavior
- **Description:** Implement cursor idle detection in `LiveDetectionView.xaml.cs` to gradually fade out configuration controls when stationary, keeping PASS/FAIL, defect count, and toggled telemetry visible.
- **Acceptance criteria:**
  - [x] A dispatcher timer detects when the mouse has been stationary for 3 seconds over the viewport.
  - [x] Gradually animates opacity of configuration controls (camera selector, confidence slider, capture button, and card 2 telemetry if toggle is off) to `0.0`.
  - [x] Any mouse movement, click, or key press immediately restores opacity to `1.0`.
  - [x] **Persistent Elements Rule:** Card 1 (PASS/FAIL badge + defect count) and Card 2 (telemetry if toggle is on) **never fade out**.
- **Verification:**
  - [x] Unit tests for auto-hide state logic.
  - [x] Build succeeds: `dotnet build pcb-deftek.csproj`
- **Dependencies:** Task 5.2
- **Files:**
  - `Presentation/Views/LiveDetectionView.xaml.cs`
  - `tests/pcb-deftek.Tests/LiveDetectionViewBehaviorTests.cs`
- **Estimated scope:** S (2 files)

---

### Checkpoint 5: View Layout, Floating HUD & Auto-Hide Interaction
- [x] Viewport renders live video with correct aspect ratio.
- [x] Top-right 3-card HUD and bottom controls display properly in both Light and Dark Fluent themes.
- [x] Stationary cursor auto-hide verified: configuration controls fade out while status and active telemetry persist.
- [x] Clean build with 0 warnings (`dotnet build`).

---

### Task Group 6: Dependency Injection, Integration & Reqnroll BDD Scenarios

#### Task 6.1: Dependency Injection Wiring & App Integration
- **Description:** Register `ICameraService`, `ILiveInspectionEngine`, `ILiveSnapshotService`, and update `LiveDetectionViewModel` registrations in `App.xaml.cs`.
- **Acceptance criteria:**
  - [x] Register `ICameraService` as Singleton (`OpenCvCameraService`).
  - [x] Register `ILiveInspectionEngine` as Singleton (`LiveInspectionEngine`).
  - [x] Register `ILiveSnapshotService` as Singleton (`LiveSnapshotService`).
  - [x] Register `LiveDetectionViewModel` as Transient with all dependencies.
  - [x] Update `DependencyInjectionTests.cs` to verify all new service registrations.
- **Verification:**
  - [x] Unit tests pass: `dotnet test tests/pcb-deftek.Tests/ --filter DependencyInjectionTests`
  - [x] Build succeeds: `dotnet build pcb-deftek.csproj`
- **Dependencies:** Task Groups 1-5
- **Files:**
  - `App.xaml.cs`
  - `tests/pcb-deftek.Tests/DependencyInjectionTests.cs`
- **Estimated scope:** S (2 files)

#### Task 6.2: Reqnroll BDD Acceptance Scenarios
- **Description:** Implement end-to-end BDD acceptance scenarios for Live Camera inspection, telemetry monitoring, auto-hide, and snapshot capture.
- **Acceptance criteria:**
  - [x] Feature file `LiveCameraInspection.feature` with Gherkin scenarios:
    - *Scenario 1: Live camera stream displays real-time defect bounding boxes and PASS/FAIL classification.*
    - *Scenario 2: Telemetry toggle displays inference latency and FPS meters.*
    - *Scenario 3: Stationary cursor auto-hides configuration controls while preserving inspection status.*
    - *Scenario 4: Pressing Space or clicking Capture saves snapshot and persists inspection record to SQLite.*
    - *Scenario 5: Switching camera source updates active video capture device cleanly.*
  - [x] Step definitions implemented in `LiveCameraInspectionStepDefinitions.cs`.
- **Verification:**
  - [x] Full test suite passes: `dotnet test pcb-deftek.slnx`
  - [x] Build succeeds: `dotnet build pcb-deftek.csproj`
- **Dependencies:** Task 6.1
- **Files:**
  - `tests/pcb-deftek.Bdd/Features/LiveCameraInspection.feature`
  - `tests/pcb-deftek.Bdd/StepDefinitions/LiveCameraInspectionStepDefinitions.cs`
- **Estimated scope:** M (2 files)

---

### Checkpoint 6: Full Verification & Merge Readiness
- [x] All 6 task groups complete.
- [x] Solution compiles cleanly with zero warnings (`dotnet build`).
- [x] All unit, integration, and BDD tests pass (`dotnet test pcb-deftek.slnx`).
- [x] PR created and passes automated 4-step Feature Review and Quality Improvement pipeline.

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
