using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace pcb_deftek.Presentation.Models;

/// <summary>
/// Selectable option used by multi-select filter bars.
/// </summary>
/// <typeparam name="T">Filtered value type.</typeparam>
public sealed partial class FilterOption<T> : ObservableObject
{
    /// <summary>Gets the value represented by this option.</summary>
    public T Value { get; }

    /// <summary>Gets the display label.</summary>
    public string Label { get; }

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// Initializes a new instance of the <see cref="FilterOption{T}"/> class.
    /// </summary>
    public FilterOption(T value, string label)
    {
        Value = value;
        Label = label ?? throw new ArgumentNullException(nameof(label));
    }
}
