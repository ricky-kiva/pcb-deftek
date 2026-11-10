using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;

namespace pcb_deftek.Infrastructure.Inference;

/// <summary>
/// High-performance contract for managing ONNX Runtime inference sessions, hardware execution providers, and tensor execution.
/// </summary>
public interface IOnnxModelRunner : IDisposable
{
    /// <summary>
    /// Gets the actively negotiated execution device (e.g. <see cref="ExecutionDevice.Cpu"/> or <see cref="ExecutionDevice.GpuDirectML"/>).
    /// </summary>
    ExecutionDevice ActiveDevice { get; }

    /// <summary>
    /// Gets metadata describing the loaded model inputs, outputs, tensor shapes, and warm-up state.
    /// </summary>
    ModelMetadataInfo Metadata { get; }

    /// <summary>
    /// Runs a synchronous inference pass on the provided input tensors.
    /// </summary>
    /// <param name="inputs">The collection of named input tensors.</param>
    /// <returns>The disposable model output collection.</returns>
    IDisposableReadOnlyCollection<DisposableNamedOnnxValue> Run(IReadOnlyCollection<NamedOnnxValue> inputs);

    /// <summary>
    /// Executes an asynchronous model inference pass off the calling thread.
    /// </summary>
    /// <param name="inputs">The collection of named input tensors.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The disposable model output collection.</returns>
    Task<IDisposableReadOnlyCollection<DisposableNamedOnnxValue>> RunAsync(
        IReadOnlyCollection<NamedOnnxValue> inputs,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a warm-up inference pass with a dummy zero-tensor to pre-compile execution kernels and shaders.
    /// </summary>
    void WarmUp();
}
