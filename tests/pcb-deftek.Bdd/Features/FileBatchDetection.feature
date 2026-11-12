Feature: File and Batch Defect Inspection
  As an assembly line quality inspector
  I want to ingest single PCB images or entire folders and execute batch defect detection
  So that boards are automatically inspected, progress and ETA are tracked, and results are persisted to the database

  Scenario: Ingest and inspect a single PCB image file
    Given a configured file detection environment connected to a test SQLite database
    And a single sample PCB image "SH-1.jpg" queued for inspection
    When the operator starts batch inspection
    Then the inspection should complete successfully
    And exactly 1 board record should be persisted in the database with mode SingleFile
    And the inspected board "SH-1" should contain detected defects

  Scenario: Ingest multiple PCB images and verify progress with inline ETA
    Given a configured file detection environment connected to a test SQLite database
    And multiple sample PCB images queued for inspection
    When the operator starts batch inspection
    Then all queued boards should be inspected
    And the batch completion percentage should reach 100 percent
    And the batch progress report should have calculated an estimated completion time
    And all inspected records should be persisted with mode BatchFolder

  Scenario: Cancel active batch inspection mid-flight
    Given a configured file detection environment connected to a test SQLite database
    And a large batch of PCB images queued for inspection
    When the operator cancels processing while the batch is running
    Then the batch execution status should transition to Cancelled
    And remaining queued images should not be inspected
    And previously processed inspection records should remain safely committed in the database

  Scenario: Dispatch completion toast notification when batch finishes
    Given a configured file detection environment connected to a test SQLite database
    And multiple sample PCB images queued for inspection
    When the operator starts batch inspection
    Then a desktop toast notification with title "Batch Inspection Complete" should be dispatched
    And the notification message should state the total processed boards and pass fail counts
