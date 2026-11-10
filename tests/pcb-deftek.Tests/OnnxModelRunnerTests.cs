using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using pcb_deftek.Common.Exceptions;
using pcb_deftek.Infrastructure.Inference;
using Xunit;

namespace pcb_deftek.Tests;

public class OnnxModelRunnerTests
{
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

        throw new FileNotFoundException("best.onnx could not be found for testing.");
    }

    [Fact]
    public void Constructor_WhenDefaultOptions_LoadsBestOnnxAndReflectsTensorMetadata()
    {
        // Act
        using var runner = new OnnxModelRunner(new InferenceOptions
        {
            ModelPath = ResolveBestOnnxPath()
        });

        // Assert
        runner.Metadata.Should().NotBeNull();
        runner.Metadata.InputName.Should().Be("images");
        runner.Metadata.InputShape.Should().Equal(1, 3, 640, 640);
        runner.Metadata.OutputName.Should().Be("output0");
        runner.Metadata.OutputShape.Should().Equal(1, 13, 8400);
        runner.ActiveDevice.Should().BeOneOf(ExecutionDevice.GpuDirectML, ExecutionDevice.Cpu);
    }

    [Fact]
    public void Constructor_WhenCpuPreferred_InitializesWithCpuDevice()
    {
        // Act
        using var runner = new OnnxModelRunner(new InferenceOptions
        {
            PreferredDevice = ExecutionDevice.Cpu,
            ModelPath = ResolveBestOnnxPath()
        });

        // Assert
        runner.ActiveDevice.Should().Be(ExecutionDevice.Cpu);
        runner.Metadata.ActiveDevice.Should().Be(ExecutionDevice.Cpu);
    }

    [Fact]
    public void Constructor_WhenInvalidModelPath_ThrowsModelLoadingException()
    {
        // Arrange
        var options = new InferenceOptions
        {
            ModelPath = Path.Combine(Path.GetTempPath(), $"non_existent_{Guid.NewGuid():N}.onnx")
        };

        // Act
        Action act = () => _ = new OnnxModelRunner(options);

        // Assert
        act.Should().Throw<ModelLoadingException>()
            .WithMessage("*does not exist*");
    }

    [Fact]
    public void WarmUp_WhenCalled_RunsDummyInputAndSetsIsWarmedUpTrue()
    {
        // Arrange
        using var runner = new OnnxModelRunner(new InferenceOptions
        {
            PreferredDevice = ExecutionDevice.Cpu,
            ModelPath = ResolveBestOnnxPath()
        });

        runner.Metadata.IsWarmedUp.Should().BeFalse();

        // Act
        runner.WarmUp();

        // Assert
        runner.Metadata.IsWarmedUp.Should().BeTrue();
    }

    [Fact]
    public void Run_WhenDummyTensorProvided_ExecutesForwardPassAndReturnsOutput0()
    {
        // Arrange
        using var runner = new OnnxModelRunner(new InferenceOptions
        {
            PreferredDevice = ExecutionDevice.Cpu,
            ModelPath = ResolveBestOnnxPath()
        });

        var dummyInput = new float[1 * 3 * 640 * 640];
        var tensor = new DenseTensor<float>(dummyInput, new[] { 1, 3, 640, 640 });
        var namedInput = NamedOnnxValue.CreateFromTensor("images", tensor);

        // Act
        using var outputs = runner.Run(new[] { namedInput });

        // Assert
        outputs.Should().NotBeNull();
        var outputTensor = outputs.FirstOrDefault(o => o.Name == "output0");
        outputTensor.Should().NotBeNull();

        var dense = outputTensor!.AsTensor<float>();
        dense.Dimensions.ToArray().Should().Equal(1, 13, 8400);
    }

    [Fact]
    public async Task RunAsync_WhenCalled_ExecutesOffCallingThreadAndReturnsOutputs()
    {
        // Arrange
        using var runner = new OnnxModelRunner(new InferenceOptions
        {
            PreferredDevice = ExecutionDevice.Cpu,
            ModelPath = ResolveBestOnnxPath()
        });

        var dummyInput = new float[1 * 3 * 640 * 640];
        var tensor = new DenseTensor<float>(dummyInput, new[] { 1, 3, 640, 640 });
        var namedInput = NamedOnnxValue.CreateFromTensor("images", tensor);

        // Act
        using var outputs = await runner.RunAsync(new[] { namedInput });

        // Assert
        outputs.Should().NotBeNull();
        var output0 = outputs.FirstOrDefault(o => o.Name == "output0");
        output0.Should().NotBeNull();
        output0!.AsTensor<float>().Dimensions.ToArray().Should().Equal(1, 13, 8400);
    }

    [Fact]
    public void Dispose_WhenCalled_SubsequentRunThrowsObjectDisposedException()
    {
        // Arrange
        var runner = new OnnxModelRunner(new InferenceOptions
        {
            PreferredDevice = ExecutionDevice.Cpu,
            ModelPath = ResolveBestOnnxPath()
        });

        var tensor = new DenseTensor<float>(new float[1 * 3 * 640 * 640], new[] { 1, 3, 640, 640 });
        var namedInput = NamedOnnxValue.CreateFromTensor("images", tensor);

        // Act
        runner.Dispose();

        // Assert
        Action act = () => runner.Run(new[] { namedInput });
        act.Should().Throw<ObjectDisposedException>();
    }
}
