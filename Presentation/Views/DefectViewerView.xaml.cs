using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using pcb_deftek.Domain.Models;
using pcb_deftek.Presentation.ViewModels;

namespace pcb_deftek.Presentation.Views;

public partial class DefectViewerView : UserControl
{
    private NormalizedRect _visibleRegion = NormalizedRect.Empty;

    public DefectViewerView()
    {
        InitializeComponent();
    }

    private InspectionViewerViewModel? ViewModel => DataContext as InspectionViewerViewModel;

    private void OnViewerLoaded(object sender, RoutedEventArgs e)
    {
        _visibleRegion = ImageCanvas.VisibleRegion;
    }

    private void OnViewerIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            Dispatcher.BeginInvoke(
                new Action(() => CloseButton.Focus()),
                System.Windows.Threading.DispatcherPriority.Input);
        }
    }

    private void OnViewerKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || ViewModel is not { } viewModel)
        {
            return;
        }

        if (viewModel.IsEditingName)
        {
            viewModel.CancelRenameCommand.Execute(null);
        }
        else
        {
            viewModel.CloseCommand.Execute(null);
        }

        e.Handled = true;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.CloseCommand.Execute(null);
    }

    private void OnMarkerSelected(object? sender, DefectMarkerViewModel? marker)
    {
        ViewModel?.SelectMarker(marker);
    }

    private void OnViewportChanged(object? sender, NormalizedRect visibleRegion)
    {
        _visibleRegion = visibleRegion;
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        ImageCanvas.ResetView();
    }

    private async void OnCropClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        NormalizedRect region = _visibleRegion.IsEmpty
            ? ImageCanvas.VisibleRegion
            : _visibleRegion;

        if (!region.IsEmpty)
        {
            await viewModel.CropViewportCommand.ExecuteAsync(region);
        }
    }

    private void OnNameGotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        ViewModel?.BeginRenameCommand.Execute(null);
    }

    private void OnNameLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (ViewModel is { IsEditingName: true, IsSavingName: false } viewModel)
        {
            _ = viewModel.CommitRenameCommand.ExecuteAsync(null);
        }
    }

    private void OnNameKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel is not { } viewModel || sender is not TextBox)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                _ = viewModel.CommitRenameCommand.ExecuteAsync(null);
                break;

            case Key.Escape:
                e.Handled = true;
                viewModel.CancelRenameCommand.Execute(null);
                break;
        }
    }
}
