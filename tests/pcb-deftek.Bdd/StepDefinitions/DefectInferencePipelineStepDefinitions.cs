using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using pcb_deftek.Domain.Models;
using pcb_deftek.Infrastructure.Inference;
using pcb_deftek.Infrastructure.Vision;
using Reqnroll;

namespace pcb_deftek.Bdd.StepDefinitions;

[Binding]
public class DefectInferencePipelineStepDefinitions : IDisposable
{
    private OnnxModelRunner? _runner;
    private OpenCvVisionPreprocessor? _preprocessor;
    private YoloPostprocessor? _postprocessor;
    private PcbDefectDetector? _detector;
    private InferenceResult? _result;

    private static string ResolveBestOnnxPath()
    {
        string? current = AppDomain.CurrentDomain.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            string candidate = Path.Combine(current, "Assets", "Models", "best.onnx");
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }

            var parent = Directory.GetParent(current);
            current = parent?.FullName;
        }

        string cwdCandidate = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "Models", "best.onnx");
        if (File.Exists(cwdCandidate))
        {
            return Path.GetFullPath(cwdCandidate);
        }

        throw new FileNotFoundException("best.onnx not found for BDD pipeline testing.");
    }

    private static string ResolveTestImagePath(string filename)
    {
        string? current = AppDomain.CurrentDomain.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            string candidate = Path.Combine(current, "tests", "pcb-deftek.Tests", "TestData", "Model", filename);
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }

            string directCandidate = Path.Combine(current, "TestData", "Model", filename);
            if (File.Exists(directCandidate))
            {
                return Path.GetFullPath(directCandidate);
            }

            var parent = Directory.GetParent(current);
            current = parent?.FullName;
        }

        string cwdCandidate = Path.Combine(Directory.GetCurrentDirectory(), "tests", "pcb-deftek.Tests", "TestData", "Model", filename);
        if (File.Exists(cwdCandidate))
        {
            return Path.GetFullPath(cwdCandidate);
        }

        throw new FileNotFoundException($"Test image fixture '{filename}' not found for BDD testing.");
    }

    [Given(@"an initialized ONNX defect detection pipeline")]
    public void GivenAnInitializedOnnxDefectDetectionPipeline()
    {
        string modelPath = ResolveBestOnnxPath();
        _runner = new OnnxModelRunner(new InferenceOptions
        {
            PreferredDevice = ExecutionDevice.Cpu,
            ModelPath = modelPath
        });
        _preprocessor = new OpenCvVisionPreprocessor(640, 640);
        _postprocessor = new YoloPostprocessor();
        _detector = new PcbDefectDetector(_runner, _preprocessor, _postprocessor);
    }

    [When(@"a PCB test image ""(.*)"" is inspected for defects")]
    public void WhenAPcbTestImageIsInspectedForDefects(string filename)
    {
        _detector.Should().NotBeNull();
        string imagePath = ResolveTestImagePath(filename);
        _result = _detector!.DetectFromFile(imagePath);
    }

    [When(@"a PCB test image ""(.*)"" is read as bytes and inspected asynchronously")]
    public async Task WhenAPcbTestImageIsReadAsBytesAndInspectedAsynchronously(string filename)
    {
        _detector.Should().NotBeNull();
        string imagePath = ResolveTestImagePath(filename);
        byte[] bytes = await File.ReadAllBytesAsync(imagePath);
        _result = await _detector!.DetectFromBytesAsync(bytes);
    }

    [Then(@"the inference result should contain timing diagnostics")]
    public void ThenTheInferenceResultShouldContainTimingDiagnostics()
    {
        _result.Should().NotBeNull();
        _result!.Timing.TotalTimeMs.Should().BeGreaterThan(0);
        _result.Timing.PreprocessingTimeMs.Should().BeGreaterThan(0);
        _result.Timing.ModelExecutionTimeMs.Should().BeGreaterThan(0);
    }

    [Then(@"the detected defects should have valid bounding boxes and normalized coordinates")]
    public void ThenTheDetectedDefectsShouldHaveValidBoundingBoxesAndNormalizedCoordinates()
    {
        _result.Should().NotBeNull();
        foreach (var defect in _result!.Defects)
        {
            defect.Confidence.Should().BeInRange(0.25f, 1.0f);
            defect.BoundingBox.X.Should().BeInRange(0f, 1f);
            defect.BoundingBox.Y.Should().BeInRange(0f, 1f);
            defect.BoundingBox.Width.Should().BeGreaterThan(0f);
            defect.BoundingBox.Height.Should().BeGreaterThan(0f);
            (defect.BoundingBox.X + defect.BoundingBox.Width).Should().BeLessThanOrEqualTo(1.0001f);
            (defect.BoundingBox.Y + defect.BoundingBox.Height).Should().BeLessThanOrEqualTo(1.0001f);
        }
    }

    [Then(@"the inspection status should be evaluated")]
    public void ThenTheInspectionStatusShouldBeEvaluated()
    {
        _result.Should().NotBeNull();
        if (_result!.Passed)
        {
            _result.Status.Should().Be(InspectionStatus.Pass);
            _result.DefectCount.Should().Be(0);
        }
        else
        {
            _result.Status.Should().Be(InspectionStatus.Fail);
            _result.DefectCount.Should().BeGreaterThan(0);
        }
    }

    public void Dispose()
    {
        _runner?.Dispose();
    }
}
