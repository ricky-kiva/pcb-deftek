using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using pcb_deftek.Common.Exceptions;
using Serilog;

namespace pcb_deftek.Infrastructure.Inference;

/// <summary>
/// Thread-safe ONNX Runtime model runner with automatic DirectML GPU acceleration and CPU fallback.
/// </summary>
public class OnnxModelRunner : IOnnxModelRunner
{
    private readonly InferenceSession _session;
    private readonly string _modelPath;
    private readonly string _inputName;
    private readonly int[] _inputShape;
    private readonly string _outputName;
    private readonly int[] _outputShape;
    private readonly object _lock = new();
    private bool _isWarmedUp;
    private bool _isDisposed;

    /// <inheritdoc />
    public ExecutionDevice ActiveDevice { get; }

    /// <inheritdoc />
    public ModelMetadataInfo Metadata => new(
        ModelPath: _modelPath,
        ActiveDevice: ActiveDevice,
        InputName: _inputName,
        InputShape: _inputShape,
        OutputName: _outputName,
        OutputShape: _outputShape,
        IsWarmedUp: _isWarmedUp);

    /// <summary>
    /// Initializes a new instance of <see cref="OnnxModelRunner"/> using the provided or default inference options.
    /// </summary>
    /// <param name="options">Optional inference configuration options.</param>
    public OnnxModelRunner(InferenceOptions? options = null)
    {
        var config = options ?? new InferenceOptions();
        _modelPath = ResolveModelPath(config.ModelPath);

        Log.Information("Loading ONNX model from '{ModelPath}'...", _modelPath);

        try
        {
            if (config.PreferredDevice == ExecutionDevice.GpuDirectML)
            {
                var gpuOptions = CreateSessionOptions(config);
                try
                {
                    gpuOptions.AppendExecutionProvider_DML(config.DeviceId);
                    _session = new InferenceSession(_modelPath, gpuOptions);
                    ActiveDevice = ExecutionDevice.GpuDirectML;
                    Log.Information("ONNX Runtime initialized successfully with DirectML GPU provider (Device {DeviceId}).", config.DeviceId);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "DirectML GPU initialization failed on device {DeviceId}. Falling back to CPU execution provider.", config.DeviceId);
                    gpuOptions.Dispose();

                    var cpuOptions = CreateSessionOptions(config);
                    _session = new InferenceSession(_modelPath, cpuOptions);
                    ActiveDevice = ExecutionDevice.Cpu;
                    Log.Information("ONNX Runtime initialized successfully with CPU execution provider (fallback).");
                }
            }
            else
            {
                var cpuOptions = CreateSessionOptions(config);
                _session = new InferenceSession(_modelPath, cpuOptions);
                ActiveDevice = ExecutionDevice.Cpu;
                Log.Information("ONNX Runtime initialized with CPU execution provider.");
            }
        }
        catch (Exception ex) when (ex is not ModelLoadingException)
        {
            Log.Error(ex, "Failed to load ONNX model from '{ModelPath}': {Message}", _modelPath, ex.Message);
            throw new ModelLoadingException($"Failed to load ONNX model from '{_modelPath}'.", _modelPath, ex);
        }

        // Reflect tensor metadata
        var firstInput = _session.InputMetadata.FirstOrDefault();
        if (firstInput.Key == null)
        {
            throw new ModelLoadingException($"ONNX model '{_modelPath}' has no input tensors defined.", _modelPath);
        }

        _inputName = firstInput.Key;
        _inputShape = firstInput.Value.Dimensions.Select(d => d < 0 ? 1 : d).ToArray();

        var firstOutput = _session.OutputMetadata.FirstOrDefault();
        if (firstOutput.Key == null)
        {
            throw new ModelLoadingException($"ONNX model '{_modelPath}' has no output tensors defined.", _modelPath);
        }

        _outputName = firstOutput.Key;
        _outputShape = firstOutput.Value.Dimensions.Select(d => d < 0 ? 1 : d).ToArray();

        Log.Information("ONNX model loaded: Input '{InputName}' [{InputShape}], Output '{OutputName}' [{OutputShape}], Device: {Device}",
            _inputName,
            string.Join(", ", _inputShape),
            _outputName,
            string.Join(", ", _outputShape),
            ActiveDevice);
    }

    /// <inheritdoc />
    public IDisposableReadOnlyCollection<DisposableNamedOnnxValue> Run(IReadOnlyCollection<NamedOnnxValue> inputs)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ArgumentNullException.ThrowIfNull(inputs);

        try
        {
            return _session.Run(inputs);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "ONNX model forward pass failed: {Message}", ex.Message);
            throw new ModelExecutionException($"Model forward pass failed: {ex.Message}", "ForwardPass", ex);
        }
    }

    /// <inheritdoc />
    public Task<IDisposableReadOnlyCollection<DisposableNamedOnnxValue>> RunAsync(
        IReadOnlyCollection<NamedOnnxValue> inputs,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Run(inputs);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public void WarmUp()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        lock (_lock)
        {
            if (_isWarmedUp)
            {
                return;
            }

            Log.Information("Warming up ONNX model runner with dummy input...");

            try
            {
                int totalElements = _inputShape.Aggregate(1, (acc, val) => acc * Math.Max(1, val));
                var dummyBuffer = new float[totalElements];
                var tensor = new DenseTensor<float>(dummyBuffer, _inputShape);
                var input = NamedOnnxValue.CreateFromTensor(_inputName, tensor);

                using var outputs = _session.Run(new[] { input });
                _isWarmedUp = true;
                Log.Information("ONNX model runner warm-up completed successfully.");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Model warm-up encountered an exception: {Message}. Model will remain functional.", ex.Message);
            }
        }
    }

    private static SessionOptions CreateSessionOptions(InferenceOptions config)
    {
        var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
        };

        if (config.IntraOpNumThreads > 0)
        {
            options.IntraOpNumThreads = config.IntraOpNumThreads;
        }

        return options;
    }

    private static string ResolveModelPath(string? customPath)
    {
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            if (File.Exists(customPath))
            {
                return Path.GetFullPath(customPath);
            }
            throw new ModelLoadingException($"Specified ONNX model file does not exist: '{customPath}'", customPath);
        }

        // Candidate search paths
        string[] candidates =
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Models", "best.onnx"),
            Path.Combine(Directory.GetCurrentDirectory(), "Assets", "Models", "best.onnx"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "Assets", "Models", "best.onnx"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Assets", "Models", "best.onnx")
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        throw new ModelLoadingException(
            "Default ONNX model 'Assets/Models/best.onnx' could not be found in application base directory or current working directory.",
            candidates[0]);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _session.Dispose();
        GC.SuppressFinalize(this);
    }
}
