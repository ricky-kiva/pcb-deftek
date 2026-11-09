Feature: Application Bootstrap
  As an inspection operator
  I want the PCB defect detection application to configure its dependencies on startup
  So that logging, windows, and core application services are ready for defect analysis

  Scenario: Application container successfully resolves logging infrastructure
    Given a new service collection for application startup
    When the application dependencies are configured
    Then the logging service should be resolvable

  Scenario: Application container registers MainWindow as a singleton
    Given a new service collection for application startup
    When the application dependencies are configured
    Then the MainWindow should be registered as a singleton
