<p align="center">
  <img src="docs/assets/readme-logo-1-1-c.png" alt="PCB Deftek" width="420">
</p>

<h1 align="center">PCB Deftek</h1>

<p align="center">
  <p align="center">
      <a href="https://github.com/ricky-kiva/pcb-deftek/actions/workflows/ci.yml" style="text-decoration: none;">
        <img src="https://github.com/ricky-kiva/pcb-deftek/actions/workflows/ci.yml/badge.svg" alt="CI status">
      </a>
      <a href="https://dotnet.microsoft.com/en-us/download/dotnet/10.0" style="text-decoration: none;">
          <img src="https://img.shields.io/badge/10.0-512BD4?logo=dotnet&logoColor=white" alt=".NET 10">
        </a>
      <a href="https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-14" style="text-decoration: none;">
        <img src="https://img.shields.io/badge/C%23-14.0-239120?logo=csharp&logoColor=white" alt="C# 14">
      </a>
    </p>
</p>

Automated PCB trace defect detection and analysis for Windows. PCB Deftek inspects boards with an ONNX YOLO detector, localizes and classifies trace defects, stores inspection evidence in SQLite, and produces audit-ready PDF reports.

> **Detection scope:** This application is specifically designed to detect PCB trace defects (e.g., open circuits and short circuits). It is best suited for bare PCBs without mounted components.

## Features

- **Nine defect classes** — Short (SH), Open (OP), Mouse bite (MB), Spur (SP), Spurious copper (SC), Hole breakout (HB), Conductor scratch (CS), Conductor foreign object (CFO), Base material foreign object (BMFO).
- **Live camera inspection** — full-screen viewport with bounding-box overlays, PASS/FAIL badge, defect counter, optional latency/FPS telemetry, confidence slider, `Space` capture, and idle auto-hide.
- **File and batch detection** — drag-and-drop single files, multi-selection, or whole folders with per-batch progress and inline ETA.
- **Inspection history** — date-grouped gallery, PASS/FAIL badges, filters, inline rename, multi-select deletion, and an interactive pan/zoom viewer with defect diagnostics and viewport cropping.
- **Statistics dashboard** — date/class/severity filters, four metric cards with interactive charts, drag-and-drop card ordering, and defect knowledge cards.
- **Audit-ready PDF export** — filtered metric summary plus annotated evidence images and three-column defect tables, written to `Downloads\PCB Deftek\Report`.
- **Settings** — confidence threshold, CPU/GPU device selection, startup page, report/image output locations, and database backup/export.

## Screenshots

### Dashboard

![Statistics dashboard](docs/assets/readme-ss-page-dashboard-1-1-c.jpg)

### Live Detection

![Live camera detection](docs/assets/readme-ss-page-live-detection-1-1-c.jpg)

### File Detection

![File and batch detection](docs/assets/readme-ss-page-file-detection-1-1-c.jpg)

### History

![Inspection history](docs/assets/readme-ss-page-history-1-1-c.jpg)

## Prerequisites

- Windows 10 or 11, x64
- [.NET 10 SDK](https://dotnet.microsoft.com/download) (build) or the .NET 10 Desktop Runtime (run a published build)
- Optional: a webcam for Live Detection
- Optional: a DirectML-capable GPU for hardware-accelerated inference (falls back to CPU)

## Installation

Download the latest artifacts from the [Releases](https://github.com/ricky-kiva/pcb-deftek/releases) page.

**Installer (recommended)** — per-user, no administrator rights:

```powershell
.\PCB-Deftek-<version>-setup.exe
```

The installer places the app in `%LOCALAPPDATA%\Programs\PCB Deftek`, adds a Start-menu shortcut (desktop shortcut optional), and registers an uninstaller. User data in `%LOCALAPPDATA%\PcbDeftek` and exported reports are left untouched by install and uninstall.

**Portable** — extract `PCB-Deftek-<version>-portable-win-x64.zip` and run `pcb-deftek.exe`. The self-contained build includes the .NET runtime.

Verify a download before running it:

```powershell
(Get-FileHash .\PCB-Deftek-<version>-setup.exe -Algorithm SHA256).Hash
```

Compare the value with the matching line in `SHA256SUMS.txt`. Release executables are Authenticode-signed with an RFC 3161 timestamp; Windows may still show an unknown-publisher warning until SmartScreen reputation builds.

## Getting started

```powershell
dotnet restore
dotnet build pcb-deftek.slnx -c Release
dotnet run --project pcb-deftek.csproj
```

The inference model ships with the repository at `Assets/Models/best.onnx` and is copied to the build output automatically.

## Tests

```powershell
dotnet test pcb-deftek.slnx
```

- `tests/pcb-deftek.Tests` — xUnit unit and SQLite integration tests.
- `tests/pcb-deftek.Bdd` — Reqnroll acceptance scenarios against real services with isolated databases.

Every test creates its own SQLite database, capture folder, and export folder under the system temp directory. Tests never read or write `%LOCALAPPDATA%\PcbDeftek` or the user's Downloads folder.

## Data and logs

| Purpose | Location |
|---|---|
| Database | `%LOCALAPPDATA%\PcbDeftek\pcb-deftek\app.db` |
| Logs (Serilog, 30 files retained) | `%LOCALAPPDATA%\PcbDeftek\pcb-deftek\logs` |
| Live capture images | `%LOCALAPPDATA%\PcbDeftek\pcb-deftek\Captures` |
| PDF reports | `Downloads\PCB Deftek\Report` |
| Database backups/exports | `Downloads\PCB Deftek\Backup` |
| Cropped images | `Downloads\PCB Deftek\Images` |

Report, backup, and image folders are configurable in Settings. User-owned source images are never modified or deleted; cleanup is limited to records and app-owned capture files.

## Sample PCB Images

You can use the sample PCB images provided in [`docs/sample-images/`](https://github.com/ricky-kiva/pcb-deftek/tree/main/docs/sample-images) to try out the application's AI-powered defect detection functionality.

Alternatively, you can browse and download additional PCB defect images from the [Ultralytics Dataset](https://platform.ultralytics.com/lion-heron/datasets/dspcbsd-pcb-surface-defect).

## Attribution

- **Developer:** [Rickyslash](https://rickyslash.my.id)
- **Model:** [PCB Defect YOLOv8m](https://huggingface.co/Janani-V/pcb-defect-yolov8m-dspcbsd/blob/main/inspector.py)
- **Dataset:** [A PCB Surface Defect Dataset](https://www.nature.com/articles/s41597-024-03656-8)

## License

GNU General Public License v3.0 — see [`LICENSE.txt`](LICENSE.txt).

