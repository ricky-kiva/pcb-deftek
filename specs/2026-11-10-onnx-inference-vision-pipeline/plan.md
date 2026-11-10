# Implementation Plan: Phase 3 — ONNX Inference Pipeline & Vision Utilities

## Task Breakdown

### Task Group 1: ONNX Runtime Dependencies, Hardware Providers & Model Runner
- **Task 1.1:** Add `Microsoft.ML.OnnxRuntime.DirectML`, `OpenCvSharp4`, and `OpenCvSharp4.Windows` NuGet packages:
  - Verify package safety via Socket CLI.
  - Update `pcb-deftek.csproj` with package references (replacing `SkiaSharp`).
  - *Acceptance:* Project builds cleanly with nullable enabled.
  - *Verify:* `dotnet build pcb-deftek.csproj`
  - *Scope:* S (1 file)
- **Task 1.2:** Implement `ExecutionDevice`, `InferenceOptions`, and `OnnxModelRunner`:
  - Create `ExecutionDevice` enum (`Cpu`, `GpuDirectML`).
  - Implement `IOnnxModelRunner` and `OnnxModelRunner` with session creation, input/output tensor metadata reflection, and automatic fallback from DirectML to CPU.
  - Implement model warm-up routine.
  - *Acceptance:* Model runner loads `Assets/Models/best.onnx`, executes dummy inference, and reports active execution device.
  - *Verify:* Unit tests verifying session loading, execution provider fallback, and tensor shapes.
  - *Scope:* M (3-4 files)

#### Checkpoint 1: Model Runner & Hardware Fallback Validation
- [x] ONNX model loads successfully from `Assets/Models/best.onnx`.
- [x] DirectML GPU provider initializes when compatible hardware is present, or falls back gracefully to CPU.
- [x] Unit tests pass with 100% success.

---

### Task Group 2: Vision Preprocessing & Letterbox Resizing Engine
- **Task 2.1:** Implement `LetterboxTransform` and `OpenCvVisionPreprocessor`:
  - Calculate aspect-ratio scale factors and centering padding offsets for any input image dimensions.
  - Letterbox-resize image onto 640x640 canvas with neutral gray padding (RGB: 114, 114, 114).
  - Extract planar RGB float array normalized to $[0.0, 1.0]$ in `[1, 3, 640, 640]` format.
  - *Acceptance:* Input images of arbitrary aspect ratios produce dense 640x640 float tensors with valid letterbox transform metadata.
  - *Verify:* Unit tests verifying channel ordering (RGB), normalization range ($[0.0, 1.0]$), and padding offsets.
  - *Scope:* M (3 files)

#### Checkpoint 2: Preprocessing Engine Validation
- [x] Arbitrary image aspect ratios (landscape, portrait, square) correctly letterbox to 640x640.
- [x] Extracted float tensor conforms strictly to `[1, 3, 640, 640]`.

---

### Task Group 3: YOLOv8 Postprocessing, Non-Maximum Suppression (NMS) & Bounding Box Mapping
- **Task 3.1:** Implement `IYoloPostprocessor` and `YoloPostprocessor`:
  - Extract 8400 candidate boxes from `[1, 13, 8400]` output tensor.
  - Filter candidates by confidence threshold ($0.0 \dots 1.0$, default 0.25).
  - Implement greedy Non-Maximum Suppression (IoU calculation) with configurable threshold (default 0.45).
  - Invert letterbox transform to project bounding box coordinates back to normalized $[0.0, 1.0]$ domain coordinates.
  - Map class indices to verified `DefectClass` enums and diagnostic knowledge base metadata.
  - *Acceptance:* Postprocessor accurately extracts non-overlapping defect bounding boxes and constructs valid `DetectedDefect` entities.
  - *Verify:* Unit tests verifying candidate filtering, NMS overlap suppression, and coordinate mapping.
  - *Scope:* M (3 files)

#### Checkpoint 3: Postprocessing & NMS Engine Validation
- [x] Synthetic candidate tensors with overlapping detections are properly pruned by NMS.
- [x] Coordinates are correctly mapped back to normalized $[0.0, 1.0]$ space without distortion.
- [x] All 9 defect classes map correctly to `DefectClass` enum.

---

### Task Group 4: Integrated Inference Service, DI Wiring & Benchmark Verification
- **Task 4.1:** Implement `IPcbDefectDetector` and `PcbDefectDetector`:
  - Orchestrate end-to-end pipeline: Input image $\rightarrow$ Preprocessing $\rightarrow$ ONNX execution $\rightarrow$ Postprocessing $\rightarrow$ `InferenceResult`.
  - Capture precise diagnostics: preprocessing time, model execution time, postprocessing time, total latency, and active execution device.
  - *Acceptance:* Unified service processes image input and outputs full defect list with diagnostic metrics.
  - *Verify:* Integration tests with sample PCB images.
  - *Scope:* S (2 files)
- **Task 4.2:** DI Registration & Application Wiring:
  - Register `IOnnxModelRunner`, `IVisionPreprocessor`, `IYoloPostprocessor`, and `IPcbDefectDetector` in `App.ConfigureServices`.
  - Add Reqnroll BDD acceptance scenarios in `tests/pcb-deftek.Bdd` for defect inference.
  - *Acceptance:* All inference services resolve from DI container, and full test suite passes.
  - *Verify:* `dotnet test pcb-deftek.slnx`
  - *Scope:* M (3-4 files)

#### Checkpoint 4: Solution-Wide Inference Verification
- [ ] `dotnet test pcb-deftek.slnx` runs all unit and BDD tests with 100% pass rate.
- [ ] End-to-end inference detects defects and records latency metrics.
