using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using OpenCvSharp;
using pcb_deftek.Common.Exceptions;
using pcb_deftek.Infrastructure.Inference;
using pcb_deftek.Infrastructure.Vision;
using Xunit;

namespace pcb_deftek.Tests;

public class PcbDefectDetectorTests : IDisposable
{
    private readonly OnnxModelRunner _runner;
    private readonly OpenCvVisionPreprocessor _preprocessor;
    private readonly YoloPostprocessor _postprocessor;
    private readonly PcbDefectDetector _detector;

    public PcbDefectDetectorTests()
    {
        string modelPath = ResolveBestOnnxPath();
        _runner = new OnnxModelRunner(new InferenceOptions
        {
            PreferredDevice = ExecutionDevice.Cpu,
            ModelPath = modelPath
        });
        _preprocessor = new OpenCvVisionPreprocessor(640, 640);
        _postprocessor = new YoloPostprocessor(new InferenceOptions
        {
            ConfidenceThreshold = 0.25f,
            NmsIouThreshold = 0.45f
        });

        _detector = new PcbDefectDetector(_runner, _preprocessor, _postprocessor);
    }

    private static string ResolveBestOnnxPath()
    {
        string[] candidates =
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Models", "best.onnx"),
            Path.Combine(Directory.GetCurrentDirectory(), "Assets", "Models", "best.onnx"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "Assets", "Models", "best.onnx"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Assets", "Models", "best.onnx")
        };

        foreach (var c in candidates)
        {
            if (File.Exists(c))
            {
                return Path.GetFullPath(c);
            }
        }

        throw new FileNotFoundException("best.onnx not found for detector testing.");
    }

    private static string ResolveTestImagePath(string filename)
    {
        string[] candidates =
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData", "Model", filename),
            Path.Combine(Directory.GetCurrentDirectory(), "tests", "pcb-deftek.Tests", "TestData", "Model", filename),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "TestData", "Model", filename)
        };

        foreach (var c in candidates)
        {
            if (File.Exists(c))
            {
                return Path.GetFullPath(c);
            }
        }

        throw new FileNotFoundException($"Test image fixture '{filename}' not found.");
    }

    [Fact]
    public void DetectFromFile_WithRealFixtureImage_ExecutesPipelineAndReturnsDiagnostics()
    {
        // Arrange
        string imagePath = ResolveTestImagePath("OP-1.jpg");
        var customId = Guid.NewGuid();

        // Act
        var result = _detector.DetectFromFile(imagePath, inspectionId: customId);

        // Assert
        result.Should().NotBeNull();
        result.InspectionId.Should().Be(customId);
        result.Transform.Should().NotBeNull();
        result.Transform.TargetWidth.Should().Be(640);
        result.Transform.TargetHeight.Should().Be(640);

        // Timing diagnostics
        result.Timing.Should().NotBeNull();
        result.Timing.PreprocessingTimeMs.Should().BeGreaterThan(0);
        result.Timing.ModelExecutionTimeMs.Should().BeGreaterThan(0);
        result.Timing.PostprocessingTimeMs.Should().BeGreaterThanOrEqualTo(0);
        result.Timing.TotalTimeMs.Should().BeGreaterThan(0);

        // Status derivation
        if (result.Defects.Count == 0)
        {
            result.Passed.Should().BeTrue();
            result.Status.Should().Be(Domain.Models.InspectionStatus.Pass);
        }
        else
        {
            result.Passed.Should().BeFalse();
            result.Status.Should().Be(Domain.Models.InspectionStatus.Fail);
            result.Defects[0].Confidence.Should().BeInRange(0.25f, 1.0f);
        }
    }

    [Fact]
    public async Task DetectFromFileAsync_WithRealFixtureImage_ExecutesOffThreadAndReturnsResult()
    {
        // Arrange
        string imagePath = ResolveTestImagePath("SH-1.jpg");

        // Act
        var result = await _detector.DetectFromFileAsync(imagePath);

        // Assert
        result.Should().NotBeNull();
        result.InspectionId.Should().NotBeEmpty();
        result.Timing.TotalTimeMs.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Detect_WithOpenCvMat_ExecutesPipelineSuccessfully()
    {
        // Arrange
        string imagePath = ResolveTestImagePath("SC-1.jpg");
        using var mat = Cv2.ImRead(imagePath, ImreadModes.Color);

        // Act
        var result = _detector.Detect(mat);

        // Assert
        result.Should().NotBeNull();
        result.InspectionId.Should().NotBeEmpty();
        result.Device.Should().Be(ExecutionDevice.Cpu);
    }

    [Fact]
    public void DetectFromBytes_WithEncodedImageBytes_ExecutesPipelineSuccessfully()
    {
        // Arrange
        string imagePath = ResolveTestImagePath("BMFO-1.jpg");
        byte[] bytes = File.ReadAllBytes(imagePath);

        // Act
        var result = _detector.DetectFromBytes(bytes);

        // Assert
        result.Should().NotBeNull();
        result.InspectionId.Should().NotBeEmpty();
    }

    [Fact]
    public void DetectFromFile_WhenFileNotFound_ThrowsFileInspectionException()
    {
        // Arrange
        string nonExistentPath = Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}.jpg");

        // Act
        Action act = () => _detector.DetectFromFile(nonExistentPath);

        // Assert
        act.Should().Throw<FileInspectionException>()
            .WithMessage("*does not exist*");
    }

    [Fact]
    public void Detect_WhenEmptyMat_ThrowsImageProcessingException()
    {
        // Arrange
        using var emptyMat = new Mat();

        // Act
        Action act = () => _detector.Detect(emptyMat);

        // Assert
        act.Should().Throw<ImageProcessingException>()
            .WithMessage("*empty*");
    }

    [Fact]
    public void Constructor_WhenNullDependencies_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new PcbDefectDetector(null!, _preprocessor, _postprocessor));
        Assert.Throws<ArgumentNullException>(() => new PcbDefectDetector(_runner, null!, _postprocessor));
        Assert.Throws<ArgumentNullException>(() => new PcbDefectDetector(_runner, _preprocessor, null!));
    }

    public void Dispose()
    {
        _runner.Dispose();
    }
}
