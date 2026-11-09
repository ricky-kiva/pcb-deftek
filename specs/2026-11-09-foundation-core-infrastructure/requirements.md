# Requirements & Context: Phase 1 — Foundation & Core Infrastructure

## 1. Executive Summary & Context
Phase 1 establishes the architectural bedrock for `pcb-deftek`. Before implementing machine learning models, camera pipelines, SQLite persistence, or interactive UI views, the application requires a deterministic dependency injection (DI) lifecycle, structured logging (Serilog), a domain exception hierarchy with centralized unhandled exception dispatching, and a dual-tier automated test harness (xUnit + Reqnroll BDD).

## 2. Architectural Decisions
- **Dependency Injection (DI):** Utilize `Microsoft.Extensions.DependencyInjection` hosted within `App.xaml.cs`. ViewModels, services, and windows are registered with appropriate lifetimes (`Singleton` for hardware/inference/logging services, `Transient` for transient views/viewmodels).
- **Structured Logging:** Utilize `Serilog` with rolling file sink (`Serilog.Sinks.File`) writing to `%LOCALAPPDATA%\PcbDeftek\pcb-deftek\logs\app-.log` and debug console (`Serilog.Sinks.Debug`). Log format includes timestamp, log level, SourceContext, thread ID, and exception stack trace.
- **Centralized Exception Handling:** Implement a domain exception tree rooted at `PcbDeftekException` under `/Common/Exceptions`. Wire `DispatcherUnhandledException`, `AppDomain.CurrentDomain.UnhandledException`, and `TaskScheduler.UnobservedTaskException` in `App.xaml.cs` to prevent unhandled app crashes and record structured crash diagnostics.
- **Testing Infrastructure:** Configure two separate test projects in the solution (`pcb-deftek.slnx`):
  1. `tests/pcb-deftek.Tests`: xUnit + FluentAssertions + Moq for fast unit and service integration tests.
  2. `tests/pcb-deftek.Bdd`: Reqnroll + xUnit runner for behavior-driven acceptance test scenarios.

## 3. In-Scope Deliverables
- Project package references for DI, Serilog, and logging extensions in `pcb-deftek.csproj`.
- DI container initialization and `MainWindow` resolution in `App.xaml.cs`.
- Serilog bootstrap and runtime configuration with rolling file logging.
- Domain exception hierarchy under `Common/Exceptions/`:
  - `PcbDeftekException` (abstract base application exception)
  - `InferenceException` (base ML inference failure)
    - `ModelLoadingException` (missing, unreadable, or corrupt ONNX model file)
    - `ModelExecutionException` (tensor allocation, shape mismatch, or inference execution failure)
  - `HardwareDeviceException` (base hardware resource failure)
    - `DeviceUnavailableException` (requested DirectML GPU execution provider unsupported or unavailable)
    - `CameraCaptureException` (camera device missing, stream disconnected, or frame capture timeout)
  - `InspectionException` (base inspection and media processing failure)
    - `FileInspectionException` (unsupported media format, corrupt image/video, or batch I/O failure)
    - `ImageProcessingException` (bitmap decoding, letterbox transformation, or defect region cropping failure)
  - `PersistenceException` (base data persistence and SQLite storage failure)
    - `DatabaseMigrationException` (database schema migration runner or versioning failure)
    - `DatabaseBackupException` (database backup creation, verification, or restore failure)
  - `ExportException` (PDF report compilation, chart rendering, or audit export write failure)
  - `ValidationException` (invalid confidence threshold bounds, invalid filter date range, or invalid filename)
  - `ConfigurationException` (corrupt or unreadable local application configuration)
- Centralized exception handlers logging fatal events with user-facing dialog recovery.
- `tests/pcb-deftek.Tests` xUnit project added to solution with DI and exception tests.
- `tests/pcb-deftek.Bdd` Reqnroll project added to solution with initial application bootstrap feature.
- Full build and `dotnet test` execution passing cleanly with 0 warnings.

## 4. Out-of-Scope (Deferred to Later Phases)
- Domain entities, SQLite schema, and repositories (Phase 2).
- ONNX model loading and inference pipelines (Phase 3).
- Navigation shell, Fluent themes, and Settings UI (Phase 4).
- Camera capture and video feeds (Phase 6).
