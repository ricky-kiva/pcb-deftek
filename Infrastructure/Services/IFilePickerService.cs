using System.Collections.Generic;
using System.Threading.Tasks;

namespace pcb_deftek.Infrastructure.Services;

/// <summary>
/// Service contract for prompting the operator with native Windows file and folder selection dialogs.
/// Abstracted behind an interface to decouple ViewModels from WPF UI dialog classes and enable testing.
/// </summary>
public interface IFilePickerService
{
    /// <summary>
    /// Default file filter string for supported PCB images.
    /// </summary>
    public const string DefaultImageFilter = "PCB Images (*.jpg;*.jpeg;*.png;*.bmp;*.webp)|*.jpg;*.jpeg;*.png;*.bmp;*.webp|All Files (*.*)|*.*";

    /// <summary>
    /// Displays a file picker dialog prompting the operator to select one or more files.
    /// </summary>
    /// <param name="title">Optional dialog title.</param>
    /// <param name="filter">Optional file filter; defaults to <see cref="DefaultImageFilter"/> if null or empty.</param>
    /// <param name="multiselect">Whether multiple files can be selected simultaneously; defaults to true.</param>
    /// <returns>A read-only list of chosen absolute file paths, or empty if cancelled.</returns>
    Task<IReadOnlyList<string>> PickFilesAsync(string? title = null, string? filter = null, bool multiselect = true);

    /// <summary>
    /// Displays a folder picker dialog prompting the operator to select a directory.
    /// </summary>
    /// <param name="title">Optional dialog title.</param>
    /// <returns>The selected absolute folder path, or null if cancelled.</returns>
    Task<string?> PickFolderAsync(string? title = null);
}
