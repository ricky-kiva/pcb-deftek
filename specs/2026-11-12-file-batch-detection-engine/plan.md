# Implementation Plan: Phase 5 — File & Batch Detection Engine

## Overview
Phase 5 implements the complete file and batch detection workflow for `pcb-deftek`. It provides automated, high-throughput defect detection and classification for individual PCB image files, multi-image selections, and entire directory folders.

The implementation builds a decoupled ingestion layer, an asynchronous batch execution worker with rolling ETA estimation, SQLite persistence of inspection records, Windows completion toast notifications, and a responsive WPF Fluent UI with drag-and-drop capabilities.

---

## Architecture & Dependency Graph

```
┌────────────────────────────────────────────────────────┐
│  Infrastructure / Services                             │
│  • IFileDiscoveryService / FileDiscoveryService        │
│  • IFilePickerService / WpfFilePickerService           │
└────────────────────────────────────────────────────────┘
                           │
                           ▼
┌────────────────────────────────────────────────────────┐
│  Batch Processing Engine                               │
│  • BatchModels (Progress, Summary, Item, Status)       │
│  • IBatchInspectionEngine / BatchInspectionEngine      │
│    ├── IPcbDefectDetector (ONNX inference)             │
│    ├── IInspectionRepository (SQLite persistence)      │
│    └── INotificationService (Windows toasts)           │
└────────────────────────────────────────────────────────┘
                           │
                           ▼
┌────────────────────────────────────────────────────────┐
│  Presentation Layer (MVVM)                             │
│  • FileDetectionViewModel                              │
│  • FileDetectionView.xaml (Fluent, Drag & Drop, ETA)   │
└────────────────────────────────────────────────────────┘
                           │
                           ▼
┌────────────────────────────────────────────────────────┐
│  End-to-End Verification                               │
│  • Dependency Injection Registration                   │
│  • Unit & Integration Test Suites                      │
│  • Reqnroll BDD Acceptance Scenarios                   │
└────────────────────────────────────────────────────────┘
```

---

## Task Breakdown

### Task Group 1: File Discovery, Dialog Abstraction & Ingestion Services

#### Task 1.1: File Discovery & Validation Service
- **Description:** Implement `IFileDiscoveryService` and `FileDiscoveryService` to discover, filter, and validate image files from individual file paths, path lists, and folder paths recursively.
- **Acceptance criteria:**
  - [ ] Supports standard PCB image extensions: `.jpg`, `.jpeg`, `.png`, `.bmp`, `.webp` (case-insensitive).
  - [ ] Recursively enumerates files when a folder path is supplied.
  - [ ] Ignores non-image files, hidden files, zero-byte files, and duplicate paths.
  - [ ] Returns structured `IReadOnlyList<string>` of validated absolute file paths.
- **Verification:**
  - [ ] Unit tests pass: `dotnet test tests/pcb-deftek.Tests/ --filter FileDiscoveryServiceTests`
  - [ ] Build succeeds: `dotnet build pcb-deftek.csproj`
- **Dependencies:** None
- **Files:**
  - `Infrastructure/Services/IFileDiscoveryService.cs`
  - `Infrastructure/Services/FileDiscoveryService.cs`
  - `tests/pcb-deftek.Tests/FileDiscoveryServiceTests.cs`
- **Estimated scope:** S (3 files)

#### Task 1.2: Windows File & Folder Picker Dialog Abstraction
- **Description:** Implement `IFilePickerService` and `WpfFilePickerService` to abstract modern Windows file picker (`OpenFileDialog` with multi-select enabled) and folder picker (`OpenFolderDialog`) for decoupled ViewModel testability.
- **Acceptance criteria:**
  - [ ] Exposes `PickFilesAsync(string title, string filter, bool multiselect)` returning selected paths or empty if cancelled.
  - [ ] Exposes `PickFolderAsync(string title)` returning selected folder path or null if cancelled.
  - [ ] Sets appropriate default filters for image file formats (`"PCB Images (*.jpg;*.jpeg;*.png;*.bmp;*.webp)|*.jpg;*.jpeg;*.png;*.bmp;*.webp|All Files (*.*)|*.*"`).
- **Verification:**
  - [ ] Unit tests with mock file picker pass.
  - [ ] Build succeeds: `dotnet build pcb-deftek.csproj`
- **Dependencies:** Task 1.1
- **Files:**
  - `Infrastructure/Services/IFilePickerService.cs`
  - `Infrastructure/Services/WpfFilePickerService.cs`
- **Estimated scope:** S (2 files)

#### Checkpoint 1: Ingestion Infrastructure
- [x] `FileDiscoveryServiceTests` pass cleanly.
- [x] Image filtering, recursion, and deduplication logic verified.
- [x] Dialog service cleanly abstracted behind interface.

---

### Task Group 2: Batch Inspection Engine & Rolling ETA Calculation

#### Task 2.1: Batch Inspection Domain Contracts & Progress Models
- **Description:** Define data contracts and models for batch inspection progress, items, and status.
- **Acceptance criteria:**
  - [ ] `BatchInspectionItem`: Encapsulates file path, filename, inspection status (`Pending`, `Processing`, `Pass`, `Fail`, `Error`), defect count, duration, and error message.
  - [ ] `BatchProgressReport`: Encapsulates `TotalCount`, `ProcessedCount`, `PassCount`, `FailCount`, `ErrorCount`, `PercentComplete`, `ElapsedTime`, `EstimatedTimeRemaining` (`TimeSpan?`), and `Speed` (items/sec or sec/item).
  - [ ] `BatchInspectionSummary`: Final aggregated results including total, passed, failed, errors, duration, and list of completed records.
  - [ ] `IBatchInspectionEngine`: Contract exposing `ExecuteBatchAsync(IReadOnlyList<string> filePaths, IProgress<BatchProgressReport>? progress, float? confidenceThreshold, CancellationToken cancellationToken)`.
- **Verification:**
  - [ ] Model contract tests pass.
  - [ ] Build succeeds: `dotnet build pcb-deftek.csproj`
- **Dependencies:** Task Group 1
- **Files:**
  - `Domain/Models/BatchInspectionModels.cs`
  - `Infrastructure/Inference/IBatchInspectionEngine.cs`
- **Estimated scope:** S (2 files)

#### Task 2.2: Batch Inspection Engine Implementation with Rolling ETA
- **Description:** Implement `BatchInspectionEngine` orchestrating asynchronous queue execution, calling `IPcbDefectDetector.DetectFromFileAsync`, persisting records via `IInspectionRepository.InsertAsync`, and computing a rolling ETA.
- **Acceptance criteria:**
  - [ ] Asynchronously processes files off the UI thread sequentially to protect GPU/CPU memory.
  - [ ] Computes dynamic rolling average ETA: maintains a window of recent item processing durations and computes `RemainingCount * AverageDuration`.
  - [ ] Persists each inspected board into SQLite database as `InspectionRecord` with `DetectionMode.SingleFile` (if 1 file) or `DetectionMode.BatchFolder` (if >1 file).
  - [ ] Handles individual image read/inference failures gracefully without aborting the batch (records item error, logs Serilog warning, continues to next item).
  - [ ] Honors `CancellationToken`, stopping immediately and cleanly transitioning status to `Cancelled`.
- **Verification:**
  - [ ] Unit & integration tests pass: `dotnet test tests/pcb-deftek.Tests/ --filter BatchInspectionEngineTests`
  - [ ] Build succeeds: `dotnet build pcb-deftek.csproj`
- **Dependencies:** Task 2.1
- **Files:**
  - `Infrastructure/Inference/BatchInspectionEngine.cs`
- **Estimated scope:** S (1 file)

#### Task 2.3: Batch Engine Unit & Integration Testing
- **Description:** Comprehensive test coverage for ETA calculation math, cancellation tokens, error recovery, and SQLite persistence.
- **Acceptance criteria:**
  - [ ] Unit test verifies ETA calculation across various queue sizes, elapsed times, and edge cases (0 items, 1 item, completed).
  - [ ] Unit test verifies cancellation mid-batch leaves previously processed items saved.
  - [ ] Integration test verifies batch execution against actual sample test images (`TestData/Model/SH-1.jpg`, `TestData/Model/SP-1.jpg`) persists valid `InspectionRecord` and `DetectedDefect` rows into SQLite.
- **Verification:**
  - [ ] `dotnet test tests/pcb-deftek.Tests/ --filter BatchInspectionEngineTests` passes.
- **Dependencies:** Task 2.2
- **Files:**
  - `tests/pcb-deftek.Tests/BatchInspectionEngineTests.cs`
- **Estimated scope:** S (1 file)

#### Checkpoint 2: Batch Engine & Persistence Verification
- [x] All batch engine tests pass.
- [x] Rolling ETA math is validated without division-by-zero or negative intervals.
- [x] SQLite database commits records with defects and correct DetectionMode.

---

### Task Group 3: Presentation Layer — FileDetectionViewModel & State Machine

#### Task 3.1: FileDetectionViewModel Implementation
- **Description:** Implement `FileDetectionViewModel` with commanding, progress bindings, queue management, and toast notifications.
- **Acceptance criteria:**
  - [ ] Commands: `BrowseFilesCommand`, `BrowseFolderCommand`, `DropFilesCommand`, `StartBatchCommand`, `CancelBatchCommand`, and `ClearQueueCommand`.
  - [ ] Observable properties: `QueueItems`, `TotalCount`, `ProcessedCount`, `PassCount`, `FailCount`, `PercentComplete`, `EtaText` (formatted inline string), `StatusText`, `IsProcessing`, `HasItems`, `CanStart`, `CanCancel`.
  - [ ] Dispatches completion toast notification via `INotificationService.ShowToastAsync` when batch completes.
  - [ ] Reads default confidence threshold from `IUserSettingsService`.
  - [ ] Implements lifecycle methods (`OnNavigatedToAsync`, `OnNavigatedFromAsync`).
- **Verification:**
  - [ ] Unit tests pass: `dotnet test tests/pcb-deftek.Tests/ --filter FileDetectionViewModelTests`
  - [ ] Build succeeds: `dotnet build pcb-deftek.csproj`
- **Dependencies:** Task Group 2
- **Files:**
  - `Presentation/ViewModels/FileDetectionViewModel.cs`
  - `Presentation/ViewModels/FileDetectionQueueItemViewModel.cs`
- **Estimated scope:** S (2 files)

#### Task 3.2: FileDetectionViewModel Unit Testing
- **Description:** Implement unit tests verifying ViewModel commanding, progress updates, cancellation flow, and notification triggers.
- **Acceptance criteria:**
  - [ ] Assert browsing and dropping updates queue.
  - [ ] Assert starting batch initiates engine and updates progress properties on UI thread.
  - [ ] Assert cancelling batch updates `CanCancel` and dispatches cancellation token.
  - [ ] Assert completion dispatches `INotificationService.ShowToastAsync` with correct totals.
- **Verification:**
  - [ ] `dotnet test tests/pcb-deftek.Tests/ --filter FileDetectionViewModelTests` passes cleanly.
- **Dependencies:** Task 3.1
- **Files:**
  - `tests/pcb-deftek.Tests/FileDetectionViewModelTests.cs`
- **Estimated scope:** S (1 file)
#### Checkpoint 3: ViewModel Commanding & State Verification
- [x] ViewModel unit tests pass.
- [x] Property notifications, queue population, and cancellation transitions validated.

---

### Task Group 4: Presentation Layer — FileDetectionView XAML & Drag-and-Drop UX

#### Task 4.1: FileDetectionView XAML Layout
- **Description:** Build the `FileDetectionView.xaml` layout adhering to `UI-INITIAL.md` (Section 4) and WPF Fluent Theme guidelines, featuring an inline Cancel Processing button directly beside the ETA indicator.
- **Acceptance criteria:**
  - [ ] **Row 1 — Upload Zone:** Large drag-and-drop area with dashed border, upload icon, supporting text ("Drag & drop PCB images or folders here, or click to browse"), and action buttons (`Browse Files`, `Browse Folder`).
  - [ ] **Row 2 — Progress, ETA & Inline Cancel Button:** Fluent `ProgressBar` bound to `PercentComplete`, inline progress text displaying `Processed N of Total (X%) • ETA: mm:ss`, and a compact **Cancel Processing button** positioned directly adjacent to the ETA indicator (bound to `CancelBatchCommand`, visible/enabled when `IsProcessing` is true).
  - [ ] **Counter Badges & Controls:** Live counter badges (Total, Pass in green, Fail in red), plus `Start Inspection` and `Clear Queue` action buttons.
  - [ ] **Results Grid:** Virtualized `ListView` or `DataGrid` displaying queue items with status chips, defect count, duration, and class details.
  - [ ] Build succeeds without XAML markup errors.
- **Dependencies:** Task Group 3
- **Files:**
  - `Presentation/Views/FileDetectionView.xaml`
- **Estimated scope:** S (1 file)

#### Task 4.2: Drag-and-Drop Behavior & Code-Behind Wiring
- **Description:** Wire drag-and-drop events (`DragEnter`, `DragOver`, `DragLeave`, `Drop`) in `FileDetectionView.xaml.cs` to forward dropped file/folder paths to `FileDetectionViewModel.DropFilesCommand` while maintaining MVVM purity.
- **Acceptance criteria:**
  - [ ] Visual highlight feedback when files are dragged over the drop zone.
  - [ ] Extracts files and directory paths from `DragEventArgs.Data.GetData(DataFormats.FileDrop)`.
  - [ ] Handles non-file drop attempts gracefully without exceptions.
- **Verification:**
  - [ ] Application builds with 0 warnings.
  - [ ] Interactive manual verification in shell.
- **Dependencies:** Task 4.1
- **Files:**
  - `Presentation/Views/FileDetectionView.xaml.cs`
- **Estimated scope:** XS (1 file)

#### Checkpoint 4: UI & Drag-and-Drop Verification
- [ ] View renders cleanly under both Light and Dark Fluent themes.
- [ ] Drag-and-drop visual highlight activates upon drag-over.
- [ ] Progress bar, inline ETA, and inline Cancel Processing button reflect ViewModel state and trigger cancellation promptly.
- [ ] Queue list displays item status chips, defect counts, and durations.
---

### Task Group 5: DI Wiring, Integration & Reqnroll BDD Scenarios

#### Task 5.1: Dependency Injection Wiring
- **Description:** Register `IFileDiscoveryService`, `IFilePickerService`, and `IBatchInspectionEngine` in `App.xaml.cs` and update DI verification tests.
- **Acceptance criteria:**
  - [ ] `IFileDiscoveryService` registered as Singleton.
  - [ ] `IFilePickerService` registered as Singleton (or Transient).
  - [ ] `IBatchInspectionEngine` registered as Singleton.
  - [ ] `FileDetectionViewModel` verified with all injected dependencies.
- **Verification:**
  - [ ] `dotnet test tests/pcb-deftek.Tests/ --filter DependencyInjectionTests` passes.
- **Dependencies:** Task Groups 1–4
- **Files:**
  - `App.xaml.cs`
  - `tests/pcb-deftek.Tests/DependencyInjectionTests.cs`
- **Estimated scope:** S (2 files)

#### Task 5.2: Reqnroll BDD Acceptance Scenarios
- **Description:** Implement Reqnroll BDD feature `FileBatchDetection.feature` and step definitions in `FileBatchDetectionStepDefinitions.cs`.
- **Acceptance criteria:**
  - [ ] Scenario 1: Single PCB image file detection and persistence.
  - [ ] Scenario 2: Multi-file batch detection with progress and inline ETA calculation.
  - [ ] Scenario 3: Batch cancellation mid-flight.
  - [ ] Scenario 4: Completion toast notification dispatch.
  - [ ] All scenarios execute and pass cleanly.
- **Verification:**
  - [ ] `dotnet test tests/pcb-deftek.Bdd/` passes 100%.
- **Dependencies:** Task 5.1
- **Files:**
  - `tests/pcb-deftek.Bdd/Features/FileBatchDetection.feature`
  - `tests/pcb-deftek.Bdd/StepDefinitions/FileBatchDetectionStepDefinitions.cs`
- **Estimated scope:** S (2 files)

#### Checkpoint 5: End-to-End Acceptance Verification
- [ ] All 5 task groups complete.
- [ ] Solution compiles cleanly with zero warnings (`dotnet build`).
- [ ] All unit, integration, and BDD tests pass (`dotnet test`).
- [ ] Checkpoint 5 verified and ready for PR.

---

## Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Large batch processing saturates DirectML GPU memory | High | Execute batch items sequentially (or with bounded degree of parallelism = 1) off the UI thread; release native OpenCV `Mat` buffers promptly. |
| Ingestion of large folders containing non-image files or subdirectories freezes UI | Med | Run file discovery and extension filtering asynchronously in `IFileDiscoveryService` off the UI thread. |
| Division by zero or jittery ETA calculation during initial items | Med | Display `"Calculating..."` until at least 2 items complete; use rolling exponential moving average to smooth ETA intervals. |
| Unreadable or corrupt image file halts batch | Med | Implement try-catch per item inside `BatchInspectionEngine`; log Serilog warning, mark item as `Error`, and continue batch processing. |
| Drag-and-drop operations fail when running as elevated/Administrator | Low | Standard WPF `DragDrop` behavior; document standard non-elevated user execution. |

---

## Open Questions
- *None:* Requirements are fully defined in `UI-INITIAL.md` (Section 4) and `specs/roadmap.md` (Phase 5).
