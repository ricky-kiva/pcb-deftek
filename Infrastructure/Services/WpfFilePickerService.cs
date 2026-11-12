using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace pcb_deftek.Infrastructure.Services;

/// <summary>
/// WPF implementation of <see cref="IFilePickerService"/> wrapping native Windows dialogs
/// (<see cref="OpenFileDialog"/> and <see cref="OpenFolderDialog"/>).
/// </summary>
public class WpfFilePickerService : IFilePickerService
{
    /// <summary>
    /// Optional global hook invoked whenever an OS file/folder dialog has closed.
    /// </summary>
    public static Action? OnDialogClosed { get; set; }

    private readonly Func<string?, string?, bool, IReadOnlyList<string>> _fileDialogHandler;
    private readonly Func<string?, string?> _folderDialogHandler;

    /// <summary>
    /// Initializes a new instance of the <see cref="WpfFilePickerService"/> class.
    /// </summary>
    /// <param name="fileDialogHandler">Optional custom file dialog handler for testing.</param>
    /// <param name="folderDialogHandler">Optional custom folder dialog handler for testing.</param>
    public WpfFilePickerService(
        Func<string?, string?, bool, IReadOnlyList<string>>? fileDialogHandler = null,
        Func<string?, string?>? folderDialogHandler = null)
    {
        _fileDialogHandler = fileDialogHandler ?? ShowDefaultFileDialog;
        _folderDialogHandler = folderDialogHandler ?? ShowDefaultFolderDialog;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> PickFilesAsync(string? title = null, string? filter = null, bool multiselect = true)
    {
        return RunOnDispatcher(() => _fileDialogHandler(title, filter, multiselect));
    }

    /// <inheritdoc />
    public Task<string?> PickFolderAsync(string? title = null)
    {
        return RunOnDispatcher(() => _folderDialogHandler(title));
    }

    private static Task<T> RunOnDispatcher<T>(Func<T> action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            return dispatcher.InvokeAsync(action).Task;
        }

        return Task.FromResult(action());
    }

    private static IReadOnlyList<string> ShowDefaultFileDialog(string? title, string? filter, bool multiselect)
    {
        var dialog = new OpenFileDialog
        {
            Title = title ?? "Select PCB Image Files",
            Filter = string.IsNullOrWhiteSpace(filter) ? IFilePickerService.DefaultImageFilter : filter,
            Multiselect = multiselect,
            CheckFileExists = true,
            CheckPathExists = true
        };

        try
        {
            var owner = Application.Current?.MainWindow;
            bool? result = owner != null ? dialog.ShowDialog(owner) : dialog.ShowDialog();

            if (result == true)
            {
                return dialog.FileNames.ToList();
            }

            return Array.Empty<string>();
        }
        finally
        {
            OnDialogClosed?.Invoke();
        }
    }

    private static string? ShowDefaultFolderDialog(string? title)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title ?? "Select PCB Image Folder",
            Multiselect = false
        };

        try
        {
            var owner = Application.Current?.MainWindow;
            bool? result = owner != null ? dialog.ShowDialog(owner) : dialog.ShowDialog();

            if (result == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
            {
                return dialog.FolderName;
            }

            return null;
        }
        finally
        {
            OnDialogClosed?.Invoke();
        }
    }
}
