# Tech Stack & Architectural Constraints: PCB Deftek

## 1. Technical Baseline
- **Runtime & Framework:** .NET 10.0 LTS Windows Desktop (`net10.0-windows`), C# 13.
- **Compiler Configuration:** `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`, `<UseWPF>true</UseWPF>`.
- **UI Framework:** Windows Presentation Foundation (WPF) with Fluent styling and modular Resource Dictionaries.
- **Architecture Pattern:** MVVM (Model-View-ViewModel) using `CommunityToolkit.Mvvm`.
- **Dependency Injection:** `Microsoft.Extensions.DependencyInjection`.

## 2. Key Libraries & Dependencies
- **Machine Learning Inference:**
  - `Microsoft.ML.OnnxRuntime`
  - `Microsoft.ML.OnnxRuntime.DirectML` (GPU execution with CPU fallback)
  - Inference model: `Assets/Models/best.onnx`
- **Computer Vision & Image Manipulation:**
  - `OpenCvSharp4` / `OpenCvSharp4.Windows` (webcam frame capture, video decoding)
  - `SkiaSharp` or standard WPF imaging for bitmap manipulation, letterboxing, and cropping
- **Data Persistence:**
  - `Microsoft.Data.Sqlite` for local SQLite database management
  - Automated schema migration runner and database backup routine
  - Path: `%LOCALAPPDATA%\PcbDeftek\pcb-deftek\app.db`
- **PDF Report Generation:**
  - `QuestPDF` for structured, code-first document layouts
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

## 5. System Boundaries & Constraints
- **Always:**
  - Run inference, camera processing, and file I/O asynchronously off the UI thread.
  - Validate hardware compatibility before initializing DirectML GPU provider.
  - Log structured diagnostics with Serilog.
  - Adhere to Nullable Reference Types with zero warnings.
- **Ask First:**
  - Database schema alterations.
  - Adding third-party NuGet packages (requires `socket` and `ctx7` checks).
  - Changing project-level persistent configurations.
- **Never:**
  - Block the UI thread with synchronous operations.
  - Hardcode absolute database paths or user folders.
  - Swallow exceptions silently.
  - Commit credentials, secrets, or unverified binaries.
