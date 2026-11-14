-- 002_add_inspection_origin.down.sql

ALTER TABLE Inspections
    DROP COLUMN Origin;
