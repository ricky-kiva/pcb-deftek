# Requirements & Context: Phase 5 — File & Batch Detection Engine

## 1. Executive Summary & Context
Phase 5 implements the **File & Batch Detection Engine** for `pcb-deftek`. It provides automated, high-throughput defect detection and classification for individual PCB image files, multi-image selections, and entire directory folders.

This phase bridges the ONNX inference pipeline (Phase 3) and persistence infrastructure (Phase 2) with the desktop presentation shell (Phase 4), fulfilling the requirements defined in:
- `specs/roadmap.md` (Phase 5: File & Batch Detection Engine)
- `specs/mission.md` (Section 5.1: File-Based Detection)
- `specs/tech-stack.md` (Asynchronous processing, OpenCV image decoding, Windows toast alerts)
- `UI-INITIAL.md` (Section 4: File-Based Detection Page)

Upon completion, QA inspectors and production technicians can drag and drop single images, multiple files, or directory trees of PCB images into the application, observe real-time progress with an inline estimated completion time (ETA), review batch defect totals (PASS/FAIL), persist all inspection records to the SQLite database, and receive a Windows completion toast notification when the batch finishes.

---

## 2. Architectural Decisions & Tech Stack

### 2.1. Ingestion & File Discovery (`IFileDiscoveryService` & `IFilePickerService`)
- Support common PCB inspection image formats: `.jpg`, `.jpeg`, `.png`, `.bmp`, `.webp`.
- **Recursive Directory Ingestion:** When a folder is dropped or selected, enumerate image files recursively, skipping hidden/system files and unsupported extensions.
- **Deduplication:** Ignore duplicate paths if selected simultaneously.
- **Dialog Abstraction:** Abstract `OpenFileDialog` (multi-select enabled) and `OpenFolderDialog` behind an `IFilePickerService` to keep ViewModels testable and decoupled from WPF UI controls.

### 2.2. Batch Execution Engine (`IBatchInspectionEngine`)
- **Queue & Pipeline Architecture:** The engine accepts a collection of valid image file paths and executes defect detection sequentially or with bounded concurrency (default sequential off the UI thread) to prevent GPU memory saturation on DirectML / CPU thread exhaustion.
- **Inference Integration:** Delegate defect localization and classification to `IPcbDefectDetector.DetectFromFileAsync` (or via byte streams).
- **Persistence Integration:** Atomically persist each processed board as an `InspectionRecord` using `IInspectionRepository.InsertAsync`, setting `DetectionMode.SingleFile` for single-item jobs and `DetectionMode.BatchFolder` for multi-item or folder batches.
- **Graceful Fault Tolerance:** If an individual image file is corrupt or unreadable, the engine records an item error, logs a warning through Serilog, increments error metrics, and continues processing subsequent items without crashing or aborting the entire batch.

### 2.3. Progress Tracking & Dynamic ETA Modeling (`BatchProgress`)
- Leverage standard `IProgress<BatchProgressReport>` for real-time progress reporting back to the UI thread without blocking.
- **Inline ETA Algorithm:**
  - Compute elapsed time using high-resolution `Stopwatch`.
  - Maintain a rolling window of recent per-image processing times (inference + pre/post-processing + database insertion).
  - Calculate `EstimatedTimeRemaining = RemainingItems * RollingAverageProcessingTime`.
  - Provide human-readable formatting: `mm:ss` (or `hh:mm:ss`), displaying `"Calculating..."` during initial warmup (first 1–2 items).
- Provide real-time counts: `TotalCount`, `ProcessedCount`, `PassCount`, `FailCount`, `ErrorCount`, and `PercentComplete`.

### 2.4. Cancellation & Life-Cycle Management
- Provide full cancellation support via `CancellationTokenSource`.
- When cancellation is requested by the operator, currently processing image completes or cancels cleanly, remaining queued items are discarded, and batch status transitions to `Cancelled`.
- Partially processed records remain safely committed in the SQLite database for historical traceability.

### 2.5. Desktop Notifications
- On batch completion (or cancellation/fault with processed items), dispatch a Windows toast notification via `INotificationService.ShowToastAsync` informing the operator of final totals (e.g., `"Batch Inspection Complete"`, `"Processed 48 boards: 42 Passed, 6 Failed in 01:14."`).

### 2.6. Presentation & UI Structure (`FileDetectionViewModel` & `FileDetectionView.xaml`)
Following `UI-INITIAL.md` (Section 4):
- **Row 1 — File Upload Area:**
  - Large, prominent drag-and-drop landing area styled with Fluent typography, subtle dashed borders, drop icon, and intuitive guidance.
  - Interactive click prompt triggering the Windows file/folder picker.
  - Dedicated action buttons: `Browse Files` and `Browse Folder`.
  - Drag-over visual highlight feedback (`DragEnter`, `DragLeave`, `Drop`).
- **Row 2 — Processing Progress, Inline ETA & Inline Cancel Button:**
  - Standard Fluent `ProgressBar` bound to `PercentComplete`.
  - Inline progress & cancellation row:
    - Formatted progress text: `Processed N of Total (X%) • ETA: mm:ss • Speed: ~X.X s/board`.
    - **Cancel Processing Button:** Positioned directly adjacent to the ETA indicator. Visible and enabled whenever batch processing is actively running (`IsProcessing = true`), providing a one-click mechanism to abort the active batch without leaving the progress area.
  - Live summary counter badges: Total queued, Passed (green badge), Failed (red badge).
  - Secondary action controls: `Start Inspection` (when items are queued and idle) and `Clear Queue` (when idle).
- **Inspection Queue / Results Table:**
  - Clean table or virtualized list displaying queued/processed items: Filename, Status badge (`Pending`, `Processing`, `Pass`, `Fail`, `Error`), Defect Count, Duration, and Detected Classes.

---

## 3. In-Scope Deliverables

### 3.1. Infrastructure & Domain
1. **File Services:**
   - `IFileDiscoveryService` & `FileDiscoveryService`: Discovers and validates image files from path lists, folders, and drop payloads.
   - `IFilePickerService` & `WpfFilePickerService`: Opens modern Windows file and folder picker dialogs.
2. **Batch Inspection Engine:**
   - `IBatchInspectionEngine` & `BatchInspectionEngine`: Executes asynchronous detection queue, computes rolling ETA, commits `InspectionRecord`s to SQLite, and fires completion notifications.
   - Domain models: `BatchInspectionItem`, `BatchProgressReport`, `BatchInspectionSummary`, `BatchStatus`.

### 3.2. Presentation Layer
1. **ViewModel (`FileDetectionViewModel`):**
   - Commands for file browsing, folder browsing, drag-and-drop ingestion, starting batch, canceling batch (`CancelBatchCommand` bound to inline cancel button), and clearing queue.
   - Observable properties for progress percentage, inline ETA string, status text, summary counters, item collection, and cancellation availability (`CanCancel`).
   - Integration with `INotificationService` and `IUserSettingsService` (reading default confidence threshold).
2. **View (`FileDetectionView.xaml`):**
   - Fluent design with light/dark theme adaptation.
   - Large drag-and-drop drop-target zone.
   - Progress bar with inline ETA indicator and adjacent Cancel Processing button.
   - Virtualized queue/results grid with status chips and defect counts.

### 3.3. Verification & Acceptance
1. Comprehensive unit tests covering file discovery, ETA calculations, batch execution, cancellation, and ViewModel state transitions.
2. Integration tests verifying end-to-end batch processing with real sample PCB images (`SH-1.jpg`, `SP-1.jpg`, etc.) and SQLite database verification.
3. Reqnroll BDD scenarios verifying single-file inspection, multi-file batch execution with ETA, cancellation, and toast alerts.

---

## 4. Out-of-Scope (Deferred to Later Phases)
- **Live Camera Streaming & Real-Time HUD:** Belongs to Phase 6 (`LiveDetectionView`).
- **History Gallery Thumbnail Grid & Inline Renaming:** Belongs to Phase 7 (`HistoryView`).
- **Interactive Deep-Zoom Defect Viewer & Viewport Cropping:** Belongs to Phase 7 (`InteractiveDefectViewer`).
- **Aggregate Statistics Dashboard & Trend Charts:** Belongs to Phase 8 (`DashboardView`).
- **Audit-Ready PDF Report Exporting:** Belongs to Phase 9 (`QuestPdfExportEngine`).
- **Large Video File Streaming & Chunking:** Future enhancement (Post-MVP).

---

## 5. Defect Taxonomy Mapping
The batch detection engine evaluates all 9 standardized defect classes from `DefectKnowledgeBase`:
1. `SH` — Short
2. `OP` — Open
3. `MB` — Mouse bite
4. `SP` — Spur
5. `SC` — Spurious copper
6. `HB` — Hole breakout
7. `CS` — Conductor scratch
8. `CFO` — Conductor foreign object
9. `BMFO` — Base material foreign object
