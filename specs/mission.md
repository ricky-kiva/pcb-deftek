# Mission & Product Scope: PCB Deftek

## 1. Objective & Problem Statement
PCB manufacturing requires fast, accurate, and consistent quality inspection. Manual inspection is prone to human error, fatigue, and throughput bottlenecks, leading to delayed defect detection and costly rework. **`pcb-deftek`** automates PCB defect detection, localization, and classification in real time, delivering consistent quality control and actionable diagnostic insights.

## 2. Vision
A fast, responsive, and modern Windows desktop inspection suite that seamlessly integrates file and batch inspection, real-time live camera analysis, interactive root-cause defect diagnostics, persistent inspection history, quality analytics dashboards, and audit-grade PDF reporting.

## 3. Target Audience
- **Assembly Line Technicians:** Need immediate PASS/FAIL classification during manual assembly or rework via live camera and one-touch frame capture.
- **QA Engineers & Inspectors:** Require deep-dive root cause diagnostics, bounding box inspection, localized defect cropping, and formal audit PDF generation.
- **Plant & Production Managers:** Need aggregate visibility into first-pass yield, defect class frequency distribution, and trend filtering on the statistics dashboard.

## 4. Defect Taxonomy (9 Classes)
1. **Short (SH)**
2. **Open (OP)**
3. **Mouse bite (MB)**
4. **Spur (SP)**
5. **Spurious copper (SC)**
6. **Hole breakout (HB)**
7. **Conductor scratch (CS)**
8. **Conductor foreign object (CFO)**
9. **Base material foreign object (BMFO)**

## 5. Scope & Feature Breakdown

### 5.1. Essential Features
These features comprise the primary deliverables for initial development and validation:
- **Defect Detection Engine:** Automated detection for all 9 defect classes with CPU and GPU hardware acceleration support.
- **Application Shell & Layout:**
  - Collapsible Sidebar Sheet on the left: Dashboard, Live Detection, File-Based Detection, History, and Settings anchored at the bottom.
  - Compact footer with dot-separated items: Social media icons (GitHub, LinkedIn, Instagram), Buy Me a Coffee link, and developer portfolio link.
- **File-Based Detection:** Large drag-and-drop upload area supporting single-file, multi-file, and folder uploads (or File Explorer prompt), with progress bar, inline estimated completion time (ETA), and Windows completion toast notification.
- **Live Camera Detection:** Full-screen video viewport with rendered defect bounding boxes and collapsed sidebar by default; floating top-right 3-card HUD (PASS/FAIL badge + defect count, telemetry toggle + latency/FPS display, camera source selector dropdown); bottom-left confidence slider; bottom-right capture button with `Space` shortcut; and cursor idle auto-hide behavior.
- **Inspection History:** Persistent local storage, date-grouped gallery thumbnail cards with 1:1 square preview, top-left PASS/FAIL badge, and default `Date-Number` filename with double-click inline editing.
- **Interactive Defect Viewer:**
  - Main viewing area: Interactive pan and smooth zoom (scroll and touchpad gestures), click-to-focus on defect bounding boxes, floating Reset View button, and floating Crop button (exports currently visible viewport).
  - Image Information Sidebar: Two distinct states: *Default* (editable filename, detection timestamp, summary list of detected classes with confidence scores) and *Focused* (selected defect's class name, confidence, severity, root cause, impact, and recommended action).
- **Statistics Dashboard:**
  - Filter bar: Date Range Selector (Day, Week, Month button group + Custom date-range button), Defect Type multi-select Combobox (9 classes), Defect Severity multi-select Dropdown (Critical, High, Medium-High, Medium, Low-Medium).
  - Action bar: PDF Export Data button, Notification button (modal displaying inference and data-export progress bars), and 1-column vs 2-column Metric Card Layout Toggle.
  - Metric Cards: 4 cards (Total PCBs Inspected, Total Defects Detected, PASS/FAIL Inspection Rate line chart with green/red lines, Defect Distribution Across Defect Classes vertical bar chart with abbreviated labels); drag-and-drop card reordering; click-to-enlarge chart dialog; interactive hover feedback.
  - Defect Class Information: Single Accordion containing defect cards (defect class name, severity information, 1:1 sample image, explanation, root cause, impact, and recommended action).
  - Startup Preference: "Skip dashboard on startup" checkbox navigating directly to Live Detection on launch.
- **Data & Report Export:** PDF export capturing all 4 metric cards in a single column and annotated PCB detection images with bounding boxes/metadata, defaulting to the Windows Downloads folder with a Windows notification on completion.
- **Settings:** Confidence threshold slider/input, dynamic CPU/GPU device selection, default startup page selector, About & Attribution (model info and developer links/social icons/Ko-fi), Report export location setting, and Database export location setting + manual database export trigger with Windows notification.

### 5.2. Future Enhancements
These capabilities are confirmed enhancements that will be implemented in future phases after all essential features above are complete and validated:
- **Large Video Processing:** Advanced video streaming strategies and warning dialogs before processing large video files.
- **Corporate Identity & Custom Storage:** Finalization of formal company and application names, including customized database storage paths (`%LOCALAPPDATA%\<Company>\<App>\app.db`).
- **Application Icon & Visual Assets:** Official application icon, splash branding, and polished desktop assets.
- **GitHub Release & Distribution:** Production packaging and official GitHub Release distribution following the `shipping-and-launch` readiness checklist.
