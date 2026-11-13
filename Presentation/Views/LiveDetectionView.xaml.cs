using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using pcb_deftek.Presentation.ViewModels;

namespace pcb_deftek.Presentation.Views;

/// <summary>
/// Interaction logic for LiveDetectionView.xaml.
/// Implements stationary cursor idle detection (3-second auto-hide) and Spacebar snapshot capture.
/// </summary>
public partial class LiveDetectionView : UserControl
{
    /// <summary>
    /// Default stationary cursor duration before configuration controls fade out.
    /// </summary>
    public static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromSeconds(3);

    private static readonly Duration FadeOutDuration = new(TimeSpan.FromMilliseconds(350));
    private static readonly Duration FadeInDuration = new(TimeSpan.FromMilliseconds(150));

    private readonly DispatcherTimer _idleTimer;
    private Point _lastMousePosition;

    /// <summary>
    /// Gets a value indicating whether configuration controls are currently auto-hidden due to cursor inactivity.
    /// </summary>
    public bool IsControlsHidden { get; private set; }

    /// <summary>
    /// Gets the current opacity of the persistent status card (PASS/FAIL + defect count). Always 1.0.
    /// </summary>
    public double StatusCardOpacity => StatusHudCard.Opacity;

    /// <summary>
    /// Gets the current opacity of the telemetry HUD card.
    /// </summary>
    public double TelemetryCardOpacity { get; private set; } = 1.0;

    /// <summary>
    /// Gets the current opacity of the configuration HUD cards (Camera Selector, Confidence Slider, Capture Button).
    /// </summary>
    public double ConfigurationControlsOpacity { get; private set; } = 1.0;

    public LiveDetectionView()
    {
        InitializeComponent();

        _idleTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = DefaultIdleTimeout
        };
        _idleTimer.Tick += OnIdleTimerTick;
    }

    /// <summary>
    /// Transitions floating HUD controls into stationary cursor auto-hide state.
    /// Retains StatusHudCard (always) and TelemetryHudCard (only if telemetry toggle is enabled).
    /// </summary>
    /// <param name="animate">True to apply smooth opacity animation; false for immediate transition in tests.</param>
    public void TriggerIdleAutoHide(bool animate = true)
    {
        IsControlsHidden = true;

        var viewModel = DataContext as LiveDetectionViewModel;
        viewModel?.TriggerIdleAutoHide();

        bool isTelemetryEnabled = viewModel?.IsTelemetryEnabled ?? true;

        ConfigurationControlsOpacity = 0.0;
        TelemetryCardOpacity = isTelemetryEnabled ? 1.0 : 0.0;

        ApplyCardOpacity(CameraSelectorHudCard, 0.0, animate, FadeOutDuration);
        ApplyCardOpacity(ConfidenceSliderHudCard, 0.0, animate, FadeOutDuration);
        ApplyCardOpacity(CaptureActionHudCard, 0.0, animate, FadeOutDuration);

        if (isTelemetryEnabled)
        {
            ApplyCardOpacity(TelemetryHudCard, 1.0, animate, FadeInDuration);
            TelemetryToggleHeader.Opacity = 0.0;
            TelemetryToggleHeader.IsHitTestVisible = false;
        }
        else
        {
            ApplyCardOpacity(TelemetryHudCard, 0.0, animate, FadeOutDuration);
        }

        // Inviolable rule: Status card never fades out
        StatusHudCard.BeginAnimation(OpacityProperty, null);
        StatusHudCard.Opacity = 1.0;
    }

    /// <summary>
    /// Immediately restores all floating HUD controls to full visibility upon user cursor or keyboard activity.
    /// </summary>
    /// <param name="animate">True to apply smooth opacity animation; false for immediate transition in tests.</param>
    public void NotifyUserActivity(bool animate = true)
    {
        _idleTimer.Stop();
        _idleTimer.Start();

        if (!IsControlsHidden && ConfigurationControlsOpacity >= 1.0 && TelemetryCardOpacity >= 1.0)
        {
            return;
        }

        IsControlsHidden = false;

        var viewModel = DataContext as LiveDetectionViewModel;
        viewModel?.NotifyUserActivity();

        ConfigurationControlsOpacity = 1.0;
        TelemetryCardOpacity = 1.0;

        TelemetryToggleHeader.Opacity = 1.0;
        TelemetryToggleHeader.IsHitTestVisible = true;

        ApplyCardOpacity(TelemetryHudCard, 1.0, animate, FadeInDuration);
        ApplyCardOpacity(CameraSelectorHudCard, 1.0, animate, FadeInDuration);
        ApplyCardOpacity(ConfidenceSliderHudCard, 1.0, animate, FadeInDuration);
        ApplyCardOpacity(CaptureActionHudCard, 1.0, animate, FadeInDuration);

        StatusHudCard.BeginAnimation(OpacityProperty, null);
        StatusHudCard.Opacity = 1.0;
    }

    private static void ApplyCardOpacity(UIElement element, double targetOpacity, bool animate, Duration duration)
    {
        element.IsHitTestVisible = targetOpacity > 0.05;

        if (!animate)
        {
            element.BeginAnimation(OpacityProperty, null);
            element.Opacity = targetOpacity;
            return;
        }

        var animation = new DoubleAnimation(targetOpacity, duration)
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        element.BeginAnimation(OpacityProperty, animation);
    }

    private void OnIdleTimerTick(object? sender, EventArgs e)
    {
        _idleTimer.Stop();
        TriggerIdleAutoHide(animate: true);
    }

    private void OnViewLoaded(object sender, RoutedEventArgs e)
    {
        Focus();
        _idleTimer.Start();
    }

    private void OnViewUnloaded(object sender, RoutedEventArgs e)
    {
        _idleTimer.Stop();
    }

    private void OnViewportPreviewMouseMove(object sender, MouseEventArgs e)
    {
        var currentPos = e.GetPosition(this);
        if ((currentPos - _lastMousePosition).LengthSquared > 1.0)
        {
            _lastMousePosition = currentPos;
            NotifyUserActivity(animate: true);
        }
    }

    private void OnViewportPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        NotifyUserActivity(animate: true);
    }

    private void OnViewportPreviewKeyDown(object sender, KeyEventArgs e)
    {
        NotifyUserActivity(animate: true);

        if (e.Key == Key.Space &&
            DataContext is LiveDetectionViewModel viewModel &&
            viewModel.CaptureSnapshotCommand.CanExecute(null))
        {
            viewModel.CaptureSnapshotCommand.Execute(null);
            e.Handled = true;
        }
    }
}
