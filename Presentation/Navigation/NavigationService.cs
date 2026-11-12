using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using pcb_deftek.Presentation.ViewModels;
using Serilog;

namespace pcb_deftek.Presentation.Navigation;

/// <summary>
/// Default implementation of <see cref="INavigationService"/> managing ViewModel resolution,
/// navigation history, and asynchronous lifecycle notifications.
/// </summary>
public partial class NavigationService : ObservableObject, INavigationService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Stack<ViewModelBase> _history = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoBack))]
    private ViewModelBase? _currentViewModel;

    /// <inheritdoc />
    public bool CanGoBack => _history.Count > 0;

    /// <inheritdoc />
    public event EventHandler<ViewModelBase>? Navigated;

    /// <summary>
    /// Initializes a new instance of <see cref="NavigationService"/>.
    /// </summary>
    /// <param name="serviceProvider">Service provider used to resolve requested ViewModel types.</param>
    public NavigationService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    /// <inheritdoc />
    public void NavigateTo<TViewModel>() where TViewModel : ViewModelBase =>
        _ = NavigateToAsync<TViewModel>();

    /// <inheritdoc />
    public async Task NavigateToAsync<TViewModel>() where TViewModel : ViewModelBase
    {
        var vm = _serviceProvider.GetRequiredService<TViewModel>();
        await NavigateToAsync(vm).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void NavigateTo(ViewModelBase viewModel) =>
        _ = NavigateToAsync(viewModel);

    /// <inheritdoc />
    public async Task NavigateToAsync(ViewModelBase viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        if (ReferenceEquals(CurrentViewModel, viewModel))
        {
            return;
        }

        var previousVm = CurrentViewModel;
        if (previousVm != null)
        {
            _history.Push(previousVm);
            try
            {
                await previousVm.OnNavigatedFromAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Exception in OnNavigatedFromAsync for {ViewModel}: {Message}",
                    previousVm.GetType().Name, ex.Message);
            }
        }

        CurrentViewModel = viewModel;
        OnPropertyChanged(nameof(CanGoBack));

        try
        {
            await viewModel.OnNavigatedToAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Exception in OnNavigatedToAsync for {ViewModel}: {Message}",
                viewModel.GetType().Name, ex.Message);
        }

        Navigated?.Invoke(this, viewModel);
    }

    /// <inheritdoc />
    public void GoBack() =>
        _ = GoBackAsync();

    /// <inheritdoc />
    public async Task GoBackAsync()
    {
        if (!CanGoBack)
        {
            return;
        }

        var previousVm = _history.Pop();
        var current = CurrentViewModel;

        if (current != null)
        {
            try
            {
                await current.OnNavigatedFromAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Exception in OnNavigatedFromAsync on GoBack for {ViewModel}: {Message}",
                    current.GetType().Name, ex.Message);
            }
        }

        CurrentViewModel = previousVm;
        OnPropertyChanged(nameof(CanGoBack));

        try
        {
            await previousVm.OnNavigatedToAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Exception in OnNavigatedToAsync on GoBack for {ViewModel}: {Message}",
                previousVm.GetType().Name, ex.Message);
        }

        Navigated?.Invoke(this, previousVm);
    }
}
