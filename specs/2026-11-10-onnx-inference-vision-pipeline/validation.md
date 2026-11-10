# Validation Criteria: Phase 3 — ONNX Inference Pipeline & Vision Utilities

## 1. Build & Quality Gates
- **Command:** `dotnet build pcb-deftek.csproj`
- **Pass Condition:** Exit code 0, 0 compiler errors, 0 compiler warnings.
- **Nullable Reference Types:** Nullable strictly enabled across all new interfaces, classes, and records.

## 2. Automated Test Suite Gates
- **Command:** `dotnet test pcb-deftek.slnx`
- **Pass Condition:**
  - 100% tests passing, 0 failed, 0 skipped.
  - All unit tests in `pcb-deftek.Tests` and BDD acceptance scenarios in `pcb-deftek.Bdd` pass cleanly.
- **Test Image Fixtures:** In case automated model tests need sample PCB test images, they can be found in `tests/pcb-deftek.Tests/TestData/Model/`.
## 3. Functional Acceptance Criteria
- **Model Loading:** `Assets/Models/best.onnx` loads and initializes without throwing exceptions.
- **Hardware Fallback:** If DirectML GPU acceleration is unavailable, model execution transparently falls back to CPU without crashing.
- **Aspect Ratio Integrity:** Letterboxing preserves image geometry by padding rather than stretching.
- **Normalization Invariant:** Model inputs strictly conform to `[1, 3, 640, 640]` float values in $[0.0, 1.0]$.
- **NMS & Clamping:** Non-Maximum Suppression eliminates redundant candidate boxes; all output coordinates are normalized and clamped to $[0.0, 1.0]$.
- **Diagnostics:** Each inference returns timing breakdowns (`PreprocessingTimeMs`, `InferenceTimeMs`, `PostprocessingTimeMs`, `TotalTimeMs`) and the active `ExecutionDevice`.

## 4. Architecture & Dependency Compliance
- Dependency injection registration verified for `IOnnxModelRunner`, `IVisionPreprocessor`, `IYoloPostprocessor`, and `IPcbDefectDetector`.
- Zero UI thread blocking: All inference calls support asynchronous execution and cancellation tokens.
- Custom exception handling: Inference errors wrap lower-level ONNX/native failures into `InferenceException` or `ModelExecutionException`.
