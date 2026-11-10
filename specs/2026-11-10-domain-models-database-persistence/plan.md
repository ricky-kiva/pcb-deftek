# Implementation Plan: Phase 2 — Domain Models & Database Persistence

## Task Breakdown

### Architecture & Visual Specifications
- **Domain Model Class Diagram:** [resources/domain-model.md](./resources/domain-model.md)
- **Database ERD & Schema Mapping:** [resources/database-erd.md](./resources/database-erd.md)

### Task Group 1: Domain Entities & Defect Catalog
*(Refer to [resources/domain-model.md](./resources/domain-model.md) for class diagrams and entity relationships)*
- **Task 1.1:** Create `DefectClass` enum and static `DefectKnowledgeBase` in `Domain/Models/`:
  - Enumerate the 9 defect classes with explicit indices 0 to 8 (`short`, `spur`, `spurious_copper`, `open`, `mouse_bite`, `hole_breakout`, `conductor_scratch`, `conductor_foreign_object`, `base_material_foreign_object`).
  - Implement static `DefectKnowledgeBase` containing authoritative `DEFECT_KB` data (`explanation`, `severity`, `root_cause`, `impact`, and `action`).
  - *Acceptance:* Static knowledge base lookups return verified diagnostic metadata for all 9 classes by enum, code string, or index.
  - *Verify:* `dotnet test tests/pcb-deftek.Tests` verifying knowledge base completeness.
  - *Scope:* S (2 files)
- **Task 1.2:** Implement `BoundingBox`, `DetectionMode`, `InspectionStatus`, `DetectedDefect`, and `InspectionRecord`:
  - `BoundingBox` validation (validates 0.0 <= coordinate <= 1.0, width > 0, height > 0) with pixel/normalized factory methods.
  - `InspectionRecord` uses streamlined fields (`Id`, `Name`, `CreatedAtUtc`, `UpdatedAtUtc`, `Mode`, `DefectCount`, `ImagePath`) and computes derived `Status`.
  - `DetectedDefect` stores normalized coordinates and delegates diagnostic properties (`Explanation`, `Severity`, `RootCause`, `Impact`, `Action`) to static `DefectKnowledgeBase`.
  - Define boundary contracts in `Domain/Contracts/`: `CreateInspectionCommand`, `CreateDefectInput`, `InspectionQueryFilter`, and `PagedResult<T>`.
  - *Acceptance:* Domain models enforce coordinate clamping, immutability, and boundary input separation.
  - *Verify:* Unit tests verifying entity invariants, bounding box boundaries, and contract types.
  - *Scope:* M (5-6 files)

#### Checkpoint 1: Domain Models Validation
- [ ] Domain models, boundary contracts, and static knowledge base compile cleanly with nullable enabled.
- [ ] Unit tests verify bounding box validation, factory methods, and `DefectKnowledgeBase` lookups.

---

### Task Group 2: SQLite Connection Management & Migrations Runner
*(Refer to [resources/database-erd.md](./resources/database-erd.md) for ER diagram, table schemas, and domain-to-table mappings)*
- **Task 2.1:** Add `Microsoft.Data.Sqlite` dependency to `pcb-deftek.csproj`:
  - Verify package with Socket CLI.
  - Create `ISqliteConnectionFactory` and `SqliteConnectionFactory` enforcing WAL mode and foreign key constraints (`PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL;`).
  - *Acceptance:* Connection factory successfully opens connections with WAL mode and foreign keys enabled.
  - *Verify:* Unit/integration test verifying pragma settings.
  - *Scope:* S (2 files)
- **Task 2.2:** Implement Bidirectional `IDatabaseMigrator` and Migration Scripts (Per `deprecation-and-migration`):
  - Create `001_initial_schema.up.sql` defining streamlined `SchemaMigrations` (with Checksum & ExecutionDurationMs), `Inspections` (Id, Name, CreatedAtUtc, UpdatedAtUtc, DetectionMode, DefectCount, ImagePath), `Defects` (Id, InspectionId, ClassCode, Confidence, Bounding Box), and `AppSettings`.
  - Create `001_initial_schema.down.sql` providing a tested, clean rollback path.
  - Implement transactional migration runner verifying SHA-256 script checksums before execution.
  - *Acceptance:* Migrator applies pending migrations idempotently within transactions and rolls back cleanly using `.down.sql`.
  - *Verify:* Integration tests covering forward migration, idempotency, and full rollback down path against a temporary SQLite database.
  - *Scope:* M (4 files)

#### Checkpoint 2: Database Migration Validation
- [ ] Database schema migrations run idempotently on fresh and existing databases.
- [ ] Bidirectional migration verified: `.up.sql` creates schema and `.down.sql` cleanly rolls it back.
- [ ] Foreign key constraints, CHECK integrity constraints, and indices are verified active.
---

### Task Group 3: Repository Pattern Implementation
- **Task 3.1:** Implement `IInspectionRepository` and `SqliteInspectionRepository`:
  - CRUD operations: `InsertAsync`, `GetByIdAsync`, `GetPagedAsync`, `GetByDateRangeAsync`, `UpdateNameAsync`, `DeleteAsync`.
  - Date filtering supporting Day, Week, Month, and Custom date ranges.
  - *Acceptance:* Inspections are persisted, queried, filtered, and deleted with all streamlined fields retained.
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
