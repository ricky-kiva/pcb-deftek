using System.Collections.Generic;

namespace pcb_deftek.Infrastructure.Inference;

/// <summary>
/// Diagnostic information describing the loaded ONNX model graph, active execution provider, and tensor specifications.
/// </summary>
public record ModelMetadataInfo(
    string ModelPath,
    ExecutionDevice ActiveDevice,
    string InputName,
    IReadOnlyList<int> InputShape,
    string OutputName,
    IReadOnlyList<int> OutputShape,
    bool IsWarmedUp);
