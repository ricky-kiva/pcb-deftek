# Validation Criteria: Phase 2 — Domain Models & Database Persistence

## 1. Build & Compiler Gate
- **Command:** `dotnet build pcb-deftek.slnx`
- **Pass Condition:** 0 errors, 0 compiler warnings, nullable reference types strictly enforced across all domain and persistence classes.

## 2. Automated Test Gate
- **Command:** `dotnet test pcb-deftek.slnx`
- **Pass Condition:**
  - Total tests >= 35.
  - 100% tests passing, 0 failed, 0 skipped.
  - Both `tests/pcb-deftek.Tests` (xUnit) and `tests/pcb-deftek.Bdd` (Reqnroll) pass.
- **Coverage Areas:**
  - `DefectClassCatalog`: All 9 defect classes return valid metadata.
  - `BoundingBox`: Normalization bounds checks and clamping invariants.
  - `SqliteDatabaseMigrator`: Schema v1 applied idempotently on fresh and migrated databases.
  - `SqliteInspectionRepository`: Insert, read, date-range filtering, pagination, and deletion.
  - `SqliteDefectRepository`: Batch insertion, query by inspection ID, class distribution stats.
  - `SqliteSettingsRepository`: Key-value get/set/update roundtrip.
  - `SqliteDatabaseBackupService`: Hot backup snapshot generation and restore verification.
  - Reqnroll BDD Scenarios: Persisting an inspection with defects and retrieving it by date range.

## 3. Runtime Verification Gate
- Starting `pcb-deftek.exe` initializes the database at `%LOCALAPPDATA%\PcbDeftek\pcb-deftek\app.db`.
- SQLite database tables (`SchemaMigrations`, `Inspections`, `Defects`, `AppSettings`) are verified present with WAL mode active.

## 4. Architecture & Quality Gate
- Domain models under `Domain/Models/` contain zero UI dependencies.
- Repositories encapsulate raw SQL / ADO.NET and expose strongly-typed interfaces.
- Working tree is clean on `feature/domain-models-database-persistence`.
