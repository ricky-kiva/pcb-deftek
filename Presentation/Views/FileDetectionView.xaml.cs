using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using pcb_deftek.Presentation.ViewModels;
using pcb_deftek.Infrastructure.Services;

namespace pcb_deftek.Presentation.Views;

/// <summary>
/// Interaction logic for FileDetectionView.xaml.
/// Implements drag-and-drop feedback and forwards dropped file payloads to the ViewModel.
/// </summary>
public partial class FileDetectionView : UserControl
{
    public FileDetectionView()
    {
        InitializeComponent();
        WpfFilePickerService.OnDialogClosed = MarkDialogClosed;
    }

    private static DateTime _lastDialogClosedTimeUtc = DateTime.MinValue;

    /// <summary>
    /// Records the timestamp when an OS file or folder dialog has closed.
    /// Used to suppress leaked mouse messages from double-clicking inside native dialogs.
    /// </summary>
    public static void MarkDialogClosed()
    {
        _lastDialogClosedTimeUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Checks whether an OS dialog was closed within the suppression cooldown window.
    /// </summary>
    public static bool IsRecentDialogClose()
    {
        return (DateTime.UtcNow - _lastDialogClosedTimeUtc).TotalMilliseconds < 450;
    }

    private void OnBrowseButtonClicked(object sender, RoutedEventArgs e)
    {
        if (IsRecentDialogClose())
        {
            return;
        }

        if (sender is Button button && button.ContextMenu != null)
        {
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            button.ContextMenu.IsOpen = true;
        }
    }

    private void OnDropTargetDragEnter(object sender, DragEventArgs e)
    {
        UpdateDragHighlight(e, isHovering: true);
    }

    private void OnDropTargetDragOver(object sender, DragEventArgs e)
    {
        UpdateDragHighlight(e, isHovering: true);
    }

    private void OnDropTargetDragLeave(object sender, DragEventArgs e)
    {
        ResetDragHighlight();
    }

    private void OnDropTargetDrop(object sender, DragEventArgs e)
    {
        ResetDragHighlight();

        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is string[] droppedPaths &&
            DataContext is FileDetectionViewModel viewModel)
        {
            if (viewModel.DropFilesCommand.CanExecute(droppedPaths))
            {
                viewModel.DropFilesCommand.Execute(droppedPaths);
            }
        }

        e.Handled = true;
    }

    private void OnDropTargetMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.ClickCount > 1)
        {
            return;
        }

        if (IsRecentDialogClose())
        {
            e.Handled = true;
            return;
        }

        // Only trigger browse if user clicked the border background, not an inner child button
        if (e.OriginalSource is DependencyObject source)
        {
            var button = FindVisualParent<Button>(source);
            if (button != null)
            {
                return;
            }
        }

        if (DataContext is FileDetectionViewModel viewModel && viewModel.BrowseFilesCommand.CanExecute(null))
        {
            e.Handled = true;
            viewModel.BrowseFilesCommand.Execute(null);
        }
    }

    private void UpdateDragHighlight(DragEventArgs e, bool isHovering)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;

            if (isHovering && TryFindResource("AccentFillColorDefaultBrush") is Brush accentBrush)
            {
                DropTargetBorder.BorderBrush = accentBrush;
            }

            if (isHovering && TryFindResource("SubtleFillColorSecondaryBrush") is Brush hoverBg)
            {
                DropTargetBorder.Background = hoverBg;
            }
        }
        else
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            ResetDragHighlight();
        }
    }

    private void ResetDragHighlight()
    {
        if (TryFindResource("SurfaceStrokeColorDefaultBrush") is Brush strokeBrush)
        {
            DropTargetBorder.BorderBrush = strokeBrush;
        }
        else
        {
            DropTargetBorder.ClearValue(Border.BorderBrushProperty);
        }

        if (TryFindResource("CardBackgroundFillColorDefaultBrush") is Brush bgBrush)
        {
            DropTargetBorder.Background = bgBrush;
        }
        else
        {
            DropTargetBorder.ClearValue(Border.BackgroundProperty);
        }
    }

    private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
    {
        var parentObj = VisualTreeHelper.GetParent(child);
        if (parentObj == null)
        {
            return null;
        }

        if (parentObj is T parent)
        {
            return parent;
        }

        return FindVisualParent<T>(parentObj);
    }
}
