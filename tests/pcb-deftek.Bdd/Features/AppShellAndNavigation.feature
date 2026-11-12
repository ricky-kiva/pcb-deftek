Feature: Application Shell and Navigation
  As an inspection operator
  I want an intuitive sidebar navigation and startup routing system
  So that I can quickly access inspection workflows and land on my preferred workspace

  Scenario: Launch application with default settings lands on Dashboard view
    Given the application dependencies are configured for shell navigation
    And the user has not enabled skip dashboard on startup
    When the application executes startup routing
    Then the active workspace view model should be Dashboard

  Scenario: Launch application with skip dashboard preference enabled lands on Live Detection view
    Given the application dependencies are configured for shell navigation
    And the user has enabled skip dashboard on startup
    When the application executes startup routing
    Then the active workspace view model should be Live Detection

  Scenario: User navigates through primary sidebar navigation routes
    Given the application dependencies are configured for shell navigation
    And the application shell is initialized
    When the operator navigates to "File-Based Detection"
    Then the active workspace view model should be File-Based Detection
    When the operator navigates to "History"
    Then the active workspace view model should be History
    When the operator navigates to "Live Detection"
    Then the active workspace view model should be Live Detection
    When the operator navigates to "Dashboard"
    Then the active workspace view model should be Dashboard
