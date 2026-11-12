# Implementation Plan: Phase 4 — Application Shell, Navigation & Settings

## Task Breakdown

### Task Group 1: Core UI Dependencies, Vector Icons, Resource Styles & Defect Abbreviations
- **Task 1.1:** Add `CommunityToolkit.Mvvm` and `Microsoft.Toolkit.Uwp.Notifications` NuGet dependencies:
  - Verify package safety via Socket CLI (`socket`).
  - Add `<PackageReference>` entries in `pcb-deftek.csproj`.
  - *Acceptance:* Solution builds cleanly with zero compiler warnings and nullable enabled.
  - *Verify:* `dotnet build pcb-deftek.csproj`
  - *Scope:* S (1 file)
- **Task 1.2:** Enhance `DefectClassInfo` and `DefectKnowledgeBase` with standardized abbreviations:
  - Add `string Abbreviation` property to `DefectClassInfo` record.
  - Update all 9 defect definitions in `DefectKnowledgeBase` (`SH`, `OP`, `MB`, `SP`, `SC`, `HB`, `CS`, `CFO`, `BMFO`).
  - Add lookup helpers `GetByAbbreviation(string abbreviation)` and `TryGetByAbbreviation(...)`.
  - *Acceptance:* Every defect class exposes its official abbreviation and severity level.
  - *Verify:* Unit tests in `tests/pcb-deftek.Tests/DefectKnowledgeBaseTests.cs` verifying abbreviation lookups and completeness.
  - *Scope:* S (2 files)
- **Task 1.3:** Configure WPF's native Fluent Theme and build domain resource dictionaries under `Presentation/Styles/`:
  - Adopt WPF's built-in Fluent Theme as the visual foundation by enabling `ThemeMode="System"` on `Application` in `App.xaml` and merging `pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml`.
  - Verify native Windows 11 Mica backdrop (`DWMSBT_MAINWINDOW`), light/dark theme synchronization, and built-in Fluent control styles (buttons, sliders, comboboxes, scrollbars).
  - Create `StatusColors.xaml`: Inspection domain brushes (PASS green, FAIL red, severity grade colors: Critical, High, Medium-High, Medium, Low-Medium) composed with Fluent accent brushes.
  - Create `Icons.xaml`: Vector geometries for sidebar navigation (Dashboard, Camera, Folder, History, Settings), footer icons (GitHub, LinkedIn, Instagram, Ko-fi, Heart), and window chrome.
  - Create `Controls.xaml`: Custom layout styles (sidebar drawer, floating HUD cards, compact footer) consuming native Fluent theme keys.
  - Merge custom dictionaries into `App.xaml` layered over `Fluent.xaml`.
  - *Acceptance:* Application renders using WPF's native Fluent Theme with system theme tracking, Mica backdrop, and accessible domain brushes/icons.
  - *Verify:* `dotnet build` succeeds; application launches displaying Fluent styled window and controls with zero XAML parse warnings.
  - *Scope:* M (4-5 files)

#### Checkpoint 1: UI Foundation, Styles & Domain Metadata Validation
- [x] `CommunityToolkit.Mvvm` and toast notification packages installed and verified.
- [x] All 9 defect classes provide valid abbreviations (`SH` through `BMFO`) in `DefectKnowledgeBase`.
- [x] WPF's native Fluent Theme (`ThemeMode="System"`) configured with Mica backdrop and layered domain resource dictionaries.
---

### Task Group 2: Database Export Service & Notification Infrastructure
- **Task 2.1:** Implement `IDatabaseExportService` and `SqliteDatabaseExportService` under `Infrastructure/Persistence/DatabaseExport/`:
  - Create `IDatabaseExportService` interface with `Task<string> ExportDatabaseAsync(string destinationDirectory, CancellationToken cancellationToken = default)`.
  - Implement SQLite database export using safe checkpointing and timestamped filename format: `pcb-deftek-export-{yyyyMMdd-HHmmss}.db`.
  - Default export directory to `%USERPROFILE%\Downloads` if unspecified.
  - Wrap any file I/O or SQLite connection errors in `ExportException`.
  - *Acceptance:* Calling `ExportDatabaseAsync` creates a valid, non-corrupted SQLite database file in the target directory asynchronously.
  - *Verify:* Unit and integration tests verifying export generation, destination fallback, and cancellation handling.
  - *Scope:* M (2-3 files)
- **Task 2.2:** Implement `INotificationService` and `WindowsNotificationService` under `Infrastructure/Notifications/`:
  - Create `INotificationService` interface: `Task ShowToastAsync(string title, string message, CancellationToken cancellationToken = default)`.
  - Implement Windows toast notifications using `Microsoft.Toolkit.Uwp.Notifications`.
  - Add headless/test-environment fallback (logging rather than failing when running in non-desktop CI or test runners).
  - *Acceptance:* Toast notifications dispatch asynchronously without crashing tests or blocking the UI thread.
  - *Verify:* Unit tests with mockable notification provider.
  - *Scope:* S (2 files)

#### Checkpoint 2: Database Export & Notification Service Validation
- [x] Database export produces valid SQLite copies of `app.db` off the UI thread.
- [x] Destination path defaults to Windows Downloads folder.
- [x] Notifications trigger gracefully in both desktop and automated test environments.

---

### Task Group 3: Navigation Architecture, View-ViewModel DataTemplates & Startup Routing
- **Task 3.1:** Implement Navigation Service and Page ViewModel contracts under `Presentation/Navigation/` and `Presentation/ViewModels/`:
  - Create `INavigationService` and `NavigationService` tracking `CurrentViewModel`, navigation stack, and navigation events.
  - Create `ViewModelBase` extending `ObservableObject`.
  - Create lightweight placeholder Page ViewModels:
    - `DashboardViewModel`
    - `LiveDetectionViewModel`
    - `FileDetectionViewModel`
    - `HistoryViewModel`
  - Create matching placeholder Views under `Presentation/Views/`:
    - `DashboardView.xaml`
    - `LiveDetectionView.xaml`
    - `FileDetectionView.xaml`
    - `HistoryView.xaml`
  - Map ViewModels to Views via declarative `DataTemplate` entries in `App.xaml` or `MainWindow.xaml`.
  - *Acceptance:* Navigation service switches `CurrentViewModel`, and the host UI renders the matching View automatically.
  - *Verify:* Unit tests verifying navigation state changes and events.
  - *Scope:* M (5-6 files)
- **Task 3.2:** Build Shell Layout in `MainWindow.xaml` and `MainViewModel`:
  - Implement Collapsible Sidebar Sheet:
    - Collapsible state toggle button (hamburger icon).
    - Navigation buttons in strict order: 1. Dashboard, 2. Live Detection, 3. File-Based Detection, 4. History, 5. Settings (anchored at bottom).
    - Active view selection indicator.
  - Implement Compact Footer:
    - Horizontal layout with dot (`·`) separators.
    - External hyperlinks: GitHub, LinkedIn, Instagram, Buy Me a Coffee (`https://ko-fi.com/rickyslash`), and Made with ❤️ by Rickyslash (`https://rickyslash.my.id`).
    - Safe browser launcher command (`Process.Start` with `UseShellExecute = true`).
  - Implement Center Content Area:
    - `ContentControl` bound to `NavigationService.CurrentViewModel`.
  - *Acceptance:* Shell renders full layout, allows sidebar collapsing/expanding, opens footer links in browser, and hosts dynamic page content.
  - *Verify:* Visual verification via throwaway smoke test and unit tests verifying `MainViewModel` commands.
  - *Scope:* M (3 files)
- **Task 3.3:** Implement Startup Preference Routing:
  - On application initialization, query `ISettingsRepository` for:
    - `SkipDashboardOnStartup` (bool)
    - `DefaultStartupPage` (string)
  - If `SkipDashboardOnStartup` is true, route directly to `LiveDetectionViewModel`.
  - Otherwise, route to `DefaultStartupPage` (or default `DashboardViewModel`).
  - *Acceptance:* Launch behavior strictly honors stored user preferences.
  - *Verify:* Unit tests verifying initial navigation targets under various setting combinations.
  - *Scope:* S (2 files)

#### Checkpoint 3: Shell Layout & Navigation Routing Validation
- [ ] Sidebar displays navigation items in exact order (Dashboard, Live, File, History, Settings).
- [ ] Sidebar collapses and expands cleanly.
- [ ] Footer displays all links separated by `·` and safely launches URLs.
- [ ] Startup routing directs to Live Detection if skip-dashboard is enabled, or to default page.

---

### Task Group 4: Settings Dialog / Panel & Preference Persistence
- **Task 4.1:** Define Settings Domain Model and Persistence Adapter under `Domain/Models/` and `Infrastructure/Persistence/Repositories/`:
  - Create `UserSettings` model encapsulating:
    - `ConfidenceThreshold` (double, default 0.25)
    - `PreferredDevice` (string, default "Cpu")
    - `DefaultStartupPage` (string, default "Dashboard")
    - `SkipDashboardOnStartup` (bool, default false)
    - `ReportExportPath` (string, default Downloads folder)
    - `DatabaseExportPath` (string, default Downloads folder)
  - Implement settings loader and saver using `ISettingsRepository`.
  - Broadcast changes using `WeakReferenceMessenger` (e.g., `ConfidenceThresholdChangedMessage`, `DevicePreferenceChangedMessage`).
  - *Acceptance:* User settings round-trip to SQLite database and broadcast updates to subscribers.
  - *Verify:* Integration tests with SQLite in-memory/temp database.
  - *Scope:* S (2 files)
- **Task 4.2:** Implement `SettingsViewModel` and `SettingsView.xaml` under `Presentation/ViewModels/` and `Presentation/Views/`:
  - Section 1 (Inference & Model Controls):
    - Confidence threshold slider and two-way numeric input ($0.00 \dots 1.00$, step $0.05$).
    - Hardware device selector dropdown listing available devices (`Cpu`, `GpuDirectML`).
  - Section 2 (Application Preferences):
    - Default startup page selector dropdown.
    - "Skip dashboard on startup" checkbox.
  - Section 3 (About & Attribution):
    - Model info: `pcb-defect-yolov8m-dspcbsd` and developer `Janani-V` with hyperlinks.
    - Developer info: Rickyslash, GitHub (`ricky-kiva`), Instagram (`r.ckyslash`), LinkedIn (`ricky-fadel`), and Ko-fi button.
  - Section 4 (Export Settings):
    - Report export location picker.
    - Database export location picker.
    - Manual **Export Database** button: triggers asynchronous database export and shows Windows toast notification upon completion.
  - *Acceptance:* Settings dialog allows modifying and saving all settings, triggers database export, and displays attribution.
  - *Verify:* Unit tests verifying `SettingsViewModel` property updates, commands, and export triggering.
  - *Scope:* M (3 files)

#### Checkpoint 4: Settings Management & User Preferences Validation
- [ ] Settings modal opens from sidebar and allows adjusting inference thresholds, device preference, and export directories.
- [ ] "Export Database" button triggers asynchronous SQLite backup to the configured folder and raises a toast notification.
- [ ] Settings changes persist across application restarts.

---

### Task Group 5: Dependency Injection Wiring, Unit Tests & Reqnroll BDD Scenarios
- **Task 5.1:** DI Registration in `App.xaml.cs`:
  - Register `INavigationService`, `IDatabaseExportService`, `INotificationService`, `MainViewModel`, `SettingsViewModel`, and Page ViewModels in `App.ConfigureServices`.
  - Update `MainWindow` constructor or DataContext binding to use `MainViewModel`.
  - *Acceptance:* Application bootstraps all UI and infrastructure dependencies with zero resolution exceptions.
  - *Verify:* `DependencyInjectionTests.cs` verifying resolution of all new services.
  - *Scope:* S (2 files)
- **Task 5.2:** Implement Comprehensive Unit Tests in `tests/pcb-deftek.Tests/`:
  - `NavigationServiceTests.cs`: Navigation transitions, route events, and startup preference logic.
  - `DatabaseExportServiceTests.cs`: Successful export, invalid directory fallback, and cancellation handling.
  - `SettingsViewModelTests.cs`: Setting mutations, persistence triggers, device enumeration, and export command execution.
  - `DefectClassInfoTests.cs`: Abbreviation completeness and mapping assertions for all 9 classes.
  - *Acceptance:* 100% test pass rate with zero warnings.
  - *Verify:* `dotnet test tests/pcb-deftek.Tests/`
  - *Scope:* M (4 files)
- **Task 5.3:** Implement Reqnroll BDD Acceptance Scenarios in `tests/pcb-deftek.Bdd/`:
  - Create `Features/AppShellAndNavigation.feature`:
    - Scenario: Launch application with default settings lands on Dashboard view.
    - Scenario: Launch application with "Skip dashboard on startup" enabled lands on Live Detection view.
    - Scenario: Navigate through sidebar items updates the active view accordingly.
  - Create `Features/SettingsAndDatabaseExport.feature`:
    - Scenario: Adjust confidence threshold and verify persistence in database.
    - Scenario: Trigger database export and verify exported file exists and toast notification fires.
  - Implement step definitions in `StepDefinitions/AppShellStepDefinitions.cs` and `StepDefinitions/SettingsStepDefinitions.cs`.
  - *Acceptance:* All Reqnroll BDD feature scenarios execute and pass cleanly.
  - *Verify:* `dotnet test tests/pcb-deftek.Bdd/`
  - *Scope:* M (4 files)

#### Checkpoint 5: End-to-End Shell & Settings Acceptance Verification
- [ ] Solution compiles cleanly with zero warnings (`dotnet build`).
- [ ] All unit and BDD tests pass (`dotnet test`).
- [ ] Full application shell runs interactively without crashes.

---

## Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Windows Toast Notifications may fail or crash in headless CI / automated test runners | High | Abstract behind `INotificationService`; implement environment detection that logs fallback diagnostics instead of throwing platform exceptions in headless mode. |
| External hyperlinks opening with `Process.Start` could introduce shell injection or platform crashes | Med | Hardcode allowed HTTPS schemes, validate URI format before launch, and set `UseShellExecute = true` in an isolated helper. |
| Active SQLite database lock during database export | Med | Perform checkpoint/backup using safe SQLite backup APIs or file copy after ensuring open read-only access off the UI thread. |
| ViewModels directly referencing WPF controls violating MVVM purity | High | Enforce strict review: ViewModels manipulate only primitive types, models, and commands; views bind via XAML DataTemplates. |
