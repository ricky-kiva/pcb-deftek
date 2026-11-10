# Requirements & Context: Phase 3 — ONNX Inference Pipeline & Vision Utilities

## 1. Executive Summary & Context
Phase 3 implements the core machine learning inference and computer vision pipeline for `pcb-deftek`. It wraps the local YOLOv8 ONNX model (`Assets/Models/best.onnx`), performs aspect-ratio-preserving vision preprocessing, executes tensor inference with hardware acceleration (DirectML GPU with automatic CPU fallback), and postprocesses model outputs into verified domain `DetectedDefect` entities.

## 2. Model Specification & Tensor Contract
- **Model Path:** `Assets/Models/best.onnx` (YOLOv8 Object Detection)
- **Input Tensor:**
  - Name: `images`
  - Datatype: `float32`
  - Shape: `[1, 3, 640, 640]` (Batch: 1, Channels: 3 RGB, Height: 640, Width: 640)
  - Value range: Normalized $[0.0, 1.0]$
- **Output Tensor:**
  - Name: `output0`
  - Datatype: `float32`
  - Shape: `[1, 13, 8400]` (8400 candidate anchor boxes, 13 attributes per box)
  - Attribute mapping:
    - Index 0: `cx` (Center X in 640x640 space)
    - Index 1: `cy` (Center Y in 640x640 space)
    - Index 2: `w` (Width in 640x640 space)
    - Index 3: `h` (Height in 640x640 space)
    - Indices 4–12: Probabilities for the 9 standardized defect classes (`short`, `spur`, `spurious_copper`, `open`, `mouse_bite`, `hole_breakout`, `conductor_scratch`, `conductor_foreign_object`, `base_material_foreign_object`)

## 3. In-Scope Deliverables
1. **Dependencies & Security Inspection:**
   - `Microsoft.ML.OnnxRuntime.DirectML`: ONNX execution with DirectML GPU provider and automatic CPU fallback.
   - `SkiaSharp`: High-performance cross-platform bitmap manipulation, letterboxing, and RGB channel extraction.
   - Inspection of packages via Socket CLI before installation.
2. **Model Runner & Hardware Execution (`Infrastructure/Inference/`):**
   - `ExecutionDevice` enum (`Cpu`, `GpuDirectML`).
   - `InferenceOptions` configuration (Device preference, device ID, thread count, confidence threshold, NMS IoU threshold).
   - `IOnnxModelRunner` & `OnnxModelRunner`: Thread-safe session management, warm-up execution, dynamic device fallback from DirectML to CPU if DirectX 12 hardware or DirectML provider fails, and resource disposal.
3. **Computer Vision Preprocessing Engine (`Infrastructure/Vision/`):**
   - `IVisionPreprocessor` & `SkiaVisionPreprocessor`:
     - Aspect-ratio-preserving letterboxing to 640x640 with neutral gray padding (`RGB 114, 114, 114`).
     - Planar RGB conversion and float normalization to dense `[1, 3, 640, 640]` float tensor.
     - `LetterboxTransform` metadata tracking scale and offsets for exact coordinate inversion.
4. **YOLOv8 Postprocessing & NMS Engine (`Infrastructure/Inference/`):**
   - `IYoloPostprocessor` & `YoloPostprocessor`:
     - Candidate filtering against configurable confidence threshold (default 0.25).
     - Greedy Non-Maximum Suppression (NMS) with configurable IoU threshold (default 0.45).
     - Inverse letterbox coordinate projection mapping model coordinates back to normalized $[0.0, 1.0]$ `BoundingBox` and domain `DetectedDefect` entities.
5. **High-Level Detection Service (`Application/Services/`):**
   - `IPcbDefectDetector` & `PcbDefectDetector`: Orchestrates preprocessing, inference, postprocessing, and diagnostic latency measurement (`InferenceResult` containing detected defects, timing breakdown, and active execution device).
6. **DI Registration & Verification Harness:**
   - Wire all inference components into `App.ConfigureServices`.
   - Comprehensive unit and Reqnroll BDD tests against sample PCB images (`tests/pcb-deftek.Tests/TestData/Model/`).

## 4. Out-of-Scope (Deferred to Later Phases)
- Camera video capture and DirectShow/OpenCvSharp frame streaming (Phase 6).
- Batch folder asynchronous file workers (Phase 5).
- UI Views, sliders, and navigation controls (Phase 4).
