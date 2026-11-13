# Tech Stack & Architectural Constraints: PCB Deftek

## 1. Technical Baseline
- **Runtime & Framework:** .NET 10.0 LTS Windows Desktop (`net10.0-windows`), C# 13.
- **Compiler Configuration:** `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`, `<UseWPF>true</UseWPF>`.
- **UI Framework:** Windows Presentation Foundation (WPF) with native built-in Fluent Theme (`PresentationFramework.Fluent`, `ThemeMode="System"`, Windows 11 Mica backdrop) and modular Resource Dictionaries.
- **Architecture Pattern:** MVVM (Model-View-ViewModel) using `CommunityToolkit.Mvvm`.
- **Dependency Injection:** `Microsoft.Extensions.DependencyInjection`.

## 2. Key Libraries & Dependencies
- **Machine Learning Inference:**
  - `Microsoft.ML.OnnxRuntime`
  - `Microsoft.ML.OnnxRuntime.DirectML` (GPU execution with CPU fallback)
  - Inference model: `Assets/Models/best.onnx`
- **Computer Vision & Image Manipulation:**
  - `OpenCvSharp4` / `OpenCvSharp4.Windows` (webcam frame capture, video decoding, bitmap manipulation, letterboxing, and viewport cropping)
- **UI Components & Desktop Integration:**
  - WPF native Fluent Theme (`PresentationFramework.Fluent` with modern Windows 11 controls, light/dark mode adaptation, and system accent colors)
  - Modular domain resource dictionaries (inspection status colors, custom layout templates, vector iconography)
  - Interactive charting (line charts with dual-series PASS/FAIL and vertical bar charts with interactive hover feedback)
  - Drag-and-drop support for dashboard metric card reordering
  - Windows Notifications (`Microsoft.Toolkit.Uwp.Notifications` / Windows toast notifications) for batch completion, PDF export, and database export alerts
- **Data Persistence:**
  - `Microsoft.Data.Sqlite` for local SQLite database management
  - Automated schema migration runner, database backup routine, and user-initiated database export service
  - Path: `%LOCALAPPDATA%\PcbDeftek\pcb-deftek\app.db` (default export path: Windows Downloads folder)
- **PDF Report Generation:**
  - `QuestPDF` for structured, code-first document layouts (single-column metric card summaries + annotated inspection images)
- **Logging & Diagnostics:**
  - `Serilog`, `Serilog.Sinks.File`, `Serilog.Sinks.Debug`
  - Custom exceptions under `/Common/Exceptions` with centralized UI exception handling
- **Testing:**
  - `xUnit` for unit and integration testing
  - `FluentAssertions` & `Moq`
  - `Reqnroll` (Cucumber for .NET) for behavior-driven acceptance tests
## 3. Core Commands
- **Build Solution:** `dotnet build`
- **Run Application:** `dotnet run --project pcb-deftek.csproj`
- **Execute Tests:** `dotnet test`
- **Restore Packages:** `dotnet restore`

## 4. Architectural Principles & Code Style
- **SOLID & Dependency Inversion:** Service layer abstracts ML inference, file processing, and camera acquisition. ViewModels depend solely on interfaces.
- **Repository Pattern:** Encapsulates SQLite queries, data mapping, and transactions.
- **MVVM Purity:** ViewModels contain zero references to `System.Windows.Controls`. DataTemplates map ViewModels to Views in XAML.
- **Event Aggregator:** `WeakReferenceMessenger` used strictly for decoupled cross-module communication (e.g., settings updates, status signals).
- **Code Style:** PascalCase for classes/methods/properties, camelCase for parameters/local variables, `_camelCase` for private fields. All public APIs explicit with nullable annotations.

### 4.1 Directory Structure
- **Place each newly created file in the appropriate directory based on its responsibility.**

## 5. System Boundaries & Constraints
- **Always:**
  - Run inference, camera processing, file I/O, PDF generation, and database export operations asynchronously off the UI thread.
  - Require explicit user confirmation before destructive operations (record deletion, capture-file deletion).
  - Validate hardware compatibility before initializing DirectML GPU provider.
  - Log structured diagnostics with Serilog.
  - Adhere to Nullable Reference Types with zero warnings.
- **Ask First:**
  - Database schema alterations.
  - Adding third-party NuGet packages (requires `socket` and `ctx7` checks).
  - Changing project-level persistent configurations.
- **Never:**
  - Block the UI thread with synchronous operations.
  - Delete or modify user-owned files (source images referenced by `SingleFile` / `BatchFolder` records); deletion is limited to database records and app-owned capture artifacts.
  - Hardcode absolute database paths or user folders.
  - Swallow exceptions silently.
  - Commit credentials, secrets, or unverified binaries.
