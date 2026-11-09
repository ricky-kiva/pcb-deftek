# Validation Criteria: Phase 1 — Foundation & Core Infrastructure

## 1. Build & Compiler Gate
- **Command:** `dotnet build pcb-deftek.slnx`
- **Pass Condition:** Exit code 0, 0 compilation errors, 0 compiler warnings.
- Nullable reference types strictly observed across all new source and test files.

## 2. Automated Test Gate
- **Command:** `dotnet test pcb-deftek.slnx`
- **Pass Condition:**
  - Total tests >= 10.
  - 100% tests passing, 0 failed, 0 skipped.
  - Both `tests/pcb-deftek.Tests` (xUnit) and `tests/pcb-deftek.Bdd` (Reqnroll) execute.
- **Test Scenarios Covered:**
  - Dependency Injection container resolves registered services and `MainWindow`.
  - Serilog logger configuration and log event creation.
  - Entire domain exception hierarchy verification (inheritance from `PcbDeftekException`, message retention, inner exceptions, contextual properties).
  - Centralized exception dispatcher handling and structured error logging.
  - Reqnroll BDD feature scenario for application bootstrap.

## 3. Runtime Verification Checks
- **DI Resolution Check:** Launching application without `StartupUri` successfully resolves `MainWindow` and dependencies via `IServiceProvider`.
- **Serilog File Output Check:** Starting the application generates a log file in `%LOCALAPPDATA%\PcbDeftek\pcb-deftek\logs\` containing timestamped startup log event.
- **Centralized Exception Dispatcher Check:** Raising an unhandled dispatcher exception records an Error/Fatal log entry with call stack and marks event handled.

## 4. Architecture & Code Quality Gate
- All domain exceptions reside strictly under `Common/Exceptions/` and inherit from `PcbDeftekException`.
- Zero business logic or direct control manipulation in `App.xaml.cs`.
- Working tree is clean on branch `feature/foundation-core-infrastructure`.
