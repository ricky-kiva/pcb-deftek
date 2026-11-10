using System;
using System.Linq;
using FluentAssertions;
using Microsoft.ML.OnnxRuntime.Tensors;
using pcb_deftek.Common.Exceptions;
using pcb_deftek.Domain.Models;
using pcb_deftek.Infrastructure.Inference;
using pcb_deftek.Infrastructure.Vision;
using Xunit;

namespace pcb_deftek.Tests;

public class YoloPostprocessorTests
{
    private static LetterboxTransform CreateDefaultTransform(int w = 640, int h = 640)
    {
        return new LetterboxTransform(w, h, 640, 640, 1.0f, 0f, 0f);
    }

    [Fact]
    public void Postprocess_WhenNoCandidatesAboveThreshold_ReturnsEmptyList()
    {
        // Arrange: [1, 13, 10] with all scores < 0.25
        var tensor = new DenseTensor<float>(new[] { 1, 13, 10 });
        for (int i = 0; i < 10; i++)
        {
            tensor[0, 0, i] = 100; // cx
            tensor[0, 1, i] = 100; // cy
            tensor[0, 2, i] = 50;  // w
            tensor[0, 3, i] = 50;  // h
            for (int c = 0; c < 9; c++)
            {
                tensor[0, 4 + c, i] = 0.10f; // Low score
            }
        }

        var postprocessor = new YoloPostprocessor();

        // Act
        var defects = postprocessor.Postprocess(tensor, CreateDefaultTransform(), Guid.NewGuid());

        // Assert
        defects.Should().BeEmpty();
    }

    [Fact]
    public void Postprocess_WhenSingleCandidateAboveThreshold_ExtractsNormalizedDefect()
    {
        // Arrange: 1 candidate for class 0 (Short), score 0.85
        var tensor = new DenseTensor<float>(new[] { 1, 13, 1 });
        tensor[0, 0, 0] = 320; // cx
        tensor[0, 1, 0] = 320; // cy
        tensor[0, 2, 0] = 100; // w
        tensor[0, 3, 0] = 80;  // h
        tensor[0, 4, 0] = 0.85f; // class 0 (Short)

        var postprocessor = new YoloPostprocessor();
        var inspectionId = Guid.NewGuid();

        // Act
        var defects = postprocessor.Postprocess(tensor, CreateDefaultTransform(), inspectionId);

        // Assert
        defects.Should().HaveCount(1);
        var defect = defects[0];
        defect.InspectionId.Should().Be(inspectionId);
        defect.Class.Should().Be(DefectClass.Short);
        defect.ClassCode.Should().Be("short");
        defect.ClassName.Should().Be("Short");
        defect.Confidence.Should().BeApproximately(0.85f, 0.001f);

        // Normalized box: cx=320, cy=320, w=100, h=80 => x=270, y=280
        defect.BoundingBox.X.Should().BeApproximately(270f / 640f, 0.01f);
        defect.BoundingBox.Y.Should().BeApproximately(280f / 640f, 0.01f);
        defect.BoundingBox.Width.Should().BeApproximately(100f / 640f, 0.01f);
        defect.BoundingBox.Height.Should().BeApproximately(80f / 640f, 0.01f);
    }

    [Fact]
    public void Postprocess_WhenOverlappingCandidatesSameClass_SuppressesDuplicateViaNms()
    {
        // Arrange: 2 overlapping candidates for class 1 (Spur), IoU > 0.7
        var tensor = new DenseTensor<float>(new[] { 1, 13, 2 });
        // Box A: score 0.92
        tensor[0, 0, 0] = 200;
        tensor[0, 1, 0] = 200;
        tensor[0, 2, 0] = 60;
        tensor[0, 3, 0] = 60;
        tensor[0, 4 + 1, 0] = 0.92f;

        // Box B: score 0.81 (overlap with A)
        tensor[0, 0, 1] = 205;
        tensor[0, 1, 1] = 205;
        tensor[0, 2, 1] = 60;
        tensor[0, 3, 1] = 60;
        tensor[0, 4 + 1, 1] = 0.81f;

        var postprocessor = new YoloPostprocessor();

        // Act
        var defects = postprocessor.Postprocess(tensor, CreateDefaultTransform(), Guid.NewGuid(), nmsIouThreshold: 0.45f);

        // Assert
        defects.Should().HaveCount(1, "NMS must suppress candidate B having lower confidence and IoU > 0.45");
        defects[0].Confidence.Should().BeApproximately(0.92f, 0.001f);
    }

    [Fact]
    public void Postprocess_WhenOverlappingCandidatesDifferentClasses_PreservesBothDetections()
    {
        // Arrange: Box A (class 0: Short), Box B (class 3: Open) at identical coordinates
        var tensor = new DenseTensor<float>(new[] { 1, 13, 2 });
        tensor[0, 0, 0] = 200;
        tensor[0, 1, 0] = 200;
        tensor[0, 2, 0] = 60;
        tensor[0, 3, 0] = 60;
        tensor[0, 4 + 0, 0] = 0.90f; // Class 0 (Short)

        tensor[0, 0, 1] = 200;
        tensor[0, 1, 1] = 200;
        tensor[0, 2, 1] = 60;
        tensor[0, 3, 1] = 60;
        tensor[0, 4 + 3, 1] = 0.88f; // Class 3 (Open)

        var postprocessor = new YoloPostprocessor();

        // Act
        var defects = postprocessor.Postprocess(tensor, CreateDefaultTransform(), Guid.NewGuid());

        // Assert
        defects.Should().HaveCount(2, "Class-specific NMS preserves distinct defect classes even if bounding boxes overlap");
        defects.Select(d => d.Class).Should().Contain(new[] { DefectClass.Short, DefectClass.Open });
    }

    [Fact]
    public void Postprocess_WhenLetterboxed_ProjectsCoordinatesBackToOriginalImageSpace()
    {
        // Arrange: 1000x500 image letterboxed with scale=0.64, padX=0, padY=160
        var transform = new LetterboxTransform(1000, 500, 640, 640, 0.64f, 0f, 160f);

        // Candidate box in letterbox space: center=(320, 260), size=(128, 64)
        // x1 = 320 - 64 = 256 => origX1 = 256 / 0.64 = 400
        // y1 = 260 - 32 = 228 => unpadY1 = 228 - 160 = 68 => origY1 = 68 / 0.64 = 106.25
        var tensor = new DenseTensor<float>(new[] { 1, 13, 1 });
        tensor[0, 0, 0] = 320;
        tensor[0, 1, 0] = 260;
        tensor[0, 2, 0] = 128;
        tensor[0, 3, 0] = 64;
        tensor[0, 4 + 2, 0] = 0.95f; // Spurious copper

        var postprocessor = new YoloPostprocessor();

        // Act
        var defects = postprocessor.Postprocess(tensor, transform, Guid.NewGuid());

        // Assert
        defects.Should().HaveCount(1);
        var defect = defects[0];
        defect.Class.Should().Be(DefectClass.SpuriousCopper);
        defect.BoundingBox.X.Should().BeApproximately(400f / 1000f, 0.01f);
        defect.BoundingBox.Y.Should().BeApproximately(106.25f / 500f, 0.01f);
        defect.BoundingBox.Width.Should().BeApproximately((128f / 0.64f) / 1000f, 0.01f);
        defect.BoundingBox.Height.Should().BeApproximately((64f / 0.64f) / 500f, 0.01f);
    }

    [Fact]
    public void Postprocess_ValidatesAll9DefectClassesMapping()
    {
        // Arrange: 9 candidates, one for each defect class
        var tensor = new DenseTensor<float>(new[] { 1, 13, 9 });
        for (int c = 0; c < 9; c++)
        {
            tensor[0, 0, c] = 50 + (c * 60);
            tensor[0, 1, c] = 50;
            tensor[0, 2, c] = 40;
            tensor[0, 3, c] = 40;
            tensor[0, 4 + c, c] = 0.90f; // High score for class c
        }

        var postprocessor = new YoloPostprocessor();

        // Act
        var defects = postprocessor.Postprocess(tensor, CreateDefaultTransform(), Guid.NewGuid());

        // Assert
        defects.Should().HaveCount(9);
        var expectedEnums = Enum.GetValues<DefectClass>();
        foreach (var expectedEnum in expectedEnums)
        {
            defects.Should().Contain(d => d.Class == expectedEnum, $"DefectClass.{expectedEnum} must be parsed correctly");
        }
    }

    [Fact]
    public void Postprocess_WhenEmptyInspectionId_ThrowsValidationException()
    {
        // Arrange
        var tensor = new DenseTensor<float>(new[] { 1, 13, 1 });
        var postprocessor = new YoloPostprocessor();

        // Act
        Action act = () => postprocessor.Postprocess(tensor, CreateDefaultTransform(), Guid.Empty);

        // Assert
        act.Should().Throw<ValidationException>()
            .WithMessage("*cannot be empty*");
    }

    [Fact]
    public void ExtractDetections_WhenTransposedTensorProvided_ParsesCorrectly()
    {
        // Arrange: Transposed tensor [1, 5, 13] (Candidates=5, Attributes=13)
        var tensor = new DenseTensor<float>(new[] { 1, 5, 13 });
        tensor[0, 0, 0] = 300; // cx
        tensor[0, 0, 1] = 300; // cy
        tensor[0, 0, 2] = 50;  // w
        tensor[0, 0, 3] = 50;  // h
        tensor[0, 0, 4 + 4] = 0.88f; // Mouse bite (class 4)

        var postprocessor = new YoloPostprocessor();

        // Act
        var detections = postprocessor.ExtractDetections(tensor, CreateDefaultTransform());

        // Assert
        detections.Should().HaveCount(1);
        detections[0].Class.Should().Be(DefectClass.MouseBite);
        detections[0].Confidence.Should().BeApproximately(0.88f, 0.001f);
    }

    [Fact]
    public void Postprocess_WhenTensorShapeInvalid_ThrowsModelExecutionException()
    {
        // Arrange: Invalid shape [1, 5, 100] (neither dimension is 13)
        var tensor = new DenseTensor<float>(new[] { 1, 5, 100 });
        var postprocessor = new YoloPostprocessor();

        // Act
        Action act = () => postprocessor.Postprocess(tensor, CreateDefaultTransform(), Guid.NewGuid());

        // Assert
        act.Should().Throw<ModelExecutionException>()
            .WithMessage("*Unsupported YOLOv8 tensor dimensions*");
    }
}
