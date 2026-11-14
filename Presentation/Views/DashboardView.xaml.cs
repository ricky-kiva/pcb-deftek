using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using pcb_deftek.Presentation.Controls;
using pcb_deftek.Presentation.ViewModels;

namespace pcb_deftek.Presentation.Views;

public partial class DashboardView : UserControl
{
    private IInputElement? _focusBeforeModal;

    public DashboardView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    internal DashboardChartHost ModalHost => ModalChartHost;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        DetachViewModel(e.OldValue as DashboardViewModel);
        AttachViewModel(e.NewValue as DashboardViewModel);
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => AttachViewModel(DataContext as DashboardViewModel);

    private void OnUnloaded(object sender, RoutedEventArgs e) => DetachViewModel(DataContext as DashboardViewModel);

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
        if (e.PropertyName != nameof(DashboardViewModel.ActiveChartCard) ||
            DataContext is not DashboardViewModel viewModel)
        {
            return;
        }

        if (viewModel.IsChartModalOpen)
        {
            _focusBeforeModal ??= Keyboard.FocusedElement as IInputElement;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => ChartModalCloseButton.Focus()));
        }
        else
        {
            IInputElement? previousFocus = _focusBeforeModal;
            _focusBeforeModal = null;
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
                        Focus();
                    }
                }));
        }
    }

    private void OnChartModalPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is DashboardViewModel { IsChartModalOpen: true } viewModel)
        {
            viewModel.CloseChartCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnChartActivated(object? sender, EventArgs e)
    {
        if (sender is DashboardChartHost { Card: { } card } &&
            DataContext is DashboardViewModel viewModel)
        {
            viewModel.OpenChartCommand.Execute(card);
        }
    }
}
