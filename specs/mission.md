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
- **File-Based Detection:** Single image, image batch, video file, and recursive folder processing with progress tracking and completion toast notification.
- **Live Camera Detection:** Direct camera stream, live FPS and inference latency metrics, live PASS/FAIL status counter, live confidence threshold slider, and spacebar/button image capture with bounding boxes.
- **Inspection History:** Persistent local storage, date-grouped items, thumbnails with PASS/FAIL badges and editable filenames.
- **Defect Viewer:** Interactive pan and smooth zoom (scroll/touchpad), auto-focus on selected defect bounding box, gradual zoom-out, and localized region cropping with guidance notice.
- **Defect Detail Panel:** Class, confidence score, root cause, impact, severity, and recommended corrective action.
- **Statistics Dashboard:** Default startup view (configurable), total PCBs inspected, total defects detected, defect distribution across 9 classes, expandable accordions, PASS/FAIL rate, detection mode breakdown (camera vs file), and multi-scale date filtering (day, week, month, custom).
- **Data Export:** PDF export capturing active dashboard metrics, charts, detection images with bounding boxes, and defect metadata.
- **Settings:** Confidence threshold slider, defect class toggles, hardware detection device selection (CPU/GPU), startup navigation behavior, model metadata, and developer contact/Ko-fi links.

### 5.2. Future Enhancements
These capabilities are confirmed enhancements that will be implemented in future phases after all essential features above are complete and validated:
- **Large Video Processing:** Advanced video streaming strategies and warning dialogs before processing large video files.
- **Defect Severity Definitions:** Formal defect severity classifications and severity-based dashboard filtering once stakeholder definitions are provided.
- **Corporate Identity & Custom Storage:** Finalization of formal company and application names, including customized database storage paths (`%LOCALAPPDATA%\<Company>\<App>\app.db`).
- **Application Icon & Visual Assets:** Official application icon, splash branding, and polished desktop assets.
- **GitHub Release & Distribution:** Production packaging and official GitHub Release distribution following the `shipping-and-launch` readiness checklist.
