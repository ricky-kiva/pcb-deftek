# Project Specification

## 1. Project Overview

- Build a PCB defect detection desktop application.
- The AI model detects **9 defect classes**.
  - Short (SH), Open (OP), Mouse bite (MB), Spur (SP), Spurious copper (SC), Hole breakout (HB), Conductor scratch (CS), Conductor foreign object (CFO), Base material foreign object (BMFO)
- Use WPF with .NET 10 LTS.
- Follow the architecture, development workflow, and agent instructions defined below.

## 2. AI Model

- Use `Assets/Models/best.onnx` as the inference model.
- Refer to the model documentation for class information and other model details:
  https://huggingface.co/Janani-V/pcb-defect-yolov8m-dspcbsd
- Support CPU and GPU inference where available.

## 3. Core Features

### 3.1. File-Based Detection

- Support image and video detection.
- Allow users to select:
  - Individual files.
  - Multiple files (batch processing).
  - Entire folders.
- Display detection progress using a progress bar.
- Send a Windows notification when processing completes.
- Allow large video files to proceed after displaying a warning. (Future enhancement)

### 3.2. Live Camera Detection

- Detect defects using the laptop's built-in camera.
- Allow users to capture an image with its bounding boxes.
- Provide a capture button and `Space` keyboard shortcut.
- Display a live PASS/FAIL status with the detected defect count.
- Provide a live confidence threshold slider.
- Allow users to toggle inference latency and FPS information.

### 3.3. Detection History

- Store detection results from uploaded images and captured camera images.
- Group history items by date.
- Each thumbnail must display:
  - Image name (default: date-number).
  - PASS/FAIL badge.

#### Image Viewer

- Open an image when its history item is selected.
- Support zooming through mouse scrolling and touchpad gestures.
- Automatically zoom into a defect when its bounding box is selected.
- Provide a button to reset the image to its original view.

#### Image Information Panel

Display the following information:
- Filename (editable; default: date-number).
- Detection timestamp.
- Detected classes and their confidence scores.

#### Bounding Box Selection

When a bounding box is selected:

- Display only the selected defect's information in the information panel.
- Show its:
  - Class.
  - Confidence score.
  - Root cause.
  - Impact.
  - Severity.
  - Recommended action.
- Allow users to zoom out gradually.
- Provide a Crop button to save the currently zoomed region as a new image.
- Display a small notice informing users that they can zoom out before cropping.

### 3.4. Defect Statistics Dashboard

- Make the dashboard the default startup page.
- Allow users to change the default startup page in Settings.
- Provide an option to skip the dashboard and open the camera view on startup.

Display:

- Total PCBs inspected.
- Total defects detected.
- Defect distribution across classes.
- Class information.
- PASS/FAIL inspection rate.
- Class information in expandable accordion sections.
- Detection mode: live camera or file-based detection.
- Export Data button.

Filtering:

- Support filtering by day, week, month, and custom date range.
- Allow filtering defect statistics by severity. (Severity definitions will be provided later.)

All dashboard statistics must reflect the selected filters.

### 3.5. Data Export

- Export the data currently displayed on the dashboard.
- Generate a PDF containing:
  - Inspection statistics.
  - Defect statistics.
  - Detection images.
  - Bounding boxes.
  - Relevant image and detection information.

### 3.6. Settings

Provide a Settings modal accessible from:
- A floating button on the camera view.
- A static button on the dashboard.

Settings must include:

- Confidence threshold.
- Enable/disable individual defect classes.
- Detection device selection (CPU/GPU).
  - Only display devices supported and available on the current machine.
- Default startup page.
- Skip-dashboard-on-startup option.
- Model version and information.
- Developer information and contact.
- Developer's Ko-fi link.

### 3.7. Database

- Use SQLite for persistent application data.
- Default database location:

  `%LOCALAPPDATA%\MyCompany\ThisApp\app.db`

- If the company name or application name has not been finalized, ask me before creating the database path.
- Implement database backup and migration support.
- Before database implementation, propose and discuss the database schema with me.

## 4. Architecture and Technical Stack

### Framework

- WPF with .NET 10 LTS.
- MVVM using CommunityToolkit.Mvvm.
- Dependency Injection using Microsoft.Extensions.DependencyInjection.
- ONNX Runtime for model inference.
- SQLite for persistence.
- Serilog for application logging.

### Architecture Principles

- Follow SOLID principles, especially Dependency Inversion.
- Implement a Service Layer to separate application logic from UI.
- Use the Repository Pattern for data access.
- Keep ViewModels modular and focused on their responsibilities.
- Use DataTemplates for ViewModel-to-View mapping.
- Use CommunityToolkit.Mvvm for:
  - Commands.
  - Observable properties and notifications.
  - Messenger-based communication when appropriate.
- Use the Event Aggregator / Messenger Pattern only when multiple independent components need to communicate through events without directly depending on each other.

### UI and Styling

- Use the WPF Fluent theme where supported.
- Prefer reusable Resource Dictionaries for shared UI resources.
- Use Styles and Triggers where appropriate.
- Keep the UI consistent, modular, and maintainable.

### Logging

- Implement structured logging using Serilog.
- Follow production-quality logging practices.
- Include appropriate log levels, contextual information, and exception details.
- Avoid logging sensitive information.

## 5. Testing Requirements

Implement both:

- **TDD:** xUnit for unit and integration testing.
- **BDD:** Reqnroll for behavior-driven acceptance testing.

Follow these principles:

- Write tests before implementation whenever practical.
- Test business logic independently of the UI.
- Cover critical workflows and edge cases.
- Keep tests maintainable and deterministic.

## 6. Engineering Standards

- Create specific application exceptions under `/Common/Exceptions`.
- Use meaningful exception types instead of relying on generic exceptions.
- Implement centralized exception handling where appropriate.
- Follow clean code, separation of concerns, and maintainability principles.
- Avoid unnecessary abstractions and overengineering.

## 7. CI/CD and Release

- Set up essential CI/CD practices suitable for the project.
- Automate building and testing.
- Ensure the application can be packaged as a Windows executable.
- Once the essential features are complete and validated, prepare a GitHub Release containing the executable.
- Before the first release, use the `shipping-and-launch` skill to prepare a deployment-specific checklist and go/no-go assessment.

## 9. Future Tasks

Do not implement these unless explicitly requested:

- Add support for large video files with an appropriate warning and processing strategy.
- Finalize defect severity definitions.
- Finalize company and application names.
- Add the application icon.
- Prepare the GitHub Release after essential features are completed.

