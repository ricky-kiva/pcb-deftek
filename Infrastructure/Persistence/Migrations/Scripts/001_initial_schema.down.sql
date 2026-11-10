-- 001_initial_schema.down.sql
-- Rollback script for initial schema

DROP TABLE IF EXISTS AppSettings;
DROP INDEX IF EXISTS IX_Defects_ClassCode;
DROP INDEX IF EXISTS IX_Defects_InspectionId;
DROP TABLE IF EXISTS Defects;
DROP INDEX IF EXISTS IX_Inspections_DetectionMode;
DROP INDEX IF EXISTS IX_Inspections_CreatedAtUtc;
DROP TABLE IF EXISTS Inspections;
DROP TABLE IF EXISTS SchemaMigrations;
