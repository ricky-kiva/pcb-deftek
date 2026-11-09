# Requirements & Context: Phase 2 — Domain Models & Database Persistence

## 1. Executive Summary & Context
Phase 2 establishes the core domain models and SQLite persistence layer for `pcb-deftek`. Every downstream capability—batch file processing, live camera inspection, history viewer, aggregate analytics dashboard, and audit PDF export—relies on a strongly-typed domain model and thread-safe data access layer.

## 2. Domain Taxonomy & Entities
- **Defect Taxonomy (9 Classes):**
  1. `Short` (SH)
  2. `Open` (OP)
  3. `MouseBite` (MB)
  4. `Spur` (SP)
  5. `SpuriousCopper` (SC)
  6. `HoleBreakout` (HB)
  7. `ConductorScratch` (CS)
  8. `ConductorForeignObject` (CFO)
  9. `BaseMaterialForeignObject` (BMFO)
- **Defect Catalog (`DefectClassCatalog`):** Provides standardized metadata for each defect class: Class code, Display name, Default root cause, Impact analysis, Severity level, and Recommended corrective action.
- **Domain Value Objects & Entities:**
  - `BoundingBox`: Normalized coordinates `(X, Y, Width, Height)` with range bounds `[0.0, 1.0]`.
  - `DetectedDefect`: Entity containing `Id`, `InspectionId`, `ClassCode`, `ClassName`, `Confidence`, `BoundingBox`, `Severity`, `RootCause`, `Impact`, `RecommendedAction`.
  - `InspectionRecord`: Aggregate root containing `Id`, `CustomName` (editable, default `date-number`), `TimestampUtc`, `DetectionMode` (`File`, `LiveCamera`), `Status` (`Pass`, `Fail`), `DefectCount`, `InferenceTimeMs`, `DeviceUsed`, `ImagePath`, and list of `DetectedDefect` items.
  - `InspectionStatus`: Enum (`Pass` = 0 defects, `Fail` >= 1 defect).
  - `DetectionMode`: Enum (`SingleFile`, `BatchFolder`, `LiveCamera`).

## 3. Database & Persistence Architecture
- **Engine:** `Microsoft.Data.Sqlite` (high performance, lightweight ADO.NET SQLite provider).
- **Pragmas & Concurrency:** Enable WAL mode (`PRAGMA journal_mode = WAL;`) and enforce foreign keys (`PRAGMA foreign_keys = ON;`).
- **Database Location:** `%LOCALAPPDATA%\PcbDeftek\pcb-deftek\app.db` (overrideable for testing).
- **Proposed Schema (v1):**
  - `SchemaMigrations`: Tracks applied schema migration versions and timestamps.
  - `Inspections`: Stores inspection sessions with indexed `TimestampUtc`, `Status`, and `DetectionMode`.
  - `Defects`: Stores individual bounding boxes and defect classifications with foreign key cascading delete to `Inspections`.
  - `AppSettings`: Key-value configuration table.
- **Automated Migration Runner:** Executes embedded transactional SQL scripts (`001_InitialSchema.sql`) on startup.
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
