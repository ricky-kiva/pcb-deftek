using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using OpenCvSharp;
using pcb_deftek.Common.Exceptions;
using pcb_deftek.Infrastructure.Vision;
using Xunit;

namespace pcb_deftek.Tests;

public class OpenCvVisionPreprocessorTests
{
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
    public void Preprocess_WhenLandscapeImage_LetterboxesWithTopBottomPadding()
    {
        // Arrange: 1000x500 landscape image
        using var mat = new Mat(500, 1000, MatType.CV_8UC3, new Scalar(100, 150, 200));
        var preprocessor = new OpenCvVisionPreprocessor(640, 640);

        // Act
        using var result = preprocessor.Preprocess(mat);

        // Assert
        result.Tensor.Dimensions.ToArray().Should().Equal(1, 3, 640, 640);
        result.Transform.OriginalWidth.Should().Be(1000);
        result.Transform.OriginalHeight.Should().Be(500);
        result.Transform.Scale.Should().Be(0.64f);
        result.Transform.PadX.Should().Be(0);
        result.Transform.PadY.Should().Be(160); // (640 - 500 * 0.64) / 2 = 160
    }

    [Fact]
    public void Preprocess_WhenPortraitImage_LetterboxesWithLeftRightPadding()
    {
        // Arrange: 500x1000 portrait image
        using var mat = new Mat(1000, 500, MatType.CV_8UC3, new Scalar(100, 150, 200));
        var preprocessor = new OpenCvVisionPreprocessor(640, 640);

        // Act
        using var result = preprocessor.Preprocess(mat);

        // Assert
        result.Tensor.Dimensions.ToArray().Should().Equal(1, 3, 640, 640);
        result.Transform.OriginalWidth.Should().Be(500);
        result.Transform.OriginalHeight.Should().Be(1000);
        result.Transform.Scale.Should().Be(0.64f);
        result.Transform.PadX.Should().Be(160); // (640 - 500 * 0.64) / 2 = 160
        result.Transform.PadY.Should().Be(0);
    }

    [Fact]
    public void Preprocess_WhenSquareImage_ScalesUniformlyWithoutPadding()
    {
        // Arrange: 800x800 square image
        using var mat = new Mat(800, 800, MatType.CV_8UC3, new Scalar(100, 150, 200));
        var preprocessor = new OpenCvVisionPreprocessor(640, 640);

        // Act
        using var result = preprocessor.Preprocess(mat);

        // Assert
        result.Tensor.Dimensions.ToArray().Should().Equal(1, 3, 640, 640);
        result.Transform.PadX.Should().Be(0);
        result.Transform.PadY.Should().Be(0);
        result.Transform.Scale.Should().Be(0.8f);
    }

    [Fact]
    public void Preprocess_VerifiesRgbChannelSeparationAndNormalization()
    {
        // Arrange: Pure Red in BGR space is (0, 0, 255)
        using var redMat = new Mat(640, 640, MatType.CV_8UC3, new Scalar(0, 0, 255));
        var preprocessor = new OpenCvVisionPreprocessor(640, 640);

        // Act
        using var result = preprocessor.Preprocess(redMat);
        var tensor = result.Tensor;

        // Assert: RGB channel layout [1, 3, 640, 640]
        // Channel 0 = Red (~1.0f)
        // Channel 1 = Green (0.0f)
        // Channel 2 = Blue (0.0f)
        tensor[0, 0, 320, 320].Should().BeApproximately(1.0f, 0.01f);
        tensor[0, 1, 320, 320].Should().BeApproximately(0.0f, 0.01f);
        tensor[0, 2, 320, 320].Should().BeApproximately(0.0f, 0.01f);
    }

    [Fact]
    public void Preprocess_WhenGrayscaleProvided_ConvertsTo3ChannelRgbTensor()
    {
        // Arrange: Grayscale image
        using var grayMat = new Mat(300, 300, MatType.CV_8UC1, new Scalar(200));
        var preprocessor = new OpenCvVisionPreprocessor(640, 640);

        // Act
        using var result = preprocessor.Preprocess(grayMat);

        // Assert
        result.Tensor.Dimensions.ToArray().Should().Equal(1, 3, 640, 640);
        result.Tensor[0, 0, 320, 320].Should().BeApproximately(200f / 255f, 0.01f);
        result.Tensor[0, 1, 320, 320].Should().BeApproximately(200f / 255f, 0.01f);
        result.Tensor[0, 2, 320, 320].Should().BeApproximately(200f / 255f, 0.01f);
    }

    [Fact]
    public void PreprocessFromFile_WithRealFixture_DecodesAndNormalizesSuccessfully()
    {
        // Arrange: Use test fixture image
        string path = ResolveTestImagePath("OP-1.jpg");
        var preprocessor = new OpenCvVisionPreprocessor(640, 640);

        // Act
        using var result = preprocessor.PreprocessFromFile(path, retainLetterboxMat: true);

        // Assert
        result.Tensor.Dimensions.ToArray().Should().Equal(1, 3, 640, 640);
        result.LetterboxedMat.Should().NotBeNull();
        result.LetterboxedMat!.Width.Should().Be(640);
        result.LetterboxedMat.Height.Should().Be(640);

        // Verify float values are strictly within [0.0, 1.0]
        var tensor = result.Tensor;
        for (int c = 0; c < 3; c++)
        {
            float val = tensor[0, c, 320, 320];
            val.Should().BeInRange(0f, 1f);
        }
    }

    [Fact]
    public void PreprocessFromFile_WhenFileNotFound_ThrowsFileInspectionException()
    {
        // Arrange
        var preprocessor = new OpenCvVisionPreprocessor(640, 640);
        string missingPath = Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}.png");

        // Act
        Action act = () => preprocessor.PreprocessFromFile(missingPath);

        // Assert
        act.Should().Throw<FileInspectionException>()
            .WithMessage("*does not exist*");
    }

    [Fact]
    public void PreprocessFromBytes_WithValidEncodedBytes_DecodesAndProducesTensor()
    {
        // Arrange
        string path = ResolveTestImagePath("SH-1.jpg");
        byte[] bytes = File.ReadAllBytes(path);
        var preprocessor = new OpenCvVisionPreprocessor(640, 640);

        // Act
        using var result = preprocessor.PreprocessFromBytes(bytes);

        // Assert
        result.Tensor.Dimensions.ToArray().Should().Equal(1, 3, 640, 640);
        result.Transform.OriginalWidth.Should().BeGreaterThan(0);
        result.Transform.OriginalHeight.Should().BeGreaterThan(0);
    }

    [Fact]
    public void PreprocessFromBytes_WhenEmpty_ThrowsArgumentException()
    {
        // Arrange
        var preprocessor = new OpenCvVisionPreprocessor(640, 640);

        // Act
        Action act = () => preprocessor.PreprocessFromBytes(ReadOnlySpan<byte>.Empty);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*cannot be empty*");
    }

    [Fact]
    public void LetterboxTransform_InvertBoxToNormalized_MapsAccuratelyAndClamps()
    {
        // Arrange: 1000x500 image scaled by 0.64 to (640, 320), padX=0, padY=160
        var transform = new LetterboxTransform(1000, 500, 640, 640, 0.64f, 0f, 160f);

        // Box at (100, 200, 200, 100) in letterbox space
        // Unpadded Y1 = 200 - 160 = 40 => origY1 = 40 / 0.64 = 62.5
        // Unpadded X1 = 100 => origX1 = 100 / 0.64 = 156.25
        // Act
        var (normX, normY, normW, normH) = transform.InvertBoxToNormalized(100f, 200f, 200f, 100f);

        // Assert
        normX.Should().BeApproximately(156.25f / 1000f, 0.01f);
        normY.Should().BeApproximately(62.5f / 500f, 0.01f);
        normW.Should().BeApproximately((200f / 0.64f) / 1000f, 0.01f);
        normH.Should().BeApproximately((100f / 0.64f) / 500f, 0.01f);

        // Coordinates must be strictly in [0.0, 1.0]
        normX.Should().BeInRange(0f, 1f);
        normY.Should().BeInRange(0f, 1f);
        (normX + normW).Should().BeLessThanOrEqualTo(1.0001f);
        (normY + normH).Should().BeLessThanOrEqualTo(1.0001f);
    }
}
