# Implementation Plan: Phase 2 — Domain Models & Database Persistence

## Task Breakdown

### Task Group 1: Domain Entities & Defect Catalog
- **Task 1.1:** Create `DefectClass` enum and `DefectClassCatalog` in `Domain/Models/`:
  - Enumerate the 9 defect classes (SH, OP, MB, SP, SC, HB, CS, CFO, BMFO).
  - Populate catalog with default root cause, impact, severity, and corrective action for each class.
  - *Acceptance:* Catalog lookup returns complete diagnostic metadata for every defect class.
  - *Verify:* `dotnet test tests/pcb-deftek.Tests` verifying catalog completeness.
  - *Scope:* S (2 files)
- **Task 1.2:** Implement `BoundingBox`, `DetectionMode`, `InspectionStatus`, `DetectedDefect`, and `InspectionRecord`:
  - `BoundingBox` validation (validates 0.0 <= coordinate <= 1.0).
  - `InspectionRecord` computes `Status` (Pass if DefectCount == 0, Fail otherwise).
  - *Acceptance:* Domain models enforce coordinate clamping and pass/fail invariants.
  - *Verify:* Unit tests verifying entity invariants and bounding box boundaries.
  - *Scope:* S (4-5 files)

#### Checkpoint 1: Domain Models Validation
- [ ] Domain models and catalog compile cleanly with nullable enabled.
- [ ] Unit tests verify bounding box validation and defect metadata lookups.

---

### Task Group 2: SQLite Connection Management & Migrations Runner
- **Task 2.1:** Add `Microsoft.Data.Sqlite` dependency to `pcb-deftek.csproj`:
  - Verify package with Socket CLI.
  - Create `ISqliteConnectionFactory` and `SqliteConnectionFactory` enforcing WAL mode and foreign key constraints.
  - *Acceptance:* Connection factory successfully opens connections with WAL mode and foreign keys enabled.
  - *Verify:* Unit/integration test verifying pragma settings.
  - *Scope:* S (2 files)
- **Task 2.2:** Implement `IDatabaseMigrator` and `001_InitialSchema.sql`:
  - Create embedded SQL script defining `SchemaMigrations`, `Inspections`, `Defects`, and `AppSettings`.
  - Implement migration runner executing pending scripts inside a transaction.
  - *Acceptance:* Migrator creates tables and records version in `SchemaMigrations`. Re-running is idempotent.
  - *Verify:* Integration test against in-memory or temporary SQLite database.
  - *Scope:* M (3 files)

#### Checkpoint 2: Database Migration Validation
- [ ] Database schema migrations run idempotently on fresh and existing databases.
- [ ] Foreign key constraints and indices are verified active.

---

### Task Group 3: Repository Pattern Implementation
- **Task 3.1:** Implement `IInspectionRepository` and `SqliteInspectionRepository`:
  - CRUD operations: `InsertAsync`, `GetByIdAsync`, `GetPagedAsync`, `GetByDateRangeAsync`, `UpdateCustomNameAsync`, `DeleteAsync`.
  - Date filtering supporting Day, Week, Month, and Custom date ranges.
  - *Acceptance:* Inspections are persisted, queried, filtered, and deleted with all fields retained.
  - *Verify:* Integration tests covering CRUD and date range filtering.
  - *Scope:* M (2 files)
- **Task 3.2:** Implement `IDefectRepository` and `SqliteDefectRepository`:
  - `InsertBatchAsync`, `GetByInspectionIdAsync`, `GetClassDistributionAsync`.
  - Cascading delete verification when parent inspection is removed.
  - *Acceptance:* Defects persist with normalized bounding boxes and link correctly to inspections.
  - *Verify:* Integration tests for defect queries and cascading deletion.
  - *Scope:* M (2 files)
- **Task 3.3:** Implement `ISettingsRepository` and `SqliteSettingsRepository`:
  - `GetSettingAsync<T>`, `SetSettingAsync<T>`, `GetAllSettingsAsync`.
  - *Acceptance:* Application settings persist and serialize/deserialize primitives and strings.
  - *Verify:* Integration tests verifying settings round-trip.
  - *Scope:* S (2 files)

#### Checkpoint 3: Repository Integration Validation
- [ ] All repository operations pass 100% in test suite against temporary SQLite database.
- [ ] Cascading deletion and aggregate queries operate as expected.

---

### Task Group 4: Database Backup Service & Application Wiring
- **Task 4.1:** Implement `IDatabaseBackupService` and `SqliteDatabaseBackupService`:
  - Hot database backup using `SqliteConnection.BackupDatabase`.
  - Backup file naming with timestamp under `%LOCALAPPDATA%\PcbDeftek\pcb-deftek\backups\`.
  - Retention cleanup pruning backups older than 30 days.
  - *Acceptance:* Backup snapshot creates an identical, restorable copy of the active database.
  - *Verify:* Integration test asserting backup creation and integrity check.
  - *Scope:* S (2 files)
- **Task 4.2:** Wire Persistence Services into DI and Application Startup:
  - Register connection factory, migrator, repositories, and backup service in `App.ConfigureServices`.
  - Execute database migration runner during `App.OnStartup`.
  - Add Reqnroll BDD scenarios in `tests/pcb-deftek.Bdd` for inspection persistence.
  - *Acceptance:* Application startup automatically migrates SQLite database. Full test suite passes.
  - *Verify:* `dotnet test pcb-deftek.slnx`
  - *Scope:* M (3-4 files)

#### Checkpoint 4: Solution-Wide Verification
- [ ] `dotnet test pcb-deftek.slnx` runs all unit and BDD tests with 100% pass rate.
- [ ] Application starts up and generates `app.db` with v1 schema intact.
