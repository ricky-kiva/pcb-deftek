Feature: Live Camera Defect Inspection
  As an assembly line technician
  I want a real-time camera viewport with defect overlays, telemetry, and one-touch capture
  So that defective boards are identified instantly and evidence is persisted to inspection history

  Scenario: Live camera stream displays defect overlays and PASS/FAIL classification
    Given a live camera inspection environment with two connected cameras and a test SQLite database
    When the operator opens the Live Detection page
    And the live camera delivers a clean frame with no defects
    Then the inspection status badge should read "PASS"
    And the defect counter should read "0 Defects"
    When the live camera delivers a frame containing 2 defects
    Then the viewport should display the annotated camera frame
    And the inspection status badge should read "FAIL"
    And the defect counter should read "2 Defects"
    And the annotation service should have rendered 2 defect overlays

  Scenario: Telemetry toggle displays inference latency and FPS meters
    Given a live camera inspection environment with two connected cameras and a test SQLite database
    When the operator opens the Live Detection page
    And the live camera delivers a frame containing 1 defect
    Then the telemetry readout should report a non-negative inference latency and a positive frame rate
    And the telemetry card should be visible
    When the operator disables telemetry
    Then the telemetry card should be hidden
    When the operator enables telemetry
    Then the telemetry card should be visible

  Scenario: Stationary cursor auto-hides configuration controls while preserving inspection status
    Given a live camera inspection environment with two connected cameras and a test SQLite database
    When the operator opens the Live Detection page
    And the live camera delivers a frame containing 1 defect
    And the cursor remains stationary over the viewport for three seconds
    Then the configuration controls should be hidden
    And the inspection status card should remain visible
    And the telemetry card should remain visible
    When the operator moves the mouse
    Then all floating controls should be visible again

  Scenario: Capture saves a snapshot and persists the inspection record
    Given a live camera inspection environment with two connected cameras and a test SQLite database
    When the operator opens the Live Detection page
    And the live camera delivers a frame containing 2 defects
    And the operator presses Space to capture the frame
    Then a live camera inspection record with 2 defects should be persisted in the database
    And the raw and annotated snapshot images should exist on disk
    And a capture toast notification should be dispatched

  Scenario: Switching camera source reconnects the live capture device
    Given a live camera inspection environment with two connected cameras and a test SQLite database
    When the operator opens the Live Detection page
    Then camera index 0 should be actively capturing
    When the operator selects the second camera source
    Then camera index 1 should be actively capturing
    And the status should report streaming from "Integrated Webcam"
