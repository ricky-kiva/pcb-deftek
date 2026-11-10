# Requirements & Context: Phase 2 — Domain Models & Database Persistence

## 1. Executive Summary & Context
Phase 2 establishes the core domain models and SQLite persistence layer for `pcb-deftek`. Every downstream capability—batch file processing, live camera inspection, history viewer, aggregate analytics dashboard, and audit PDF export—relies on a strongly-typed domain model and thread-safe data access layer.

## 2. Domain Taxonomy & Entities
- **Defect Taxonomy (9 Classes per `CLASS_NAMES`):**
  - `0: short` (Short)
  - `1: spur` (Spur)
  - `2: spurious_copper` (Spurious copper)
  - `3: open` (Open)
  - `4: mouse_bite` (Mouse bite)
  - `5: hole_breakout` (Hole breakout)
  - `6: conductor_scratch` (Conductor scratch)
  - `7: conductor_foreign_object` (Conductor foreign object)
  - `8: base_material_foreign_object` (Base material foreign object)
- **Static Defect Knowledge Base (`DefectKnowledgeBase`):**
  Centralized static source containing authoritative diagnostic metadata (`DEFECT_KB`):
  - `Explanation`: Descriptive definition of the defect anomaly.
  - `Severity`: Impact level (`Critical`, `High`, `Medium-High`, `Medium`, `Low-Medium`).
  - `RootCause`: Manufacturing or fabrication origin.
  - `Impact`: Circuit reliability and operational consequences.
  - `Action`: Recommended immediate engineering remediation.
- **Domain Value Objects & Entities:**
  - `BoundingBox`: Immutable normalized coordinates `(X, Y, Width, Height)` with range bounds `[0.0, 1.0]` and factory/clamping utilities.
  - `DetectedDefect`: Entity containing `Id`, `InspectionId`, `ClassCode`, `Confidence`, `BoundingBox`, with diagnostic properties (`Explanation`, `Severity`, `RootCause`, `Impact`, `Action`) resolved via static `DefectKnowledgeBase`.
  - `InspectionRecord`: Aggregate root containing `Id`, `Name` (editable, default `date-number`), `CreatedAtUtc`, `UpdatedAtUtc`, `DetectionMode` (`SingleFile`, `BatchFolder`, `LiveCamera`), `DefectCount`, `ImagePath`, and list of `DetectedDefect` items. `Status` is derived dynamically (`DefectCount == 0 ? Pass : Fail`).
  - `InspectionStatus`: Enum (`Pass` = 0 defects, `Fail` >= 1 defect).
  - `DetectionMode`: Enum (`SingleFile`, `BatchFolder`, `LiveCamera`).
- **Boundary Contracts (Per `api-and-interface-design`):**
  - `CreateInspectionCommand` & `CreateDefectInput`: Input boundary contracts separating caller parameters from server/aggregate-generated fields.
  - `InspectionQueryFilter`: Query parameters for date range, mode, status, pagination, and sorting.
  - `PagedResult<T>`: Deterministic paginated collection result with metadata (`TotalItems`, `TotalPages`, `HasNextPage`).

## 3. Database & Persistence Architecture
- **Engine:** `Microsoft.Data.Sqlite` (high performance, lightweight ADO.NET SQLite provider).
- **Pragmas & Concurrency:** Enable WAL mode (`PRAGMA journal_mode = WAL;`) and enforce foreign keys (`PRAGMA foreign_keys = ON;`).
- **Database Location:** `%LOCALAPPDATA%\PcbDeftek\pcb-deftek\app.db` (overrideable for testing).
- **Proposed Schema (Streamlined v1 with CHECK constraints):**
  - `SchemaMigrations`: Auditable migration tracking (`Version`, `AppliedAtUtc`, `Description`, `Checksum`, `ExecutionDurationMs`).
  - `Inspections`: Streamlined columns (`Id`, `Name`, `CreatedAtUtc`, `UpdatedAtUtc`, `DetectionMode`, `DefectCount`, `ImagePath`). Indexed by `CreatedAtUtc` and `DetectionMode`.
  - `Defects`: Normalized columns (`Id`, `InspectionId`, `ClassCode`, `Confidence`, `BoxX`, `BoxY`, `BoxWidth`, `BoxHeight`) with `ON DELETE CASCADE` and coordinate/confidence CHECK constraints.
  - `AppSettings`: Key-value configuration table.
- **Bidirectional Migration Runner:**
  - Transactional execution with SHA-256 script checksum validation.
  - Explicit forward (`001_initial_schema.up.sql`) and rollback (`001_initial_schema.down.sql`) migration scripts.
  - Pre-migration backup snapshot safety before applying scripts.
- **Database Backup Service:** Uses SQLite Online Backup API (`SqliteConnection.BackupDatabase`) to create hot snapshots under `%LOCALAPPDATA%\PcbDeftek\pcb-deftek\backups\app-backup-YYYYMMDD-HHmmss.db` with a 30-day retention policy.
- **Repository Pattern:**
  - `IInspectionRepository`: CRUD, pagination, date-range filtering (day, week, month, custom), and pass/fail summary statistics.
  - `IDefectRepository`: Queries by inspection, defect class frequency aggregation, severity distribution.
  - `ISettingsRepository`: Key-value application settings persistence.

## 4. In-Scope Deliverables
- Domain models and defect catalog under `Domain/Models/`.
- SQLite database connection factory and migration runner under `Infrastructure/Persistence/`.
- Initial migration script (`001_InitialSchema.sql`).
- Repositories (`SqliteInspectionRepository`, `SqliteDefectRepository`, `SqliteSettingsRepository`).
- SQLite online backup service (`SqliteDatabaseBackupService`).
- DI container registration in `App.xaml.cs` and automatic migration execution on startup.
- Dual-tier tests: xUnit repository integration tests and Reqnroll BDD persistence scenarios.

## 5. Out-of-Scope (Deferred to Later Phases)
- ONNX model loading and inference pipelines (Phase 3).
- UI Views, Navigation shell, and Dashboard charts (Phases 4, 7, 8).
- Webcam stream integration (Phase 6).
