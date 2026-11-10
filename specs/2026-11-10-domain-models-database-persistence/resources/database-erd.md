# Database Entity-Relationship Diagram (ERD) & Migration Architecture: Phase 2

This document defines the SQLite physical schema, constraints, indices, domain mapping contracts, and the bidirectional migration lifecycle in accordance with `api-and-interface-design` and `deprecation-and-migration`.

```mermaid
erDiagram
    SchemaMigrations {
        INTEGER Version PK "Sequential migration identifier"
        TEXT AppliedAtUtc "ISO 8601 UTC timestamp"
        TEXT Description "Summary of migration script"
        TEXT Checksum "SHA-256 hash of migration script"
        INTEGER ExecutionDurationMs "Execution elapsed time in ms"
    }

    Inspections {
        TEXT Id PK "GUID string (lowercase hyphenated)"
        TEXT CustomName "User-editable board identifier"
        TEXT TimestampUtc "ISO 8601 UTC timestamp"
        INTEGER DetectionMode "0: SingleFile, 1: BatchFolder, 2: LiveCamera"
        INTEGER Status "0: Pass, 1: Fail"
        INTEGER DefectCount "Cached defect count (0 = Pass)"
        REAL InferenceTimeMs "Inference latency in milliseconds"
        TEXT DeviceUsed "Execution device (CPU / GPU - DirectML)"
        TEXT ImagePath "Path to stored PCB image"
        TEXT CreatedAtUtc "Creation ISO 8601 UTC timestamp"
        TEXT UpdatedAtUtc "Last update ISO 8601 UTC timestamp"
    }

    Defects {
        TEXT Id PK "GUID string"
        TEXT InspectionId FK "References Inspections(Id)"
        TEXT ClassCode "Class code (SH, OP, MB, SP, SC, HB, CS, CFO, BMFO)"
        TEXT ClassName "Full defect name"
        REAL Confidence "Score in range [0.0, 1.0]"
        REAL BoxX "Normalized X coordinate [0.0, 1.0]"
        REAL BoxY "Normalized Y coordinate [0.0, 1.0]"
        REAL BoxWidth "Normalized width [0.0, 1.0]"
        REAL BoxHeight "Normalized height [0.0, 1.0]"
        INTEGER Severity "0: Low, 1: Medium, 2: High, 3: Critical"
        TEXT RootCause "Probable manufacturing cause"
        TEXT Impact "Operational/electrical impact"
        TEXT RecommendedAction "Remediation guidance"
    }

    AppSettings {
        TEXT Key PK "Configuration setting identifier"
        TEXT Value "Serialized setting value"
        TEXT UpdatedAtUtc "ISO 8601 UTC timestamp of last update"
    }

    Inspections ||--o{ Defects : "contains (CASCADE DELETE)"
```

---

## Physical Table DDL & Integrity Constraints

```sql
-- Schema Migration Tracking (Auditable, Idempotent, Tamper-Evident)
CREATE TABLE IF NOT EXISTS SchemaMigrations (
    Version INTEGER PRIMARY KEY,
    AppliedAtUtc TEXT NOT NULL,
    Description TEXT NOT NULL,
    Checksum TEXT NOT NULL,
    ExecutionDurationMs INTEGER NOT NULL
);

-- Inspections Table
CREATE TABLE IF NOT EXISTS Inspections (
    Id TEXT PRIMARY KEY,
    CustomName TEXT NOT NULL,
    TimestampUtc TEXT NOT NULL,
    DetectionMode INTEGER NOT NULL CHECK (DetectionMode IN (0, 1, 2)),
    Status INTEGER NOT NULL CHECK (Status IN (0, 1)),
    DefectCount INTEGER NOT NULL CHECK (DefectCount >= 0),
    InferenceTimeMs REAL NOT NULL CHECK (InferenceTimeMs >= 0.0),
    DeviceUsed TEXT NOT NULL,
    ImagePath TEXT NOT NULL,
    CreatedAtUtc TEXT NOT NULL,
    UpdatedAtUtc TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS IX_Inspections_TimestampUtc ON Inspections(TimestampUtc DESC);
CREATE INDEX IF NOT EXISTS IX_Inspections_Status ON Inspections(Status);
CREATE INDEX IF NOT EXISTS IX_Inspections_DetectionMode ON Inspections(DetectionMode);

-- Defects Table
CREATE TABLE IF NOT EXISTS Defects (
    Id TEXT PRIMARY KEY,
    InspectionId TEXT NOT NULL,
    ClassCode TEXT NOT NULL,
    ClassName TEXT NOT NULL,
    Confidence REAL NOT NULL CHECK (Confidence >= 0.0 AND Confidence <= 1.0),
    BoxX REAL NOT NULL CHECK (BoxX >= 0.0 AND BoxX <= 1.0),
    BoxY REAL NOT NULL CHECK (BoxY >= 0.0 AND BoxY <= 1.0),
    BoxWidth REAL NOT NULL CHECK (BoxWidth >= 0.0 AND BoxWidth <= 1.0),
    BoxHeight REAL NOT NULL CHECK (BoxHeight >= 0.0 AND BoxHeight <= 1.0),
    Severity INTEGER NOT NULL CHECK (Severity IN (0, 1, 2, 3)),
    RootCause TEXT,
    Impact TEXT,
    RecommendedAction TEXT,
    FOREIGN KEY (InspectionId) REFERENCES Inspections(Id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS IX_Defects_InspectionId ON Defects(InspectionId);
CREATE INDEX IF NOT EXISTS IX_Defects_ClassCode ON Defects(ClassCode);
CREATE INDEX IF NOT EXISTS IX_Defects_Severity ON Defects(Severity);

-- Application Settings Table
CREATE TABLE IF NOT EXISTS AppSettings (
    Key TEXT PRIMARY KEY,
    Value TEXT NOT NULL,
    UpdatedAtUtc TEXT NOT NULL
);
```

---

## Domain Model to Database Mapping

| Domain Entity / Value Object | Domain Property | SQLite Table | SQLite Column | SQLite Type | Nullable | Validation / Invariant Rule |
|---|---|---|---|---|:---:|---|
| **`InspectionRecord`** | `Guid Id` | `Inspections` | `Id` | `TEXT` | No | `Guid.ToString("D")` |
| | `string CustomName` | `Inspections` | `CustomName` | `TEXT` | No | Non-empty board identifier |
| | `DateTime TimestampUtc` | `Inspections` | `TimestampUtc` | `TEXT` | No | ISO 8601 (`yyyy-MM-ddTHH:mm:ss.fffZ`) |
| | `DetectionMode Mode` | `Inspections` | `DetectionMode` | `INTEGER` | No | 0: SingleFile, 1: BatchFolder, 2: LiveCamera |
| | `InspectionStatus Status` | `Inspections` | `Status` | `INTEGER` | No | 0: Pass, 1: Fail (`Status == (DefectCount == 0 ? 0 : 1)`) |
| | `int DefectCount` | `Inspections` | `DefectCount` | `INTEGER` | No | `DefectCount >= 0` |
| | `double InferenceTimeMs` | `Inspections` | `InferenceTimeMs` | `REAL` | No | `InferenceTimeMs >= 0.0` |
| | `string DeviceUsed` | `Inspections` | `DeviceUsed` | `TEXT` | No | Non-empty execution provider string |
| | `string ImagePath` | `Inspections` | `ImagePath` | `TEXT` | No | File path to stored image |
| | `DateTime CreatedAtUtc` | `Inspections` | `CreatedAtUtc` | `TEXT` | No | ISO 8601 UTC timestamp |
| | `DateTime UpdatedAtUtc` | `Inspections` | `UpdatedAtUtc` | `TEXT` | No | ISO 8601 UTC timestamp |
| **`DetectedDefect`** | `Guid Id` | `Defects` | `Id` | `TEXT` | No | `Guid.ToString("D")` |
| | `Guid InspectionId` | `Defects` | `InspectionId` | `TEXT` | No | Foreign key with `ON DELETE CASCADE` |
| | `string ClassCode` | `Defects` | `ClassCode` | `TEXT` | No | Standardized code (SH, OP, MB, SP, SC, HB, CS, CFO, BMFO) |
| | `string ClassName` | `Defects` | `ClassName` | `TEXT` | No | Full descriptive class name |
| | `float Confidence` | `Defects` | `Confidence` | `REAL` | No | `0.0 <= Confidence <= 1.0` |
| | `BoundingBox.X` | `Defects` | `BoxX` | `REAL` | No | `0.0 <= BoxX <= 1.0` |
| | `BoundingBox.Y` | `Defects` | `BoxY` | `REAL` | No | `0.0 <= BoxY <= 1.0` |
| | `BoundingBox.Width` | `Defects` | `BoxWidth` | `REAL` | No | `0.0 <= BoxWidth <= 1.0` |
| | `BoundingBox.Height` | `Defects` | `BoxHeight` | `REAL` | No | `0.0 <= BoxHeight <= 1.0` |
| | `DefectSeverity Severity` | `Defects` | `Severity` | `INTEGER` | No | 0: Low, 1: Medium, 2: High, 3: Critical |
| | `string? RootCause` | `Defects` | `RootCause` | `TEXT` | Yes | Nullable diagnostic root cause |
| | `string? Impact` | `Defects` | `Impact` | `TEXT` | Yes | Nullable operational impact |
| | `string? RecommendedAction` | `Defects` | `RecommendedAction` | `TEXT` | Yes | Nullable corrective remediation |
| **`AppSettings`** | `string Key` | `AppSettings` | `Key` | `TEXT` | No | Unique setting name |
| | `string Value` | `AppSettings` | `Value` | `TEXT` | No | Serialized setting value |
| | `DateTime UpdatedAtUtc` | `AppSettings` | `UpdatedAtUtc` | `TEXT` | No | ISO 8601 UTC timestamp |

---

## Migration Architecture & Safety Plan

### 1. Bidirectional Migration Contracts (`Up` and `Down`)
Every database migration must provide both a forward application script (`.up.sql`) and a tested rollback script (`.down.sql`):
- `001_initial_schema.up.sql`: Creates `SchemaMigrations`, `Inspections`, `Defects`, and `AppSettings` tables and indices.
- `001_initial_schema.down.sql`: Drops created indices and tables in reverse dependency order.

### 2. Transactional Isolation & Checksum Integrity
- **Transactional Execution:** Each migration script executes inside an explicit transaction (`BEGIN TRANSACTION ... COMMIT`). If any statement fails, the transaction is rolled back immediately, leaving the database unmodified.
- **Checksum Verification:** The SHA-256 hash of each applied script is verified against the recorded checksum in `SchemaMigrations`. If an existing script has been modified post-deployment, the runner halts and raises `DatabaseMigrationException` to prevent inconsistent schema drift.
- **Pre-Migration Snapshot:** Before applying pending migrations, `SqliteDatabaseBackupService` automatically generates a pre-migration backup (`app-pre-migrate-v{N}.db`), ensuring an immediate restore path if hardware failure or process interruption occurs.

### 3. Expand / Contract Rules for Future Schema Migrations
To ensure non-breaking forward compatibility across application upgrades:
1. **Additive Phase (Expand):**
   - New columns must be created as `NULL` or with explicit default values.
   - New tables and indices can be created freely without affecting existing query paths.
2. **Backfill Phase (Migrate):**
   - Data transformations and backfills must execute in chunked batches off hot transaction paths.
3. **Destructive Phase (Contract):**
   - Column removals and table drops must be isolated into a dedicated subsequent release *after* all application code has ceased querying the deprecated fields.
