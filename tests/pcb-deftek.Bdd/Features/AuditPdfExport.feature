Feature: Audit PDF Export
  As a QA engineer
  I want to export the filtered dashboard as an audit-ready PDF
  So that inspection evidence and defect metadata can be shared and reviewed

  Scenario: Filtered export writes a PDF and notifies the operator
    Given an isolated export database
    And the report export path is configured
    And an exportable inspection "short-board" with a "short" defect
    And an exportable inspection "open-board" with an "open" defect
    When I filter to short defects and export
    Then a report PDF should exist in the configured export directory
    And the report notification should have fired
    And the export should complete at 100 percent

  Scenario: Blank report path resolves under the default report tree
    Given an isolated export database
    And the report export path is blank
    And an exportable inspection "default-path-board" with a "short" defect
    When I export the dashboard report
    Then a report PDF should exist in the default report directory
    And the report PDF should start with the PDF signature

  Scenario: No matching inspections produces a notification and no report
    Given an isolated export database
    And the report export path is configured
    And no exportable inspections
    When I export the dashboard report
    Then no report PDF should be written
    And the no-match notification should have fired
    And the background activity modal should remain closed

  Scenario: Declining a large export writes nothing
    Given an isolated export database
    And the report export path is configured
    And 101 exportable inspections
    When I export and decline the large-report confirmation
    Then the confirmation should mention 101 inspections
    And no report PDF should be written
    And the background activity modal should remain closed

  Scenario: Cancelling from the Background Activity modal removes the report
    Given an isolated export database
    And the report export path is configured
    And an exportable inspection "cancel-board" with a "short" defect
    When I start an export and cancel it from the Background Activity modal
    Then the data export should be cancelled
    And no report PDF should be written
    And no export staging directory should remain

  Scenario: Unreadable evidence is deleted while the report still succeeds
    Given an isolated export database
    And the report export path is configured
    And an exportable inspection "readable-board" with a "short" defect
    And an inspection "broken-board" whose image is missing
    When I export the dashboard report
    Then a report PDF should exist in the configured export directory
    And the broken inspection should be deleted
    And the readable inspection should remain

  Scenario: Missing History image is removed during gallery hydration
    Given an isolated history database
    And an inspection "missing-history-board" whose image file is missing
    When the history page loads
    Then the missing record should be removed from history

  Scenario: Failed export notifies and writes nothing
    Given an isolated export database
    And the report export path points at a file
    And an exportable inspection "blocked-board" with a "short" defect
    When I export the dashboard report
    Then the data export should have failed
    And the report failure notification should have fired
    And no report PDF should be written
    And no export staging directory should remain
