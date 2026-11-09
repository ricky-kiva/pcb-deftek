# Implementation Plan: Phase 1 — Foundation & Core Infrastructure

## Task Breakdown

### Task Group 1: Dependency Configuration & Structured Logging
- **Task 1.1:** Add core NuGet dependencies to `pcb-deftek.csproj`:
  - `Microsoft.Extensions.DependencyInjection`
  - `Serilog`
  - `Serilog.Sinks.File`
  - `Serilog.Sinks.Debug`
  - `Serilog.Extensions.Logging`
  - *Acceptance:* Project builds cleanly with `<Nullable>enable</Nullable>`.
  - *Verify:* `dotnet build pcb-deftek.csproj`
  - *Scope:* XS (1 file)
- **Task 1.2:** Configure Serilog logging infrastructure in `App.xaml.cs`:
  - Configure rolling file sink at `%LOCALAPPDATA%\PcbDeftek\pcb-deftek\logs\app-.log` (retained for 30 days) and debug sink.
  - Implement flush and close on application exit.
  - *Acceptance:* Starting the application creates a structured log entry on startup.
  - *Verify:* Run app and verify log file exists with startup entry.
  - *Scope:* S (1-2 files)

#### Checkpoint 1: Logging Validation
- [x] `dotnet build` succeeds with 0 errors, 0 warnings.
- [x] Log file is created on application startup.
---

### Task Group 2: Dependency Injection & Application Lifecycle
- **Task 2.1:** Implement DI container bootstrap in `App.xaml.cs`:
  - Configure `IServiceProvider` with `ServiceCollection`.
  - Register core logging and foundational services.
  - *Acceptance:* Service provider builds cleanly and resolves registered services.
  - *Verify:* Unit test asserting service resolution.
  - *Scope:* S (1-2 files)
- **Task 2.2:** Wire `MainWindow` resolution through DI:
  - Update `App.xaml` (remove `StartupUri="MainWindow.xaml"`, override `OnStartup`).
  - Resolve and display `MainWindow` from `IServiceProvider`.
  - *Acceptance:* Application launches `MainWindow` resolved through the DI container.
  - *Verify:* `dotnet run --project pcb-deftek.csproj` successfully displays window.
  - *Scope:* S (2 files)

#### Checkpoint 2: DI Lifecycle Validation
- [ ] `MainWindow` displays without `StartupUri`.
- [ ] Service provider cleanly manages disposal on shutdown.

---

### Task Group 3: Common Exception Hierarchy & Centralized Dispatcher
- **Task 3.1:** Create domain exception classes under `Common/Exceptions/`:
  - `PcbDeftekException` (abstract base application exception)
  - `InferenceException` & `ModelLoadingException`, `ModelExecutionException`
  - `HardwareDeviceException` & `DeviceUnavailableException`, `CameraCaptureException`
  - `InspectionException` & `FileInspectionException`, `ImageProcessingException`
  - `PersistenceException` & `DatabaseMigrationException`, `DatabaseBackupException`
  - `ExportException`
  - `ValidationException`
  - `ConfigurationException`
  - *Acceptance:* All exceptions derive from `PcbDeftekException`, support inner exceptions, and retain contextual error properties.
  - *Verify:* Unit tests verifying inheritance, serialization/message formatting, and contextual properties.
  - *Scope:* M (10-12 files)
- **Task 3.2:** Wire centralized WPF exception handlers in `App.xaml.cs`:
  - Hook `DispatcherUnhandledException`, `AppDomain.CurrentDomain.UnhandledException`, and `TaskScheduler.UnobservedTaskException`.
  - Log fatal error via Serilog and gracefully inform the user without silent crashes.
  - *Acceptance:* Simulated unhandled exception is caught, logged with stack trace, and marked as handled.
  - *Verify:* Unit/integration test exercising handler logic.
  - *Scope:* S (1-2 files)

#### Checkpoint 3: Exception Safety Validation
- [ ] Exception types compile and enforce descriptive error contexts.
- [ ] Unhandled exception simulation proves logger captures error.

---

### Task Group 4: Dual-Tier Testing Projects Setup (xUnit & Reqnroll)
- **Task 4.1:** Setup `tests/pcb-deftek.Tests` (xUnit):
  - Create xUnit project targeting `net10.0-windows`.
  - Add references to `pcb-deftek.csproj`, `FluentAssertions`, `Moq`.
  - Add to `pcb-deftek.slnx`.
  - Implement tests for DI resolution and Exception hierarchy.
  - *Acceptance:* `dotnet test tests/pcb-deftek.Tests` passes 100%.
  - *Verify:* `dotnet test tests/pcb-deftek.Tests`
  - *Scope:* M (3 files)
- **Task 4.2:** Setup `tests/pcb-deftek.Bdd` (Reqnroll):
  - Create Reqnroll project targeting `net10.0-windows` with xUnit test runner.
  - Add reference to `pcb-deftek.csproj` and add to `pcb-deftek.slnx`.
  - Add feature file `AppBootstrap.feature` with Given/When/Then steps verifying app initialization and service availability.
  - *Acceptance:* Reqnroll test executes and passes.
  - *Verify:* `dotnet test tests/pcb-deftek.Bdd`
  - *Scope:* M (3-4 files)

#### Checkpoint 4: Solution-Wide Verification
- [ ] `dotnet test` runs both test projects with 100% pass rate.
- [ ] Solution builds cleanly with 0 warnings.
