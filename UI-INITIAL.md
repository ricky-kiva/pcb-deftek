# UI Structure & Requirements

This specification focuses exclusively on **page structure, component hierarchy, layout, and interactions**. The application should use WPF's new Fluent Theme as its visual & UI foundation.

## 1. General Application Layout

### Footer

A very compact footer with minimal vertical padding. Arrange the following items horizontally, separated by a dot (`·`):

1. Social media icons: GitHub, LinkedIn, Instagram.
2. Buy Me a \<Coffee icon>: where coffee icon links to [https://ko-fi.com/rickyslash](https://ko-fi.com/rickyslash)
3. Made with \<Heart icon> by `Rickyslash`: where Rickyslash links to [https://rickyslash.my.id](https://rickyslash.my.id)

### Sidebar Navigation

A collapsible Sidebar Sheet positioned on the left side of the application.

Navigation items, in order:

1. Dashboard
2. Live Detection
3. File-Based Detection
4. History
5. Settings (anchored at the bottom of the sidebar).

## 2. Dashboard Page

### Row 1 – Filters & Actions

Arrange the filters on the left and action controls on the right.

**Left section – Filters:**

- **Date Range Selector:**

  - Button group: Day, Week, Month.
  - Custom button: Opens a custom date-range selector.

- **Defect Type Filter:**

  - Multi-select Combobox.
  - Options: Short, Spur, Spurious Copper, Open, Mouse Bite, Hole Breakout, Conductor Scratch, CFO (Conductor Foreign Object), BMFO (Base Material Foreign Object).

- **Defect Severity Filter:**

  - Multi-select Dropdown.
  - Options: Critical, High, Medium-High, Medium, Low-Medium.

**Right section – Actions:**

- **Export Data button:** Exports the current dashboard data, respecting the currently applied filters and configurations, into a PDF.

  - Default export location: Windows Downloads folder.
  - Send a Windows notification when PDF generation finishes.
  - PDF contents:
    - All four metric cards, arranged in a single column.
    - Annotated detection images with bounding boxes and their corresponding information.

- **Notification button:** Opens a modal/dialog displaying inference and data-export progress using progress bars.

- **Metric Card Layout Toggle:** An icon-only button group for switching between one-column and two-column metric card layouts.

### Row 2 – Metric Cards

Display four metric cards:

1. **Total PCBs Inspected**
2. **Total Defects Detected**
3. **PASS/FAIL Inspection Rate**
   - Main value: PASS percentage.
   - Chart: Two lines representing PASS and FAIL rates, using green and red respectively.
4. **Defect Distribution Across Defect Classes**
   - Use abbreviated defect class names as chart labels.

**Shared Metric Card Structure:**

Each card follows the same hierarchy:

- First row: Small, bold title.
- Second row:
  - Main numerical value.
  - Chart visualization.

**Chart types:**

- Line charts: Display the number of inspections or relevant values for each time unit within the selected date range.
- Vertical bar charts: Display defect distribution across classes within the selected date range.

**Interactions:**

- Metric cards must support drag-and-drop reordering.
- Clicking a chart opens a modal/dialog displaying an enlarged version of that chart.
- Chart data points and elements must provide interactive hover feedback.

### Row 3 – Defect Class Information

Use a single Accordion containing a collection of defect information cards.

Each card follows this structure:

- **First row:** Full-width section containing the defect class name.
- **Second row:** Two-column layout:
  - **Left column:** Severity information and a square (1:1 aspect ratio) defect image.
  - **Right column:** Four vertically arranged information sections:
    1. Explanation
    2. Root Cause
    3. Impact
    4. Recommended Action

### Row 4 – Startup Preference

Display a small checkbox with the label:

"Skip dashboard on startup"

When checked, the application should open directly on the Live Detection page the next time it launches.

## 3. Live Camera / Live Detection Page

### Main Viewport

Use a full-screen video viewport displaying the live camera feed with rendered defect bounding boxes.

The sidebar remains accessible but is collapsed by default on this page.

### Floating Controls

Arrange three compact cards vertically in the top-right corner:

**First card:**

- Live PASS/FAIL status badge.
- Detected defect counter.

**Second card:**

- Toggle for enabling/disabling telemetry display.
- Telemetry information:
  - Inference latency (ms).
  - FPS.

**Third card (leftmost):**

- Dropdown for changing the camera source.

**Additional Controls:**

- **Bottom-left:** Confidence threshold slider.
- **Bottom-right:** Capture button.
  - Display a small hint indicating that `Space` is its keyboard shortcut.

### Auto-Hide Behavior

When the cursor remains stationary for a few seconds, gradually fade out the configuration controls.

The following elements must remain visible:

- Live PASS/FAIL status badge.
- Detected defect counter.
- Telemetry display, but only when its toggle is enabled.

## 4. File-Based Detection Page

### Row 1 – File Upload

Display a large drag-and-drop upload area supporting:

- Single-file uploads.
- Multiple-file uploads.
- Folder uploads.
- Clicking the upload area opens the Windows File Explorer.

### Row 2 – Processing Progress

Display:

- Processing progress bar.
- Estimated completion time, arranged inline with the progress indicator.

When processing finishes, send a Windows notification.

## 5. Detection History Page

Display a date-grouped, phone-gallery-style collection of detection history thumbnail cards.

### Row 1 – Filters & Actions

Mirror the Dashboard Page Row 1 structure: arrange filters on the left and action controls on the right.

**Left section – Filters:**

- **Date Range Selector:**
  - Button group: Day, Week, Month.
  - Custom button: Opens a custom date-range selector.
  - No preset selected means all time; clicking the active preset again clears it.
- **Status Filter:**
  - Multi-select Dropdown.
  - Options: PASS, FAIL.
  - Empty selection means both statuses.
- **Defect Class Filter:**
  - Multi-select Combobox.
  - Options: Short, Spur, Spurious Copper, Open, Mouse Bite, Hole Breakout, Conductor Scratch, CFO (Conductor Foreign Object), BMFO (Base Material Foreign Object).
  - Empty selection means all classes; a record matches when it contains any selected class.

**Right section – Actions:**

- Result count (`N results`).
- **Select Loaded** and **Clear Selection** actions.
- **Delete Selected (N)** button with destructive styling; disabled when nothing is selected or deletion is running.

### History Container

- Group images by date.
- Arrange thumbnail cards in a gallery-style layout.
- Each card must have a square (1:1 aspect ratio) image preview.
- Overlay a PASS/FAIL status badge in the top-left corner of the thumbnail.
- Overlay a selection checkbox in the top-right corner of the thumbnail.
  - Toggling the checkbox selects the card without opening the Interactive Image Viewer.
  - Selected cards must have a visible highlight.
- Display the image filename below the thumbnail.
  - Default filename format: Date-Number.
  - Allow inline filename editing by double-clicking the filename.
- Filters compose; changing any filter reloads the gallery from the first page and clears the current selection.
- Show a distinct "No inspections match these filters" state with a Clear Filters action when filters return no records.

Clicking a thumbnail (outside the checkbox) opens the Interactive Image Viewer.

### Deleting Inspections

- Multi-select deletion requires a confirmation dialog stating the number of inspections and the retention policy:
  - App-owned capture images are deleted.
  - User-owned original source images are kept.
- Cancel performs no changes.
- After a confirmed deletion, the gallery, date groups, and result count refresh.

## 6. Interactive Image Viewer

Use an image viewer panel/modal with two main sections:

- Main image viewing area.
- Image Information Sidebar positioned on the right.

### Main Image Viewing Area

**Image interactions:**

- Pan and zoom using mouse scrolling and touchpad gestures.
- Display interactive defect bounding boxes.
- Clicking a bounding box automatically zooms and focuses on the corresponding defect.

**Image controls:**

- Floating Reset View button in the bottom-right corner of the image area.
  - Restores the image to its original full-image view.
- Crop button in the bottom-right corner of the image area.
  - Exports the currently visible zoomed viewport as a new image.

### Image Information Sidebar

**Default State:**

Display:

- Editable filename field.
- Detection timestamp.
- Summary list of detected defect classes with their respective confidence scores.

**Focused State (when a bounding box is selected):**

Replace the default information with the selected defect's details:

- Defect class name.
- Confidence score.
- Severity.
- Root cause.
- Impact.
- Recommended action.

**Sidebar Action (available in both states):**

- **Delete Image** button, rendered below the state content with destructive styling.
  - Requires confirmation.
  - Deletes the current inspection and its app-owned capture images; user-owned original source images are kept.
  - On success, closes the viewer and refreshes the Detection History page.
  - Disabled while the record is loading or a deletion is running.

## 7. Settings Modal / Panel

Organize settings into the following sections:

### Inference & Model Controls

- Confidence threshold slider/input.
- Detection device selector.
  - Dynamically display available CPU/GPU devices.

### Application Preferences

- Default startup page dropdown selector.

### About & Attribution

**Model Information:**

- Model name: `pcb-defect-yolov8m-dspcbsd` (links to: [https://huggingface.co/Janani-V/pcb-defect-yolov8m-dspcbsd](https://huggingface.co/Janani-V/pcb-defect-yolov8m-dspcbsd))
- Model developer: `Janani-V` (links to: [https://github.com/Janviswa](https://github.com/Janviswa))

**Developer Information (displayed on icons, except the developer):**

- Developer: Rickyslash. (links to: [https://rickyslash.my.id](https://rickyslash.my.id))
- GitHub: `ricky-kiva` (links to: [https://github.com/ricky-kiva](https://github.com/ricky-kiva))
- Instagram: `r.ckyslash` (links to: [https://instagram.com/r.ckyslash](https://instagram.com/r.ckyslash))
- LinkedIn: `ricky-fadel` (links to: [https://linkedin.com/ricky-fadel](https://linkedin.com/ricky-fadel))
- Ko-fi link/button. (links to: [https://ko-fi.com/rickyslash](https://ko-fi.com/rickyslash))

### Export

**Report:**

- Export location setting.

**Database:**

- Export location setting.
- Default location: Windows Downloads folder.
- Export button.
- Send a Windows notification when export finishes.

## 8. Defect Class Abbreviations

Use these abbreviations consistently wherever defect class names are shortened, particularly in charts.

| Defect Class                 | Abbreviation |
| ---------------------------- | ------------ |
| Short                        | SH           |
| Open                         | OP           |
| Mouse Bite                   | MB           |
| Spur                         | SP           |
| Spurious Copper              | SC           |
| Hole Breakout                | HB           |
| Conductor Scratch            | CS           |
| Conductor Foreign Object     | CFO          |
| Base Material Foreign Object | BMFO         |

## 9. Additional Implementation Notes

- When generating sample PCB detection images, use top-down PCB views captured at a straight 90-degree angle.
- Although this specification describes certain elements using rows and columns, these are descriptions of content hierarchy, not mandatory grid or table implementations. Choose the most appropriate layout structure for each section.
- Preserve the specified component hierarchy and interactions without introducing additional UI components or features.
