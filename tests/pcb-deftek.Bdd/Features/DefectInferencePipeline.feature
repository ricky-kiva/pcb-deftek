Feature: Defect Inference Pipeline
  As a quality technician
  I want to run the automated PCB defect detection model on board images
  So that defects are localized with bounding boxes and categorized accurately

  Scenario: Run end-to-end defect detection on a PCB test image
    Given an initialized ONNX defect detection pipeline
    When a PCB test image "OP-1.jpg" is inspected for defects
    Then the inference result should contain timing diagnostics
    And the detected defects should have valid bounding boxes and normalized coordinates
    And the inspection status should be evaluated

  Scenario: Run defect detection asynchronously on raw image bytes
    Given an initialized ONNX defect detection pipeline
    When a PCB test image "SH-1.jpg" is read as bytes and inspected asynchronously
    Then the inference result should contain timing diagnostics
    And the detected defects should have valid bounding boxes and normalized coordinates
