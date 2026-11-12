using System;
using System.ComponentModel;
using pcb_deftek.Presentation.ViewModels;

namespace pcb_deftek.Presentation.Navigation;

/// <summary>
/// Contract for orchestrating page navigation across application ViewModels.
/// </summary>
public interface INavigationService : INotifyPropertyChanged
{
    /// <summary>
    /// Gets the currently active view model being rendered.
    /// </summary>
    ViewModelBase? CurrentViewModel { get; }

    /// <summary>
    /// Gets a value indicating whether backwards navigation history exists.
    /// </summary>
    bool CanGoBack { get; }

    /// <summary>
    /// Event raised whenever navigation transitions to a new view model.
    /// </summary>
    event EventHandler<ViewModelBase>? Navigated;

    /// <summary>
    /// Navigates to a view model resolved by type from dependency injection.
    /// </summary>
    /// <typeparam name="TViewModel">The view model type to activate.</typeparam>
    void NavigateTo<TViewModel>() where TViewModel : ViewModelBase;

    /// <summary>
    /// Asynchronously navigates to a view model resolved by type from dependency injection, awaiting lifecycle hooks.
    /// </summary>
    Task NavigateToAsync<TViewModel>() where TViewModel : ViewModelBase;

    /// <summary>
    /// Navigates to the specified view model instance.
    /// </summary>
    /// <param name="viewModel">The view model instance to activate.</param>
    void NavigateTo(ViewModelBase viewModel);

    /// <summary>
    /// Asynchronously navigates to the specified view model instance, awaiting lifecycle hooks.
    /// </summary>
    Task NavigateToAsync(ViewModelBase viewModel);

    /// <summary>
    /// Navigates back to the previous view model in the navigation history.
    /// </summary>
    void GoBack();

    /// <summary>
    /// Asynchronously navigates back to the previous view model in the navigation history, awaiting lifecycle hooks.
    Task GoBackAsync();
}
