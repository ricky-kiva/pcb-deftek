using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using pcb_deftek.Presentation.ViewModels;
using pcb_deftek.Presentation.Models;

namespace pcb_deftek.Presentation.Views;

public partial class HistoryView : UserControl
{
    private Border? _viewerOrigin;
    private HistoryViewModel? _subscribedViewModel;

    public HistoryView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private HistoryViewModel? ViewModel => DataContext as HistoryViewModel;

    private void OnDatePresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: string tag } ||
            !Enum.TryParse<DateRangePreset>(tag, out var preset) ||
            ViewModel is not { } viewModel)
        {
            return;
        }

        viewModel.Filters.SelectedPreset = viewModel.Filters.SelectedPreset == preset
            ? DateRangePreset.None
            : preset;

        if (preset != DateRangePreset.Custom)
        {
            CustomDatePopup.IsOpen = false;
        }
    }

    private void OnCustomDateClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        bool opening = !CustomDatePopup.IsOpen;
        CustomDatePopup.IsOpen = opening;

        if (opening && viewModel.Filters.SelectedPreset != DateRangePreset.Custom)
        {
            viewModel.Filters.SelectedPreset = DateRangePreset.Custom;
        }

        // Keep the chip active while the custom preset is applied, even when the flyout closes.
        CustomDateToggle.SetCurrentValue(
            ToggleButton.IsCheckedProperty,
            viewModel.Filters.SelectedPreset == DateRangePreset.Custom);
    }

    private void OnFilterChipClick(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(sender, StatusFilterToggle) || ReferenceEquals(sender, ClassFilterToggle))
        {
            CustomDatePopup.IsOpen = false;
        }
    }

    private void OnCustomRangeDoneClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.Filters.IsCustomRangeValid == true)
        {
            CustomDatePopup.IsOpen = false;
        }
    }

    private void OnCardMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { DataContext: HistoryItemViewModel item } || item.IsEditingName)
        {
            return;
        }

        if (e.OriginalSource is DependencyObject source && FindAncestor<CheckBox>(source) != null)
        {
            return;
        }

        ((Border)sender).Focus();
        _viewerOrigin = (Border)sender;
        if (ViewModel is { } viewModel)
        {
            _ = viewModel.OpenViewerAsync(item);
        }
    }

    private void OnCardKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not Border { DataContext: HistoryItemViewModel item } || item.IsEditingName)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
            case Key.Space:
                _viewerOrigin = sender as Border;
                if (ViewModel is { } viewModel)
                {
                    _ = viewModel.OpenViewerAsync(item);
                }

                e.Handled = true;
                break;

            case Key.F2:
                item.BeginRename();
                e.Handled = true;
                break;
        }
    }

    private void OnNameDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 ||
            sender is not FrameworkElement { DataContext: HistoryItemViewModel item })
        {
            return;
        }

        item.BeginRename();
        e.Handled = true;
    }

    private void OnRenameTextBoxVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && sender is TextBox textBox)
        {
            textBox.Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    textBox.Focus();
                    textBox.SelectAll();
                }),
                DispatcherPriority.Input);
        }
    }

    private void OnRenameKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: HistoryItemViewModel item })
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            _ = item.CommitRenameAsync();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            item.CancelRename();
        }
    }

    private void OnRenameTextBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: HistoryItemViewModel item } &&
            item.IsEditingName &&
            !item.IsSavingName)
        {
            _ = item.CommitRenameAsync();
        }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.Viewer.Closed -= OnViewerClosed;
        }

        _subscribedViewModel = e.NewValue as HistoryViewModel;

        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.Viewer.Closed += OnViewerClosed;
        }
    }

    private void OnViewerClosed(object? sender, EventArgs e)
    {
        if (_viewerOrigin is { IsLoaded: true, IsVisible: true } origin)
        {
            origin.Focus();
        }
        else
        {
            Focus();
        }
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match)
            {
                return match;
            }

            current = current is Visual
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return null;
    }
}
