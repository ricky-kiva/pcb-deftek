# Validation Criteria: Phase 5 — File & Batch Detection Engine

## 1. Build & Quality Gates
- **Command:** `dotnet build pcb-deftek.csproj`
- **Pass Condition:** Exit code 0, 0 compiler errors, 0 compiler warnings.
- **Nullable Reference Types:** Nullable strictly enabled across all new services, models, and ViewModels with zero warnings.
- **Dependency Audit:** Socket CLI (`socket`) verification for any new dependency (none required — all dependencies already installed).

---

## 2. Automated Test Suite Gates
- **Command:** `dotnet test pcb-deftek.slnx`
- **Pass Condition:**
  - 100% tests passing, 0 failed, 0 skipped.
  - All existing 310 tests (Persistence, Inference, Application Shell, Navigation, Settings, BDD) continue to pass without regression.
  - All new unit, integration, and BDD tests for Phase 5 pass cleanly.

---

## 3. Functional Acceptance Criteria

### 3.1. File Discovery & Dialog Ingestion
- **Formats:** Successfully filters and discovers `.jpg`, `.jpeg`, `.png`, `.bmp`, and `.webp` files (case-insensitive).
- **Directory Traversal:** Recursively discovers image files within nested folders while skipping non-image and hidden files.
- **Deduplication:** Omits duplicate file paths if added multiple times.
- **Dialog Abstraction:** `IFilePickerService` cleanly prompts native Windows file and folder pickers without coupling ViewModels to WPF UI classes.

### 3.2. Drag-and-Drop User Experience
- **Drop Target Area:** Large, prominent upload area styled with Fluent typography and dashed border.
- **Drag Feedback:** Provides visual highlight feedback when files/folders are hovered over the drop target.
- **Drop Ingestion:** Dropping single files, multiple files, or directory folders populates the queue correctly.
- **Graceful Rejection:** Dropping invalid or non-file items causes no exceptions or crashes.

### 3.3. Batch Processing & Fault Tolerance
- **Asynchronous Execution:** Batch executes entirely off the UI thread via `IBatchInspectionEngine`.
- **Sequential Safety:** Items are processed sequentially to prevent GPU/CPU memory exhaustion on DirectML.
- **Individual File Resilience:** A corrupted or invalid image file records an `Error` item state, logs a warning via Serilog, and allows the batch to proceed to the next item without aborting.

### 3.4. Inline Progress & Dynamic ETA Calculation
- **Progress Bar:** Reflects exact integer `PercentComplete` (0–100%).
- **Inline Text:** Displays formatted progress: `Processed N of Total (X%) • ETA: mm:ss`.
- **Warmup State:** Displays `"Calculating..."` during initial warmup (first 1–2 items) before displaying stabilized rolling average times.
- **Counters:** Live badges reflect Total, Passed (green), and Failed (red) counts as each item completes.

### 3.5. Persistence Integration
- **Record Creation:** Every successfully inspected board is persisted into SQLite via `IInspectionRepository.InsertAsync`.
- **Mode Tagging:** Single-item inspections are tagged with `DetectionMode.SingleFile`; multi-item/folder batch inspections are tagged with `DetectionMode.BatchFolder`.
- **Defects & Coordinates:** All detected defect bounding boxes and class IDs are saved and retrievable.

### 3.6. Desktop Toast Notifications
- **Completion Alert:** When batch processing finishes, dispatches a Windows desktop toast notification via `INotificationService.ShowToastAsync` summarizing total processed, pass count, and fail count.
- **Headless Fallback:** In test/headless environments, notifications dispatch safely without throwing COM/WinRT exceptions.

### 3.7. Cancellation Support
- **Cancel Button:** Operator can request cancellation at any point during active batch execution.
- **Clean Halt:** The engine completes or cancels the current item and halts further processing.
- **Preserved Data:** Previously processed items remain safely committed in SQLite.

---

## 4. Performance & Threading Verification
- **UI Responsiveness:** Shell navigation and window interactions remain completely fluid during large batch processing (60 FPS, no UI freeze).
- **Native Memory Cleanup:** Native OpenCV `Mat` buffers and ONNX tensors are disposed promptly per iteration.

---

## 5. Definition of Done (DoD)
Before Phase 5 can be merged into `main`:
1. All 5 Task Groups defined in `specs/2026-11-12-file-batch-detection-engine/plan.md` are completed.
2. Checkpoints 1 through 5 are verified and checked off.
3. Solution compiles with 0 errors and 0 warnings.
4. Full test suite (`pcb-deftek.slnx`) passes 100%.
5. Pull Request is submitted and passes the 4-step Feature Review and Quality Improvement pipeline (`code-review-and-quality`, `code-simplification`, `performance-optimization`, `security-and-hardening`).
