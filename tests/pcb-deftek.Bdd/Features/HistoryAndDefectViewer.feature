Feature: History and Interactive Defect Viewer
  As a quality engineer
  I want to browse, inspect, crop, rename, and delete historical inspections
  So that I can review defects and manage stored evidence safely

  Scenario: Gallery groups records by local date and shows PASS/FAIL badges
    Given an isolated history database
    And the history contains a passing inspection today and a failing inspection yesterday
    When the history page loads
    Then the gallery should contain 2 date groups
    And the cards should report one pass and one fail

  Scenario: Rename persists and survives a reload
    Given an isolated history database
    And a history record named "rename-board"
    When I rename the record to "renamed-board" and reload
    Then the history card name should be "renamed-board"

  Scenario: Viewer default sidebar shows record metadata and defect classes
    Given an isolated history database
    And a history record "viewer-board" with "short" and "open" defects
    When I open the viewer for "viewer-board"
    Then the viewer default sidebar should show the record name and timestamp
    And the viewer should show 2 defect class summaries

  Scenario: Focusing a marker exposes its diagnostics
    Given an isolated history database
    And a history record "focus-board" with a "short" defect
    When I open the viewer for "focus-board"
    And I focus the first defect marker
    Then the focused sidebar should show the short diagnostic fields

  Scenario: Crop writes a PNG matching the visible region and adds it to history
    Given an isolated history database
    And a 200x100 inspection image named "crop-board"
    When I open the viewer for "crop-board"
    And I crop the normalized region 0.25, 0.25, 0.5, 0.5
    Then the crop PNG should be 100x50
    And the gallery should contain the cropped inspection

  Scenario: Missing source image shows the unavailable state
    Given an isolated history database
    And an inspection "missing-board" whose image file is missing
    When I open the viewer for "missing-board"
    Then the viewer should show the unavailable state

  Scenario: Date, status, and defect-class filters compose
    Given an isolated history database
    And a mixed history for filtering
    When I filter to today, FAIL, and short defects
    Then only the failing short record should remain

  Scenario: Confirmed delete removes app-owned files and keeps external originals
    Given an isolated history database
    And a live capture "capture-board" and an external import "external-board"
    When I select both records and confirm deletion
    Then the live capture files and both rows should be deleted
    And the external original should remain on disk

  Scenario: Cancelled delete changes nothing
    Given an isolated history database
    And a live capture "cancel-board" selected for deletion
    When I cancel deletion
    Then the live capture row and files should remain
    And the card should remain selected

  Scenario: Sidebar Delete Image closes the viewer and refreshes the gallery
    Given an isolated history database
    And an open viewer for a deletable crop image
    When I confirm deletion from the viewer sidebar
    Then the viewer should be closed
    And the gallery should no longer contain the deleted crop
