using System;
using System.IO;
using System.Threading;
using System.Windows;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using pcb_deftek.Infrastructure.Inference;
using pcb_deftek.Infrastructure.Notifications;
using pcb_deftek.Infrastructure.Persistence.Repositories;
using pcb_deftek.Infrastructure.Services;
using pcb_deftek.Presentation.ViewModels;
using pcb_deftek.Presentation.Views;
using Xunit;

namespace pcb_deftek.Tests;

public class LiveDetectionViewBehaviorTests
{
    private readonly Mock<ICameraService> _mockCamera = new();
    private readonly Mock<ILiveInspectionEngine> _mockEngine = new();
    private readonly Mock<ILiveSnapshotService> _mockSnapshot = new();
    private readonly Mock<IUserSettingsService> _mockSettings = new();
    private readonly Mock<INotificationService> _mockNotification = new();

    private LiveDetectionViewModel CreateViewModel()
    {
        return new LiveDetectionViewModel(
            _mockCamera.Object,
            _mockEngine.Object,
            _mockSnapshot.Object,
            _mockSettings.Object,
            _mockNotification.Object,
            NullLogger<LiveDetectionViewModel>.Instance);
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? capturedException = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                capturedException = ex;
            }
            finally
            {
                System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (capturedException != null)
        {
            throw new InvalidOperationException("STA test execution failed.", capturedException);
        }
    }
    [Fact]
    public void AutoHide_WhenCursorIdleAndTelemetryEnabled_HidesConfigControlsButKeepsStatusAndTelemetryVisible()
    {
        var vm = CreateViewModel();
        vm.IsTelemetryEnabled = true;

        // Initial state
        vm.IsControlsAutoHidden.Should().BeFalse();
        vm.StatusCardOpacity.Should().Be(1.0);
        vm.TelemetryCardOpacity.Should().Be(1.0);
        vm.ConfigurationControlsOpacity.Should().Be(1.0);

        // Act: Cursor becomes stationary for 3s
        vm.TriggerIdleAutoHide();

        // Assert: Config controls fade out, but Status card and enabled Telemetry card remain visible
        vm.IsControlsAutoHidden.Should().BeTrue();
        vm.StatusCardOpacity.Should().Be(1.0);
        vm.TelemetryCardOpacity.Should().Be(1.0);
        vm.ConfigurationControlsOpacity.Should().Be(0.0);

        // Act: Cursor moves again
        vm.NotifyUserActivity();

        // Assert: All controls restored
        vm.IsControlsAutoHidden.Should().BeFalse();
        vm.ConfigurationControlsOpacity.Should().Be(1.0);
        vm.TelemetryCardOpacity.Should().Be(1.0);
    }

    [Fact]
    public void AutoHide_WhenCursorIdleAndTelemetryDisabled_HidesBothConfigControlsAndTelemetryCard()
    {
        var vm = CreateViewModel();
        vm.IsTelemetryEnabled = false;

        // Act: Cursor becomes stationary for 3s
        vm.TriggerIdleAutoHide();

        // Assert: Status card remains visible (1.0), but Telemetry card and Config controls hide (0.0)
        vm.IsControlsAutoHidden.Should().BeTrue();
        vm.StatusCardOpacity.Should().Be(1.0);
        vm.TelemetryCardOpacity.Should().Be(0.0);
        vm.ConfigurationControlsOpacity.Should().Be(0.0);
    }

    [Fact]
    public void LiveDetectionView_RendersHudCardsAndAppliesStationaryAutoHideRules()
    {
        RunOnStaThread(() =>
        {
            var vm = CreateViewModel();
            vm.IsTelemetryEnabled = true;

            var view = new LiveDetectionView
            {
                DataContext = vm
            };

            view.StatusCardOpacity.Should().Be(1.0);
            view.ConfigurationControlsOpacity.Should().Be(1.0);
            view.TelemetryCardOpacity.Should().Be(1.0);

            // Trigger stationary cursor auto-hide on the view (without animation delay for test)
            view.TriggerIdleAutoHide(animate: false);

            view.IsControlsHidden.Should().BeTrue();
            view.StatusCardOpacity.Should().Be(1.0);
            view.TelemetryCardOpacity.Should().Be(1.0);
            view.ConfigurationControlsOpacity.Should().Be(0.0);

            // Disable telemetry while auto-hidden -> telemetry card should also hide
            vm.IsTelemetryEnabled = false;
            view.TriggerIdleAutoHide(animate: false);
            view.TelemetryCardOpacity.Should().Be(0.0);
            view.StatusCardOpacity.Should().Be(1.0);

            // Simulate user cursor movement -> config controls restore; telemetry readout stays hidden while disabled
            view.NotifyUserActivity(animate: false);

            view.IsControlsHidden.Should().BeFalse();
            view.StatusCardOpacity.Should().Be(1.0);
            view.TelemetryCardOpacity.Should().Be(0.0);
            view.ConfigurationControlsOpacity.Should().Be(1.0);
        });
    }
}
