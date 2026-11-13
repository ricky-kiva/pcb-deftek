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

### 4.1 Directory Structure & Engineering Standards
- **The established directory structure is authoritative.** Place every new type in the existing folder that matches its layer and responsibility. Reuse the current layout before proposing a new one:
  - `Domain/Contracts` and `Domain/Models` — domain contracts and models.
  - `Infrastructure/{Inference,Notifications,Persistence,Repositories,Services,Vision}` — technical implementations and external integrations.
  - `Presentation/{Controls,Converters,Models,Services,Styles,ViewModels,Views}` — WPF presentation layers.
  - `Common/{Exceptions,...}` — shared cross-cutting primitives.
  - `tests/pcb-deftek.Tests` and `tests/pcb-deftek.Bdd` — automated verification.
- **ViewModels are view models only.** `Presentation/ViewModels` must not hold pure models, geometry/transform helpers, converters, services, or view code. Place pure presentation state and geometry in `Presentation/Models` (for example `ViewportTransform`, `InspectionNameEditor`), WPF value converters in `Presentation/Converters`, UI-facing abstractions/implementations in `Presentation/Services`, and view-only interaction in code-behind or `Presentation/Controls`.
- **Do not create parallel structures.** Do not add a new top-level folder, duplicate an existing abstraction, or skip a layer when an established location fits. Moving a type across layers or introducing a new folder/layer is an **Ask First** decision.
- **Apply standard software-engineering practice:** single responsibility, high cohesion, low coupling, dependency inversion, one source of truth, testability without UI, no duplicated or dead code, minimal public surface, and consistent naming/formatting.
- **Preserve the architecture.** Match an existing pattern in the repository before introducing a new one; architectural consistency outranks novelty.

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
