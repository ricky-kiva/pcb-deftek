using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using pcb_deftek.Infrastructure.Inference;
using Xunit;

namespace pcb_deftek.Tests;

/// <summary>
/// Hardware-gated proof that the DirectML execution provider initializes. Excluded from CI
/// (no GPU on hosted runners) via a fully-qualified-name filter in the workflow.
/// </summary>
[Trait("Category", "Hardware")]
public class DirectMlInferenceTests
{
    [Fact]
    public void DirectMl_WhenRequested_InitializesGpuAndRunsInference()
    {
        // Arrange
        using var runner = new OnnxModelRunner(new InferenceOptions
        {
            ModelPath = ResolveBestOnnxPath(),
            PreferredDevice = ExecutionDevice.GpuDirectML
        });

        // Assert the provider, not just that a session opened.
        runner.ActiveDevice.Should().Be(
            ExecutionDevice.GpuDirectML,
            $"DirectML should initialize on a DirectX 12 GPU, but the runner reported {runner.ActiveDevice}. " +
            "A CPU result means DirectML initialization failed and the runner fell back (check the GPU driver " +
            "and the onnxruntime warning in the log).");

        // One forward pass so a loaded-but-broken provider cannot pass.
        var tensor = new DenseTensor<float>(
            new float[1 * 3 * 640 * 640],
            new[] { 1, 3, 640, 640 });
        using var outputs = runner.Run(
            new[] { NamedOnnxValue.CreateFromTensor("images", tensor) });

        var output = outputs.FirstOrDefault(o => o.Name == "output0");
        output.Should().NotBeNull();
        output!.AsTensor<float>().Dimensions.ToArray().Should().Equal(1, 13, 8400);
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

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        throw new FileNotFoundException("best.onnx could not be found for testing.");
    }
}
