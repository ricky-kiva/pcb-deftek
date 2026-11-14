Feature: Defect Statistics Dashboard
  As a plant quality manager
  I want to inspect dashboard metrics and configure its presentation
  So that production trends and defect knowledge are available in one place

  Scenario: Dashboard totals and class filter compose
    Given an isolated dashboard database
    And dashboard inspections across dates and classes
    When the dashboard loads
    Then the dashboard should report 3 inspections and 2 defects
    And the dashboard should use daily chart buckets
    When I filter the dashboard to short defects
    Then the dashboard should report 1 inspection and 1 defect

  Scenario: Layout and card order persist across dashboard reloads
    Given an isolated dashboard database
    And dashboard inspections across dates and classes
    When I move the second dashboard card left and choose one column
    And I reload the dashboard
    Then the dashboard should use one column
    And the first dashboard card should be defects detected

  Scenario: All nine defect knowledge assets resolve
    Given an isolated dashboard database
    Then the dashboard should expose nine defect knowledge items
    And every defect knowledge image should resolve from packaged resources

  Scenario: Chart modal opens and closes
    Given an isolated dashboard database
    And dashboard inspections across dates and classes
    When the dashboard loads
    And I activate the first dashboard chart
    Then the enlarged dashboard chart should be open
    When I close the enlarged dashboard chart
    Then the enlarged dashboard chart should be closed

  Scenario: Notification progress states are observable
    Given an isolated dashboard database
    When I open dashboard notifications
    Then the dashboard notifications should be visible
    When dashboard inference and export progress report running
    Then dashboard inference progress should be running at 40 percent
    And dashboard export progress should be running at 25 percent
    When dashboard progress is reset
    Then dashboard progress should be idle at zero

  Scenario: Startup preference persists
    Given an isolated dashboard database
    When I enable skip dashboard on startup
    Then the persisted startup preference should be enabled
    When I reload the dashboard
    Then skip dashboard on startup should be enabled

  Scenario: Empty class filter produces a no-match state
    Given an isolated dashboard database
    And a dashboard database with only an open defect
    When the dashboard loads
    And I filter the dashboard to short defects
    Then the dashboard should have no matching inspections
