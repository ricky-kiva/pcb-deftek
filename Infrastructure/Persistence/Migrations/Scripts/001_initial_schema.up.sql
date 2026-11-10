-- 001_initial_schema.up.sql
-- Streamlined schema for pcb-deftek Phase 2

CREATE TABLE IF NOT EXISTS SchemaMigrations (
    Version INTEGER PRIMARY KEY,
    AppliedAtUtc TEXT NOT NULL,
    Description TEXT NOT NULL,
    Checksum TEXT NOT NULL,
    ExecutionDurationMs INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS Inspections (
    Id TEXT PRIMARY KEY,
    Name TEXT NOT NULL,
    CreatedAtUtc TEXT NOT NULL,
    UpdatedAtUtc TEXT NOT NULL,
    DetectionMode INTEGER NOT NULL CHECK (DetectionMode IN (0, 1, 2)),
    DefectCount INTEGER NOT NULL DEFAULT 0 CHECK (DefectCount >= 0),
    ImagePath TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS IX_Inspections_CreatedAtUtc ON Inspections(CreatedAtUtc DESC);
CREATE INDEX IF NOT EXISTS IX_Inspections_DetectionMode ON Inspections(DetectionMode);

CREATE TABLE IF NOT EXISTS Defects (
    Id TEXT PRIMARY KEY,
    InspectionId TEXT NOT NULL,
    ClassCode TEXT NOT NULL,
    Confidence REAL NOT NULL CHECK (Confidence >= 0.0 AND Confidence <= 1.0),
    BoxX REAL NOT NULL CHECK (BoxX >= 0.0 AND BoxX <= 1.0),
    BoxY REAL NOT NULL CHECK (BoxY >= 0.0 AND BoxY <= 1.0),
    BoxWidth REAL NOT NULL CHECK (BoxWidth >= 0.0 AND BoxWidth <= 1.0),
    BoxHeight REAL NOT NULL CHECK (BoxHeight >= 0.0 AND BoxHeight <= 1.0),
    FOREIGN KEY (InspectionId) REFERENCES Inspections(Id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS IX_Defects_InspectionId ON Defects(InspectionId);
CREATE INDEX IF NOT EXISTS IX_Defects_ClassCode ON Defects(ClassCode);

CREATE TABLE IF NOT EXISTS AppSettings (
    Key TEXT PRIMARY KEY,
    Value TEXT NOT NULL,
    UpdatedAtUtc TEXT NOT NULL
);
