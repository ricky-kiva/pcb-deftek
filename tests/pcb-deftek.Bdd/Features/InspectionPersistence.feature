Feature: Inspection Persistence
  As a quality technician
  I want to persist inspection results with defect details in the database
  So that historical board records can be retrieved, audited, and reviewed reliably

  Scenario: Persist a board inspection with detected defects and retrieve it
    Given a configured SQLite persistence layer for testing
    And an inspection session for board "PCB-QA-001" with a "short" defect
    When the inspection record is saved to the database
    Then the inspection record should be retrievable by its ID
    And the retrieved inspection should have 1 defect of class "short"
    And the inspection status should evaluate to Fail

  Scenario: Delete an inspection record and verify cascading defect removal
    Given a configured SQLite persistence layer for testing
    And an existing inspection for board "PCB-QA-002" with an "open" defect
    When the inspection record is deleted from the database
    Then the inspection should no longer exist in the database
    And all associated defects should be removed automatically
