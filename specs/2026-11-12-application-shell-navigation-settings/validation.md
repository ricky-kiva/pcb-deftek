# Validation Criteria: Phase 4 — Application Shell, Navigation & Settings

## 1. Build & Quality Gates
- **Command:** `dotnet build pcb-deftek.csproj`
- **Pass Condition:** Exit code 0, 0 compiler errors, 0 compiler warnings.
- **Nullable Reference Types:** Nullable strictly enabled across all new ViewModels, services, and interfaces with zero warnings.
- **Dependency Audit:** Any new third-party NuGet package (`CommunityToolkit.Mvvm`, `Microsoft.Toolkit.Uwp.Notifications`) verified via Socket CLI (`socket`).

---

## 2. Automated Test Suite Gates
- **Command:** `dotnet test pcb-deftek.slnx`
- **Pass Condition:**
  - 100% tests passing, 0 failed, 0 skipped.
  - All existing tests (Persistence, Vision Preprocessing, ONNX Model Runner, Postprocessing, and Inference Pipeline) continue to pass without regression.
  - All new unit tests in `pcb-deftek.Tests` and BDD acceptance scenarios in `pcb-deftek.Bdd` pass cleanly.

---

## 3. Functional Acceptance Criteria

### 3.1. Visual Foundation & WPF Native Fluent Theme
- **Theme Activation:** `App.xaml` activates WPF's built-in Fluent Theme via `ThemeMode="System"` and `pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml`.
- **Mica Backdrop:** Main window activates native Windows 11 Mica window backdrop (`DWMSBT_MAINWINDOW`).
- **Theme Responsiveness:** Application controls automatically respond to Windows system light/dark mode transitions and adhere to system accent colors.
- **Resource Layering:** Domain resource dictionaries (`StatusColors.xaml`, `Icons.xaml`, `Controls.xaml`) layer over Fluent theme keys without collision or runtime parse exceptions.

### 3.2. Defect Class Abbreviations & Metadata
- **Completeness:** All 9 defect classes provide standard abbreviations (`SH`, `OP`, `MB`, `SP`, `SC`, `HB`, `CS`, `CFO`, `BMFO`) in `DefectKnowledgeBase`.
- **Lookups:** `DefectKnowledgeBase.GetByAbbreviation` and `TryGetByAbbreviation` resolve the correct `DefectClassInfo` record.

### 3.3. Application Shell & Sidebar Layout
- **Sidebar Ordering:** Left sidebar sheet displays navigation items in the strict specified order:
  1. Dashboard
  2. Live Detection
  3. File-Based Detection
  4. History
  5. Settings (anchored at the bottom)
- **Collapsible Behavior:** Sidebar expands and collapses upon user toggle while maintaining access to all navigation triggers.
- **Active Selection Indicator:** The active navigation route is clearly indicated visually.

### 3.4. Compact Footer & External Hyperlinks
- **Layout:** Footer renders horizontally with minimal vertical padding, separated by dot (`·`) delimiters.
- **Attribution:**
  - Social media icons for GitHub, LinkedIn, and Instagram.
  - Buy Me a Coffee link pointing to `https://ko-fi.com/rickyslash`.
  - "Made with ❤️ by Rickyslash" pointing to `https://rickyslash.my.id`.
- **Safe Browser Launch:** Clicking external links triggers the default web browser safely using `UseShellExecute = true` without crashing the application.

### 3.5. Navigation & Startup Routing
- **Startup Logic:**
  - When `"Skip dashboard on startup"` is `true`, application launches directly into `LiveDetectionViewModel`.
  - When `false`, application launches into the configured `DefaultStartupPage` (defaulting to `DashboardViewModel`).
- **Dynamic Content:** Clicking any sidebar item updates `NavigationService.CurrentViewModel`, rendering the corresponding view via XAML `DataTemplate`.

### 3.6. Database Export & Notification Service
- **Export Integrity:** `IDatabaseExportService.ExportDatabaseAsync()` copies `app.db` to the destination directory with timestamped naming (`pcb-deftek-export-{yyyyMMdd-HHmmss}.db`).
- **Destination Default:** Defaults to `%USERPROFILE%\Downloads` if no path is specified.
- **Thread Safety:** Export executes asynchronously off the UI thread.
- **Completion Toast:** Sends a Windows desktop toast notification upon export completion.
- **Headless Safety:** In test runners or environments without interactive desktop shells, notification calls log diagnostics without throwing unhandled exceptions.

### 3.7. Settings Modal & Persistence
- **Controls & Sections:**
  - Inference & Model Controls: Confidence threshold slider/input ($0.00 \dots 1.00$) and dynamic device selector (`Cpu`, `GpuDirectML`).
  - Application Preferences: Default startup page selector and "Skip dashboard on startup" checkbox.
  - About & Attribution: Links to model (`pcb-defect-yolov8m-dspcbsd`), model author (`Janani-V`), developer (`Rickyslash`), and Ko-fi.
  - Export: Report and database export directory selectors, plus a manual "Export Database" button.
- **Persistence:** Settings changes save to SQLite through `ISettingsRepository` and persist across application restarts.

---

## 4. Architecture & Dependency Compliance
- **MVVM Purity:** ViewModels contain zero references to `System.Windows.Controls` or WPF UI elements.
- **Dependency Injection:** All services (`INavigationService`, `IDatabaseExportService`, `INotificationService`) and ViewModels are registered in `App.ConfigureServices` and resolvable via `IServiceProvider`.
- **Zero UI Thread Blocking:** File I/O, database export, and settings operations run asynchronously with cancellation support.
- **Exception Handling:** Database export failures and navigation errors are caught and surfaced via custom exceptions (`ExportException`, `PcbDeftekException`).
