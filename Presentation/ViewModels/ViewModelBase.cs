using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace pcb_deftek.Presentation.ViewModels;

/// <summary>
/// Abstract base class for all application view models, providing MVVM property change notifications
/// and view lifecycle transition hooks.
/// </summary>
public abstract class ViewModelBase : ObservableObject
{
    /// <summary>
    /// Gets the user-facing display title for this view model.
    /// </summary>
    public virtual string Title { get; } = string.Empty;

    /// <summary>
    /// Lifecycle callback invoked when this view model becomes the active navigation target.
    /// </summary>
    public virtual Task OnNavigatedToAsync() => Task.CompletedTask;

    /// <summary>
    /// Lifecycle callback invoked when this view model is being navigated away from.
    /// </summary>
    public virtual Task OnNavigatedFromAsync() => Task.CompletedTask;
}
