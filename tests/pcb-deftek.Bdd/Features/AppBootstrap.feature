Feature: Application Bootstrap
  As a quality technician
  I want the PCB defect detection application to initialize its core services
  So that the application starts reliably with dependency injection and logging configured

  Scenario: Bootstrap service container and verify core services
    Given a new service collection for application startup
    When the application dependencies are configured
    Then the logging service should be resolvable
    And the MainWindow should be registered as a singleton
