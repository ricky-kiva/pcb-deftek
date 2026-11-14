using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using pcb_deftek.Presentation.Controls;
using pcb_deftek.Presentation.Models;
using pcb_deftek.Presentation.ViewModels;

namespace pcb_deftek.Presentation.Views;

public partial class DashboardView : UserControl
{
    private const string CardDragFormat = "pcb-deftek.dashboard-card";
    private const double DragThreshold = 6;

    private IInputElement? _focusBeforeChartModal;
    private IInputElement? _focusBeforeNotificationModal;
    private Point _dragStart;
    private DashboardMetricCardViewModel? _dragSource;

    public DashboardView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    internal DashboardChartHost ModalHost => ModalChartHost;

    internal ItemsControl CardsControl => MetricCardsControl;

    private DashboardViewModel? ViewModel => DataContext as DashboardViewModel;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        DetachViewModel(e.OldValue as DashboardViewModel);
        AttachViewModel(e.NewValue as DashboardViewModel);
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => AttachViewModel(ViewModel);

    private void OnUnloaded(object sender, RoutedEventArgs e) => DetachViewModel(ViewModel);

    private void AttachViewModel(DashboardViewModel? viewModel)
    {
        if (viewModel is null)
        {
            return;
        }

        viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void DetachViewModel(DashboardViewModel? viewModel)
    {
        if (viewModel is not null)
        {
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        if (e.PropertyName == nameof(DashboardViewModel.ActiveChartCard))
        {
            if (viewModel.IsChartModalOpen)
            {
                _focusBeforeChartModal ??= Keyboard.FocusedElement as IInputElement;
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => ChartModalCloseButton.Focus()));
            }
            else
            {
                RestoreFocus(_focusBeforeChartModal, ChartModalOverlay);
                _focusBeforeChartModal = null;
            }
        }
        else if (e.PropertyName == nameof(DashboardViewModel.IsNotificationModalOpen))
        {
            if (viewModel.IsNotificationModalOpen)
            {
                _focusBeforeNotificationModal ??= Keyboard.FocusedElement as IInputElement;
                Dispatcher.BeginInvoke(
                    DispatcherPriority.Input,
                    new Action(() => NotificationModalCloseButton.Focus()));
            }
            else
            {
                RestoreFocus(_focusBeforeNotificationModal, NotificationModalOverlay);
                _focusBeforeNotificationModal = null;
            }
        }
    }

    private void RestoreFocus(IInputElement? previousFocus, UIElement fallback)
    {
        Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() =>
            {
                if (previousFocus is not null && previousFocus is UIElement { IsVisible: true })
                {
                    Keyboard.Focus(previousFocus);
                }
                else
                {
                    fallback.Focus();
                }
            }));
    }

    private void OnDashboardPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || ViewModel is not { } viewModel)
        {
            return;
        }

        if (viewModel.IsNotificationModalOpen)
        {
            viewModel.CloseNotificationsCommand.Execute(null);
            e.Handled = true;
        }
        else if (viewModel.IsChartModalOpen)
        {
            viewModel.CloseChartCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnChartModalPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && ViewModel is { IsChartModalOpen: true } viewModel)
        {
            viewModel.CloseChartCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnNotificationModalPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && ViewModel is { IsNotificationModalOpen: true } viewModel)
        {
            viewModel.CloseNotificationsCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnChartActivated(object? sender, EventArgs e)
    {
        if (sender is DashboardChartHost { Card: { } card } && ViewModel is { } viewModel)
        {
            viewModel.OpenChartCommand.Execute(card);
        }
    }

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

        CustomDateToggle.SetCurrentValue(
            ToggleButton.IsCheckedProperty,
            viewModel.Filters.SelectedPreset == DateRangePreset.Custom);
    }

    private void OnFilterChipClick(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(sender, ClassFilterToggle) || ReferenceEquals(sender, SeverityFilterToggle))
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

    private void OnDefectImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        if (sender is Image { DataContext: DefectInfoItemViewModel item })
        {
            Serilog.Log.Warning(
                e.ErrorException,
                "Failed to load defect knowledge image for {DefectClass}: {ImagePath}",
                item.DisplayName,
                item.ImagePath);
        }
    }

    private void OnCardDragHandleMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DashboardMetricCardViewModel card })
        {
            _dragStart = e.GetPosition(this);
            _dragSource = card;
            e.Handled = true;
        }
    }

    private void OnCardDragHandleMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragSource is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        Point current = e.GetPosition(this);
        if (Math.Abs(current.X - _dragStart.X) < DragThreshold &&
            Math.Abs(current.Y - _dragStart.Y) < DragThreshold)
        {
            return;
        }

        var data = new DataObject(CardDragFormat, _dragSource);
        DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Move);
        _dragSource = null;
    }

    private void OnCardDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(CardDragFormat) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnCardDrop(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: DashboardMetricCardViewModel target } ||
            e.Data.GetData(CardDragFormat) is not DashboardMetricCardViewModel source ||
            ViewModel is not { } viewModel)
        {
            return;
        }

        int targetIndex = viewModel.MetricCards.IndexOf(target);
        if (targetIndex >= 0 && !ReferenceEquals(source, target))
        {
            _ = viewModel.MoveCardToAsync(source, targetIndex);
        }

        e.Handled = true;
    }
}
