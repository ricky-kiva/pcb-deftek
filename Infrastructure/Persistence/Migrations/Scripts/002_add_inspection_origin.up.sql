-- 002_add_inspection_origin.up.sql
-- Distinguish app-generated images from user-owned imports so deletion can
-- remove only app-owned crops and live captures.

ALTER TABLE Inspections
    ADD COLUMN Origin INTEGER NOT NULL DEFAULT 0;
