# Requirements & Context: Phase 4 — Application Shell, Navigation & Settings

## 1. Executive Summary & Context
Phase 4 implements the application presentation shell, primary navigation architecture, user settings management, and database export service for `pcb-deftek`. It transforms the blank WPF host window into a modern, responsive desktop interface adhering to `UI-INITIAL.md`, `specs/mission.md`, and `specs/tech-stack.md`. 

This phase establishes the foundational UI framework upon which subsequent detection workflows (Batch File Detection in Phase 5, Live Camera Inspection in Phase 6, History & Viewer in Phase 7, and Dashboard Analytics in Phase 8) will be hosted. Additionally, it fulfills requirements moved from Phase 2, specifically the automated SQLite database export service and standardized defect class abbreviations for presentation contracts.

---

## 2. Architectural Decisions & Tech Stack

1. **MVVM Framework (`CommunityToolkit.Mvvm`):**
   - Use `ObservableObject`, `RelayCommand`, and `AsyncRelayCommand` for clean property change notifications and decoupled commanding.
   - Use `WeakReferenceMessenger` for loosely coupled event broadcasting across ViewModels (e.g., settings persistence events, navigation requests).
   - Ensure strict MVVM purity: ViewModels must have zero dependencies on `System.Windows.Controls` or UI elements.

2. **View Navigation & Routing:**
   - Implement an interface-driven navigation service (`INavigationService`) managed via Dependency Injection.
   - Bind the active view in `MainWindow.xaml` via `ContentControl` with declarative `DataTemplate` mapping matching each ViewModel type to its corresponding UserControl View.
   - Provide lightweight placeholder views for downstream phases (`DashboardView`, `LiveDetectionView`, `FileDetectionView`, `HistoryView`) so navigation is fully operational and verifiable.

3. **Desktop Toast Notifications:**
   - Integrate native Windows toast notifications (via `Microsoft.Toolkit.Uwp.Notifications` or Windows notification APIs) to notify users when asynchronous background tasks (such as database export) complete.
   - Abstract notification dispatch behind `INotificationService` with a testable implementation that avoids crashing in headless test environments.

4. **External Hyperlink Safety:**
   - External URLs in the footer and About section must open safely in the user's default browser using `ProcessStartInfo` with `UseShellExecute = true`, guarded against shell injection and platform exceptions.

5. **Asynchronous Database Export Service:**
   - Implement `IDatabaseExportService` to safely copy the active SQLite database (`%LOCALAPPDATA%\PcbDeftek\pcb-deftek\app.db`) to a user-configured destination path (defaulting to the Windows Downloads folder `%USERPROFILE%\Downloads`).
   - Run file I/O off the UI thread with proper database locking/checkpointing, throwing `ExportException` upon failure.

6. **WPF Native Fluent Theme (`PresentationFramework.Fluent`):**
   - Adopt WPF's new built-in Fluent Theme (.NET 9/.NET 10) as the visual and UI foundation for all windows and controls.
   - Enable `ThemeMode="System"` and merge `pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml` in `App.xaml` to provide modern Windows 11 aesthetics, native Mica window backdrop (`DWMSBT_MAINWINDOW`), system light/dark theme adaptation, and native Fluent control templates.
   - Layer application-specific resources (inspection status brushes, vector icons, custom layout templates) on top of Fluent theme resource keys.

---

## 3. In-Scope Deliverables

### 3.1. Dependencies & Foundation
- Inspect and add `CommunityToolkit.Mvvm` and `Microsoft.Toolkit.Uwp.Notifications` via Socket CLI (`socket`).
- Configure WPF's native Fluent Theme in `App.xaml` (`ThemeMode="System"` and `PresentationFramework.Fluent` theme resource dictionary).
- Create modular WPF resource dictionaries under `Presentation/Styles/` for inspection status colors (PASS/FAIL/severity grades), vector icons, and custom layout controls.
- Extend `DefectClassInfo` and domain knowledge base to expose official 2–4 letter abbreviations (`SH`, `OP`, `MB`, `SP`, `SC`, `HB`, `CS`, `CFO`, `BMFO`) and severity levels.
### 3.2. Application Shell & Layout (`MainWindow.xaml`)
- **Collapsible Sidebar Sheet (Left):**
  - Collapsible drawer with smooth expand/collapse toggle.
  - Navigation items in strict order:
    1. **Dashboard**
    2. **Live Detection**
    3. **File-Based Detection**
    4. **History**
    5. **Settings** (anchored at the bottom of the sidebar).
  - Selected item visual indicator and hover feedback.
- **Compact Footer (Bottom):**
  - Minimal vertical padding.
  - Centered/aligned horizontal layout with items separated by a dot (`·`):
    1. Social media icons: GitHub, LinkedIn, Instagram.
    2. Buy Me a Coffee: Coffee icon linking to `https://ko-fi.com/rickyslash`.
    3. Attribution: "Made with ❤️ by Rickyslash" linking to `https://rickyslash.my.id`.
- **Content Area (Center):**
  - Dynamic viewport hosting the current active view via `DataTemplate` resolution.

### 3.3. Navigation & Startup Preference Routing
- `INavigationService` managing current view model and navigation commands.
- Startup navigation routing based on persisted user configuration:
  - If `"Skip dashboard on startup"` is checked, route directly to `LiveDetectionViewModel`.
  - Otherwise, route to `DefaultStartupPage` (defaults to `DashboardViewModel`).

### 3.4. Database Export & Notification Service
- `IDatabaseExportService` and `SqliteDatabaseExportService`:
  - Asynchronous background export of SQLite database to target path.
  - Safe checkpoint flush and timestamped default filename (`pcb-deftek-export-{yyyyMMdd-HHmmss}.db`).
- `INotificationService` and `WindowsNotificationService`:
  - Toast notification sent upon export completion: "Database Export Finished — Successfully exported database to {path}".

### 3.5. Settings Modal / Panel
Organized into four structured sections:
1. **Inference & Model Controls:**
   - Confidence threshold slider and numeric input ($0.0 \dots 1.0$, default $0.25$).
   - Hardware detection device selector: dynamically reflects available CPU/GPU devices (`Cpu`, `GpuDirectML`).
2. **Application Preferences:**
   - Default startup page dropdown selector (Dashboard, Live Detection, File-Based Detection, History).
   - "Skip dashboard on startup" synchronization.
3. **About & Attribution:**
   - Model name: `pcb-defect-yolov8m-dspcbsd` (links to Hugging Face model repository).
   - Model developer: `Janani-V` (links to GitHub profile).
   - Application developer: Rickyslash (links to `https://rickyslash.my.id`).
   - Developer GitHub: `ricky-kiva` (links to `https://github.com/ricky-kiva`).
   - Developer Instagram: `r.ckyslash` (links to `https://instagram.com/r.ckyslash`).
   - Developer LinkedIn: `ricky-fadel` (links to `https://linkedin.com/ricky-fadel`).
   - Ko-fi support button: links to `https://ko-fi.com/rickyslash`.
4. **Export Settings:**
   - Report export location path selector (default Windows Downloads).
   - Database export location path selector (default Windows Downloads).
   - Manual **Export Database** button triggering asynchronous export and completion notification.

### 3.6. Persistence Integration
- Store settings keys in SQLite via `ISettingsRepository`:
  - `Settings:Inference:ConfidenceThreshold` (double)
  - `Settings:Inference:PreferredDevice` (string)
  - `Settings:App:DefaultStartupPage` (string)
  - `Settings:App:SkipDashboardOnStartup` (bool)
  - `Settings:Export:ReportPath` (string)
  - `Settings:Export:DatabasePath` (string)

---

## 4. Defect Class Abbreviations Reference

| Defect Class | Abbreviation | Severity Level |
|---|---|---|
| Short | `SH` | Critical |
| Open | `OP` | Critical |
| Mouse Bite | `MB` | Medium-High |
| Spur | `SP` | Medium |
| Spurious Copper | `SC` | Medium-High |
| Hole Breakout | `HB` | High |
| Conductor Scratch | `CS` | Low-Medium |
| Conductor Foreign Object | `CFO` | High |
| Base Material Foreign Object | `BMFO` | Medium |

---

## 5. Out-of-Scope (Deferred to Later Phases)
- Batch file processing engine and progress worker (Phase 5).
- Camera frame acquisition, direct video rendering, and floating HUD (Phase 6).
- History thumbnail gallery, deep-zoom defect viewer, and region cropping (Phase 7).
- Statistics charts, drag-and-drop metric cards, and defect class accordions (Phase 8).
- QuestPDF audit-ready PDF document generator (Phase 9).
