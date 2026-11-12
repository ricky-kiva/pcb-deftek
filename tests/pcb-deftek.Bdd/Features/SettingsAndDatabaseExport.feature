Feature: Settings Management and Database Export
  As a quality engineer or supervisor
  I want to adjust inference parameters, configure export paths, and export database snapshots
  So that application preferences persist reliably and data audits are easily captured

  Scenario: Adjust inference confidence threshold and verify persistence
    Given a configured settings service connected to a test SQLite database
    When the operator adjusts the confidence threshold to 0.45
    And the operator saves the updated settings
    Then the persisted confidence threshold in the database should be 0.45

  Scenario: Trigger database export and verify snapshot creation with toast notification
    Given a configured settings service connected to a test SQLite database
    And a custom destination export folder
    When the operator triggers an immediate database export
    Then a valid SQLite export database file should be created in the destination folder
    And a desktop toast notification should be dispatched informing the user
